using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SIGER.Application.DTOs.Auth;
using SIGER.Domain.Exceptions;
using SIGER.Infrastructure.Authentication;

namespace SIGER.Tests.Infrastructure;

public class AuthCompensationTests
{
    private static AuthUserStateDto State() => new(Guid.NewGuid(), "old@example.test", DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddDays(-1), null, Guid.NewGuid());
    private static string Json(AuthUserStateDto state) => JsonSerializer.Serialize(new
    { id = state.Id, email = state.Email, updated_at = state.UpdatedAt, created_at = state.CreatedAt,
        banned_until = state.BannedUntil, app_metadata = new { siger_operation_id = state.OperationId } });

    [Fact]
    public async Task Email_update_is_administrative_without_forcing_confirmation()
    {
        var state = State() with { Email = "new@example.test" };
        using var h = new Handler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json(state)) });
        using var client = Client(h);
        var result = await new SupabaseAuthService(client).UpdateEmailAsync(state.Id, state.OperationId!.Value, state.Email);
        Assert.Equal(state, result);
        var request = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith($"/admin/users/{state.Id}", request.Path);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(state.Email, body.RootElement.GetProperty("email").GetString());
        Assert.False(body.RootElement.TryGetProperty("email_confirm", out _));
        Assert.False(body.RootElement.TryGetProperty("password", out _));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Restore_checks_snapshot_then_restores_only_changed_fields(bool status)
    {
        var original = State();
        var changed = status ? original with { BannedUntil = DateTimeOffset.UtcNow.AddYears(100), UpdatedAt = original.UpdatedAt.AddSeconds(1) }
            : original with { Email = "new@example.test", UpdatedAt = original.UpdatedAt.AddSeconds(1) };
        using var h = new Handler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json(changed)) },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json(original)) });
        using var client = Client(h);
        await new SupabaseAuthService(client).RestoreUserAsync(original, changed);
        Assert.Equal(HttpMethod.Get, h.Requests[0].Method); Assert.Equal(HttpMethod.Put, h.Requests[1].Method);
        using var body = JsonDocument.Parse(h.Requests[1].Body!);
        if (status) { Assert.Equal("none", body.RootElement.GetProperty("ban_duration").GetString()); Assert.False(body.RootElement.TryGetProperty("email", out _)); }
        else { Assert.Equal(original.Email, body.RootElement.GetProperty("email").GetString()); Assert.False(body.RootElement.TryGetProperty("ban_duration", out _)); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Later_remote_change_prevents_restore_or_delete(bool delete)
    {
        var expected = State(); var later = expected with { UpdatedAt = expected.UpdatedAt.AddSeconds(1) };
        using var h = new Handler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json(later)) });
        using var client = Client(h); var service = new SupabaseAuthService(client);
        await Assert.ThrowsAsync<BusinessRuleException>(() => delete ? service.DeleteCreatedUserAsync(expected) : service.RestoreUserAsync(expected, expected));
        Assert.Equal(HttpMethod.Get, Assert.Single(h.Requests).Method);
    }

    [Fact]
    public async Task Creation_cleanup_deletes_only_the_verified_identity()
    {
        var expected = State();
        using var h = new Handler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json(expected)) }, new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(h);
        await new SupabaseAuthService(client).DeleteCreatedUserAsync(expected);
        Assert.Equal(HttpMethod.Get, h.Requests[0].Method); Assert.Equal(HttpMethod.Delete, h.Requests[1].Method);
        Assert.EndsWith(expected.Id.ToString(), h.Requests[1].Path);
    }

    [Fact]
    public async Task Missing_Auth_identity_is_not_successfully_updated()
    {
        using var h = new Handler(new HttpResponseMessage(HttpStatusCode.NotFound)); using var client = Client(h);
        Assert.Null(await new SupabaseAuthService(client).GetUserAsync(Guid.NewGuid()));
    }

    [Fact]
    public void Failed_compensation_emits_structured_critical_without_exception_payloads()
    {
        var logger = new Mock<ILogger<UserOperationReporter>>(); var operation = Guid.NewGuid();
        new UserOperationReporter(logger.Object).ReportFailure(operation, "Update", 1, Guid.NewGuid(), "DbUpdateException", "HttpRequestException");
        logger.Verify(x => x.Log(LogLevel.Critical, It.Is<EventId>(id => id.Id == 6201),
            It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains(operation.ToString()) && !value.ToString()!.Contains("password")),
            null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static HttpClient Client(HttpMessageHandler h) => new(h) { BaseAddress = new("https://test.invalid/auth/v1/") };
    private sealed class Handler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int next;
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add((r.Method, r.RequestUri!.AbsolutePath, r.Content is null ? null : await r.Content.ReadAsStringAsync(token)));
            return responses[next++];
        }
    }
}
