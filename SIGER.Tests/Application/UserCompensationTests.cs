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
    public async Task Email_update_restores_Auth_if_database_fails()
    {
        var f = new RecoveryFixture { FailSave = true }; var before = f.Remote;
        var error = await Assert.ThrowsAsync<UserOperationException>(() => f.F.UserService.UpdateAsync(1, Update()));
        Assert.False(error.CompensationFailed); Assert.Equal(before, f.Remote); Assert.Equal(before!.Email, f.Stored!.Email);
        Assert.Equal(1, f.Restores);
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

    private static CreateUserRequestDto Create() => new() { RoleId = 1, FirstName = "Test", LastName = "User", Email = "new@example.test", Password = "PRIVATE_PASSWORD" };
    private static UpdateUserRequestDto Update() => new() { RoleId = 1, FirstName = "Test", LastName = "User", Email = "new@example.test" };

    private sealed class RecoveryFixture
    {
        public ServiceFixture F { get; } = new();
        public User? Stored;
        public AuthUserStateDto? Remote;
        private User? working;
        public bool FailSave, LaterAuthChange, LoseCommitAcknowledgement;
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
                    var now = DateTimeOffset.UtcNow; Remote = new(id, email, now, now, null, op);
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
                    Assert.Equal(expected, Remote); Remote = original; Restores++;
                }).Returns(Task.CompletedTask);
            F.Provider.Setup(x => x.DeleteCreatedUserAsync(It.IsAny<AuthUserStateDto>(), It.IsAny<CancellationToken>()))
                .Callback<AuthUserStateDto, CancellationToken>((expected, token) =>
                {
                    RecoveryWasCancelled = token.IsCancellationRequested;
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
            Remote = Remote! with { Email = email, BannedUntil = banned, OperationId = operation, UpdatedAt = Remote!.UpdatedAt.AddSeconds(1) };
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
