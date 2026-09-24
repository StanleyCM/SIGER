using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIGER.Application.DTOs.Auth;
using SIGER.Application.Interfaces.Services;

namespace SIGER.Infrastructure.Authentication;

public sealed class SupabaseAuthService(HttpClient httpClient) : IAuthProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AuthSessionDto?> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "token?grant_type=password",
            new SignInRequest(email, password),
            JsonOptions,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            return null;
        }

        await EnsureSuccessAsync(response);

        var session = await ReadResponseAsync<AuthResponse>(response, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (session.User is null || session.User.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(session.AccessToken) || session.ExpiresIn <= 0 ||
            session.ExpiresIn >= (DateTimeOffset.MaxValue - now).TotalSeconds)
        {
            throw new HttpRequestException("Supabase returned an invalid authentication response.");
        }

        return new AuthSessionDto
        {
            AuthUserId = session.User.Id,
            AccessToken = session.AccessToken,
            ExpiresAt = now.AddSeconds(session.ExpiresIn)
        };
    }

    public async Task<Guid> CreateUserAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "admin/users",
            new CreateUserRequest(email, password, true),
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response);
        var user = await ReadResponseAsync<UserResponse>(response, cancellationToken);
        if (user.Id == Guid.Empty)
        {
            throw new HttpRequestException("Supabase returned an invalid user creation response.");
        }

        return user.Id;
    }

    public Task EnableUserAsync(Guid authUserId, CancellationToken cancellationToken = default) =>
        SetBanDurationAsync(authUserId, "none", cancellationToken);

    public Task DisableUserAsync(Guid authUserId, CancellationToken cancellationToken = default) =>
        SetBanDurationAsync(authUserId, "876000h", cancellationToken);

    private async Task SetBanDurationAsync(
        Guid authUserId,
        string duration,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"admin/users/{authUserId:D}",
            new UpdateUserRequest(duration),
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new HttpRequestException("Supabase returned an empty response.");
        }
        catch (JsonException)
        {
            // Parser errors can include remote payload fragments. Do not retain their message or inner exception.
            throw new HttpRequestException("Supabase returned an invalid response.");
        }
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Supabase authentication request failed with HTTP status {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        return Task.CompletedTask;
    }

    private sealed record SignInRequest(string Email, string Password);

    private sealed record CreateUserRequest(
        string Email,
        string Password,
        [property: JsonPropertyName("email_confirm")] bool EmailConfirm);

    private sealed record UpdateUserRequest(
        [property: JsonPropertyName("ban_duration")] string BanDuration);

    private sealed class AuthResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; init; }

        public UserResponse User { get; init; } = new();
    }

    private sealed class UserResponse
    {
        public Guid Id { get; init; }
    }
}
