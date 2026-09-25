using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using SIGER.Infrastructure.Authentication;
using SIGER.Infrastructure.Persistence;
using Work = SIGER.Infrastructure.UnitOfWork.UnitOfWork;

namespace SIGER.Tests.Infrastructure;

public class AuthProviderTests
{
    private sealed class Handler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public HttpMethod? Method { get; private set; }
        public CancellationToken Token { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Token = token; token.ThrowIfCancellationRequested();
            Path = request.RequestUri!.PathAndQuery; Method = request.Method;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            return new(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Login_maps_provider_session_and_sends_required_contract()
    {
        var id = Guid.NewGuid();
        using var handler = new Handler(HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "opaque-test", expires_in = 3600, user = new { id } }));
        using var client = new HttpClient(handler) { BaseAddress = new("https://test.invalid/auth/v1/") };
        using var cts = new CancellationTokenSource();
        var before = DateTimeOffset.UtcNow;
        var session = await new SupabaseAuthService(client).SignInAsync("a@example.test", "synthetic-input", cts.Token);
        Assert.Equal(id, session!.AuthUserId); Assert.Equal("opaque-test", session.AccessToken);
        Assert.InRange(session.ExpiresAt, before.AddHours(1), DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal("/auth/v1/token?grant_type=password", handler.Path);
        Assert.Equal(HttpMethod.Post, handler.Method); Assert.True(handler.Token.CanBeCanceled);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("a@example.test", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("synthetic-input", body.RootElement.GetProperty("password").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)] [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Rejected_login_returns_no_session(HttpStatusCode status)
    {
        using var client = new HttpClient(new Handler(status, "remote-private-content")) { BaseAddress = new("https://test.invalid/") };
        Assert.Null(await new SupabaseAuthService(client).SignInAsync("a", "b"));
    }

    [Theory]
    [InlineData("{}")] [InlineData("null")] [InlineData("{\"user\":null}")]
    [InlineData("{\"access_token\":\"x\",\"expires_in\":3600,\"user\":{}}")]
    [InlineData("not-json")]
    public async Task Malformed_login_is_rejected_with_sanitized_error(string response)
    {
        using var client = new HttpClient(new Handler(HttpStatusCode.OK, response)) { BaseAddress = new("https://test.invalid/") };
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new SupabaseAuthService(client).SignInAsync("a", "b"));
        Assert.DoesNotContain(response, error.Message); Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Create_user_maps_id_and_admin_contract()
    {
        var id = Guid.NewGuid();
        using var handler = new Handler(HttpStatusCode.OK, JsonSerializer.Serialize(new { id, email = "a@example.test", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow }));
        using var client = new HttpClient(handler) { BaseAddress = new("https://test.invalid/auth/v1/") };
        Assert.Equal(id, (await new SupabaseAuthService(client).CreateUserAsync(id, Guid.NewGuid(), "a@example.test", "synthetic-input")).Id);
        Assert.Equal("/auth/v1/admin/users", handler.Path);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.True(body.RootElement.GetProperty("email_confirm").GetBoolean());
    }

    [Theory]
    [InlineData("{}")] [InlineData("null")] [InlineData("not-json")]
    public async Task Invalid_create_response_cannot_create_empty_local_identity(string response)
    {
        using var client = new HttpClient(new Handler(HttpStatusCode.OK, response)) { BaseAddress = new("https://test.invalid/") };
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new SupabaseAuthService(client).CreateUserAsync(Guid.NewGuid(), Guid.NewGuid(), "a", "b"));
        Assert.DoesNotContain(response, error.Message); Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false, "876000h")] [InlineData(true, "none")]
    public async Task Enable_disable_use_admin_update(bool enable, string duration)
    {
        var id = Guid.NewGuid();
        using var handler = new Handler(HttpStatusCode.OK, JsonSerializer.Serialize(new { id, email = "a@example.test", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow }));
        using var client = new HttpClient(handler) { BaseAddress = new("https://test.invalid/auth/v1/") };
        var service = new SupabaseAuthService(client);
        await service.SetActiveAsync(id, Guid.NewGuid(), enable);
        Assert.Equal(HttpMethod.Put, handler.Method); Assert.Equal($"/auth/v1/admin/users/{id:D}", handler.Path);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(duration, body.RootElement.GetProperty("ban_duration").GetString());
    }

    [Fact]
    public async Task Remote_errors_do_not_expose_remote_body()
    {
        using var client = new HttpClient(new Handler(HttpStatusCode.InternalServerError, "PRIVATE_REMOTE_CONTENT")) { BaseAddress = new("https://test.invalid/") };
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new SupabaseAuthService(client).CreateUserAsync(Guid.NewGuid(), Guid.NewGuid(), "a", "b"));
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.DoesNotContain("PRIVATE_REMOTE_CONTENT", error.ToString());
    }

    [Fact]
    public async Task Cancellation_is_not_converted_to_authentication_success()
    {
        using var client = new HttpClient(new Handler(HttpStatusCode.OK, "{}")) { BaseAddress = new("https://test.invalid/") };
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SupabaseAuthService(client).SignInAsync("a", "b", cts.Token));
    }
}

public class UnitOfWorkTests
{
    private readonly Mock<SIGERDbContext> context = new(new DbContextOptionsBuilder<SIGERDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=never_connect").Options);
    private readonly Mock<IDbContextTransaction> transaction = new();
    private readonly Mock<DatabaseFacade> database;
    public UnitOfWorkTests()
    {
        database = new(context.Object);
        context.SetupGet(x => x.Database).Returns(database.Object);
        database.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
    }

    [Fact]
    public async Task Save_delegates_cancellation_and_result()
    {
        using var cts = new CancellationTokenSource();
        context.Setup(x => x.SaveChangesAsync(cts.Token)).ReturnsAsync(7);
        Assert.Equal(7, await new Work(context.Object).SaveChangesAsync(cts.Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Transaction_commits_and_disposes_after_success(bool generic)
    {
        using var cts = new CancellationTokenSource();
        var work = new Work(context.Object);
        if (generic) Assert.Equal(42, await work.ExecuteInTransactionAsync(t => { Assert.Equal(cts.Token, t); return Task.FromResult(42); }, cts.Token));
        else await work.ExecuteInTransactionAsync(t => { Assert.Equal(cts.Token, t); return Task.CompletedTask; }, cts.Token);
        transaction.Verify(x => x.CommitAsync(cts.Token), Times.Once);
        transaction.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Transaction_rolls_back_and_preserves_operation_error(bool generic)
    {
        var failure = new InvalidOperationException("simulated");
        var work = new Work(context.Object);
        var actual = generic
            ? await Assert.ThrowsAsync<InvalidOperationException>(() => work.ExecuteInTransactionAsync<int>(_ => Task.FromException<int>(failure)))
            : await Assert.ThrowsAsync<InvalidOperationException>(() => work.ExecuteInTransactionAsync(_ => Task.FromException(failure)));
        Assert.Same(failure, actual);
        transaction.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
        transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Nested_work_does_not_commit_owners_transaction()
    {
        database.SetupGet(x => x.CurrentTransaction).Returns(transaction.Object);
        var work = new Work(context.Object);
        await work.ExecuteInTransactionAsync(_ => Task.CompletedTask);
        Assert.Equal(1, await work.ExecuteInTransactionAsync(_ => Task.FromResult(1)));
        database.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Null_operations_are_rejected_before_begin()
    {
        var work = new Work(context.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() => work.ExecuteInTransactionAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => work.ExecuteInTransactionAsync<int>(null!));
        database.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
