using Moq;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Auth;
using SIGER.Application.DTOs.Users;
using SIGER.Application.Exceptions;
using SIGER.Domain.Entities;
using SIGER.Domain.Exceptions;

namespace SIGER.Tests.Application;

public class UserCompensationTests
{
    [Fact]
    public async Task Creation_succeeds_in_both_systems()
    {
        var f = new RecoveryFixture(creation: true);
        var result = await f.F.UserService.CreateAsync(Create());
        Assert.True(result.IsSuccess); Assert.NotNull(f.Stored); Assert.NotNull(f.Remote);
        Assert.Equal(f.Remote.Id, f.Stored.AuthUserId); Assert.Equal(f.Remote.Email, f.Stored.Email);
        Assert.Equal(0, f.Deletes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Creation_compensates_DB_failure_even_when_request_cancelled(bool cancelled)
    {
        var f = new RecoveryFixture(creation: true) { FailSave = true };
        using var cts = new CancellationTokenSource(); f.CancelOnSave = cancelled ? cts : null;
        var error = await Assert.ThrowsAsync<UserOperationException>(() => f.F.UserService.CreateAsync(Create(), cts.Token));
        Assert.False(error.CompensationFailed); Assert.Null(f.Stored); Assert.Null(f.Remote); Assert.Equal(1, f.Deletes);
        Assert.False(f.RecoveryWasCancelled); Assert.DoesNotContain("PRIVATE", error.ToString());
    }

    [Fact]
    public async Task Creation_timeout_after_remote_commit_uses_reserved_UID_for_cleanup()
    {
        var f = new RecoveryFixture(creation: true) { LoseAuthResponse = true };
        var error = await Assert.ThrowsAsync<UserOperationException>(() => f.F.UserService.CreateAsync(Create()));
        Assert.False(error.CompensationFailed); Assert.Equal(1, f.Deletes); Assert.Null(f.Remote); Assert.Null(f.Stored);
    }

    [Fact]
    public async Task Precommit_database_outage_does_not_prevent_automatic_Auth_cleanup()
    {
        var f = new RecoveryFixture(creation: true) { FailSave = true };
        f.F.Users.Setup(x => x.GetByAuthUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("PRIVATE_DB_UNAVAILABLE"));
        var error = await Assert.ThrowsAsync<UserOperationException>(() => f.F.UserService.CreateAsync(Create()));
        Assert.False(error.CompensationFailed); Assert.Equal(1, f.Deletes); Assert.Null(f.Remote);
    }

    [Fact]
    public async Task Email_update_synchronizes_without_changing_auth_ID()
    {
        var f = new RecoveryFixture(); var id = f.Remote!.Id;
        var result = await f.F.UserService.UpdateAsync(1, Update());
        Assert.True(result.IsSuccess); Assert.Equal("new@example.test", f.Stored!.Email);
        Assert.Equal(f.Stored.Email, f.Remote!.Email); Assert.Equal(id, f.Stored.AuthUserId);
    }

    [Fact]
    public async Task Email_update_restores_Auth_if_database_fails()
    {
        var f = new RecoveryFixture { FailSave = true }; var before = f.Remote;
        var error = await Assert.ThrowsAsync<UserOperationException>(() => f.F.UserService.UpdateAsync(1, Update()));
        Assert.False(error.CompensationFailed); Assert.Equal(before, f.Remote); Assert.Equal(before!.Email, f.Stored!.Email);
        Assert.Equal(1, f.Restores);
    }

    [Fact]
    public async Task Auth_email_failure_does_not_change_database()
    {
        var f = new RecoveryFixture { FailAuth = true }; var before = f.Remote;
        var error = await Assert.ThrowsAsync<UserOperationException>(() => f.F.UserService.UpdateAsync(1, Update()));
        Assert.False(error.CompensationFailed); Assert.Equal(before, f.Remote); Assert.Equal(before!.Email, f.Stored!.Email);
        Assert.Equal(0, f.Saves); Assert.Equal(0, f.Restores);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Activation_and_deactivation_are_compensated_when_needed(bool active, bool fail)
    {
        var f = new RecoveryFixture(active: !active) { FailSave = fail }; var before = f.Remote;
        if (fail)
        {
            var error = await Assert.ThrowsAsync<UserOperationException>(() => f.F.UserService.UpdateStatusAsync(1, new() { IsActive = active }));
            Assert.False(error.CompensationFailed); Assert.Equal(before, f.Remote);
            Assert.Equal(!active, f.Stored!.IsActive); Assert.Equal(1, f.Restores);
        }
        else
        {
            Assert.True((await f.F.UserService.UpdateStatusAsync(1, new() { IsActive = active })).IsSuccess);
            Assert.Equal(active, f.Remote!.IsActive); Assert.Equal(active, f.Stored!.IsActive);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failed_compensation_is_critical_and_safe(bool create)
    {
        var f = new RecoveryFixture(creation: create) { FailSave = true, FailRecovery = true };
        var error = await Assert.ThrowsAsync<UserOperationException>(async () =>
        {
            if (create) await f.F.UserService.CreateAsync(Create()); else await f.F.UserService.UpdateAsync(1, Update());
        });
        Assert.True(error.CompensationFailed); Assert.Null(error.InnerException);
        Assert.DoesNotContain("PRIVATE", error.ToString());
        f.F.Reporter.Verify(x => x.ReportFailure(error.OperationId, It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<Guid>(),
            nameof(InvalidOperationException), nameof(HttpRequestException)), Times.Once);
    }

    [Fact]
    public async Task Compensation_never_overwrites_a_subsequent_Auth_change()
    {
        var f = new RecoveryFixture { FailSave = true, LaterAuthChange = true };
        var error = await Assert.ThrowsAsync<UserOperationException>(() => f.F.UserService.UpdateAsync(1, Update()));
        Assert.True(error.CompensationFailed); Assert.Equal("later@example.test", f.Remote!.Email); Assert.Equal(0, f.Restores);
    }

    [Fact]
    public async Task Lost_commit_acknowledgement_does_not_delete_a_persisted_identity()
    {
        var f = new RecoveryFixture(creation: true) { LoseCommitAcknowledgement = true };
        Assert.True((await f.F.UserService.CreateAsync(Create())).IsSuccess);
        Assert.NotNull(f.Stored); Assert.NotNull(f.Remote); Assert.Equal(0, f.Deletes);
    }

    [Theory]
    [InlineData("missing")] [InlineData("mismatch")]
    public async Task Invalid_Auth_profile_blocks_update_before_writes(string scenario)
    {
        var f = new RecoveryFixture();
        f.Remote = scenario == "missing" ? null : f.Remote! with { Email = "other@example.test" };
        await Assert.ThrowsAsync<BusinessRuleException>(() => f.F.UserService.UpdateAsync(1, Update()));
        Assert.Equal(0, f.Saves); Assert.Equal(0, f.Restores);
    }

    [Fact]
    public async Task Uncertain_Auth_update_does_not_blindly_overwrite_remote_state()
    {
        var f = new RecoveryFixture { LoseAuthResponse = true };
        var error = await Assert.ThrowsAsync<UserOperationException>(() => f.F.UserService.UpdateAsync(1, Update()));
        Assert.True(error.CompensationFailed); Assert.Equal(0, f.Restores); Assert.Equal(0, f.Saves);
    }

    private static CreateUserRequestDto Create() => new() { RoleId = 1, FirstName = "Test", LastName = "User", Email = "new@example.test", Password = "PRIVATE_PASSWORD" };
    private static UpdateUserRequestDto Update() => new() { RoleId = 1, FirstName = "Test", LastName = "User", Email = "new@example.test" };

    private sealed class RecoveryFixture
    {
        public ServiceFixture F { get; } = new();
        public User? Stored;
        public AuthUserStateDto? Remote;
        private User? working;
        public bool FailSave, FailAuth, FailRecovery, LaterAuthChange, LoseCommitAcknowledgement, LoseAuthResponse;
        public CancellationTokenSource? CancelOnSave;
        public int Restores, Deletes, Saves;
        public bool RecoveryWasCancelled;
        public RecoveryFixture(bool creation = false, bool active = true)
        {
            F.User.IsActive = active;
            Stored = creation ? null : Clone(F.User);
            Remote = creation ? null : F.AuthState with { BannedUntil = active ? null : DateTimeOffset.UtcNow.AddYears(1) };
            F.Users.Setup(x => x.GetByIdForUpdateAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(() => working = Clone(Stored));
            F.Users.Setup(x => x.GetByAuthUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Stored?.AuthUserId == id ? Clone(Stored) : null);
            F.Users.Setup(x => x.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .Callback<User, CancellationToken>((user, _) => { working = user; user.Id = 2; }).Returns(Task.CompletedTask);
            F.Provider.Setup(x => x.GetUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Remote?.Id == id ? Remote : null);
            F.Provider.Setup(x => x.CreateUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, Guid op, string email, string _, CancellationToken _) =>
                {
                    if (FailAuth) throw new HttpRequestException("PRIVATE_AUTH");
                    var now = DateTimeOffset.UtcNow; Remote = new(id, email, now, now, null, op);
                    if (LoseAuthResponse) throw new TaskCanceledException("PRIVATE_TIMEOUT");
                    return Remote;
                });
            F.Provider.Setup(x => x.UpdateEmailAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, Guid op, string email, CancellationToken _) => Change(op, email, Remote!.BannedUntil));
            F.Provider.Setup(x => x.SetActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, Guid op, bool enabled, CancellationToken _) => Change(op, Remote!.Email, enabled ? null : DateTimeOffset.UtcNow.AddYears(100)));
            F.Provider.Setup(x => x.RestoreUserAsync(It.IsAny<AuthUserStateDto>(), It.IsAny<AuthUserStateDto>(), It.IsAny<CancellationToken>()))
                .Callback<AuthUserStateDto, AuthUserStateDto, CancellationToken>((original, expected, token) =>
                {
                    RecoveryWasCancelled = token.IsCancellationRequested;
                    if (FailRecovery) throw new HttpRequestException("PRIVATE_RECOVERY");
                    Assert.Equal(expected, Remote); Remote = original; Restores++;
                }).Returns(Task.CompletedTask);
            F.Provider.Setup(x => x.DeleteCreatedUserAsync(It.IsAny<AuthUserStateDto>(), It.IsAny<CancellationToken>()))
                .Callback<AuthUserStateDto, CancellationToken>((expected, token) =>
                {
                    RecoveryWasCancelled = token.IsCancellationRequested;
                    if (FailRecovery) throw new HttpRequestException("PRIVATE_RECOVERY");
                    Assert.Equal(expected, Remote); Remote = null; Deletes++;
                }).Returns(Task.CompletedTask);
            F.Work.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            {
                Saves++; CancelOnSave?.Cancel();
                if (LaterAuthChange) Remote = Remote! with { Email = "later@example.test", UpdatedAt = Remote!.UpdatedAt.AddSeconds(1) };
                if (FailSave) throw new InvalidOperationException("PRIVATE_DATABASE_CONNECTION_STRING");
                Stored = Clone(working); return 1;
            });
            Transaction<Result<UserDto>>(); Transaction<Result>();
        }
        private AuthUserStateDto Change(Guid operation, string email, DateTimeOffset? banned)
        {
            if (FailAuth) throw new HttpRequestException("PRIVATE_AUTH");
            Remote = Remote! with { Email = email, BannedUntil = banned, OperationId = operation, UpdatedAt = Remote!.UpdatedAt.AddSeconds(1) };
            if (LoseAuthResponse) throw new TaskCanceledException("PRIVATE_TIMEOUT");
            return Remote;
        }
        private void Transaction<T>() => F.Work
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<T>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<T>> action, CancellationToken token) =>
            {
                var before = Clone(Stored);
                try
                {
                    var result = await action(token);
                    if (LoseCommitAcknowledgement) throw new IOException("PRIVATE_COMMIT_ACK");
                    return result;
                }
                catch { if (!LoseCommitAcknowledgement) Stored = before; throw; }
            });
        private static User? Clone(User? u) => u is null ? null : new()
        { Id = u.Id, AuthUserId = u.AuthUserId, RoleId = u.RoleId, Role = u.Role, FirstName = u.FirstName,
            LastName = u.LastName, Email = u.Email, Phone = u.Phone, IsActive = u.IsActive, CreatedAt = u.CreatedAt, UpdatedAt = u.UpdatedAt };
    }
}
