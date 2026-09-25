using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIGER.Application.DTOs.Auth;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Exceptions;

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

    public async Task<AuthUserStateDto> CreateUserAsync(Guid authUserId, Guid operationId,
        string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "admin/users",
            new { id = authUserId, email, password, email_confirm = true, app_metadata = new { siger_operation_id = operationId } },
            JsonOptions,
            cancellationToken);

        await EnsureAdminSuccessAsync(response);
        return await ReadStateAsync(response, authUserId, cancellationToken);
    }

    public async Task<AuthUserStateDto?> GetUserAsync(Guid authUserId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"admin/users/{authUserId:D}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureAdminSuccessAsync(response);
        return await ReadStateAsync(response, authUserId, cancellationToken);
    }

    public Task<AuthUserStateDto> UpdateEmailAsync(Guid authUserId, Guid operationId, string email, CancellationToken cancellationToken = default)
        // Administrative update is immediate by Supabase contract. Do not send email_confirm or change project settings.
        => UpdateAsync(authUserId, new { email, app_metadata = new { siger_operation_id = operationId } }, cancellationToken);

    public Task<AuthUserStateDto> SetActiveAsync(Guid authUserId, Guid operationId, bool isActive, CancellationToken cancellationToken = default)
        => UpdateAsync(authUserId, new { ban_duration = isActive ? "none" : "876000h", app_metadata = new { siger_operation_id = operationId } }, cancellationToken);

    public async Task RestoreUserAsync(AuthUserStateDto original, AuthUserStateDto expected, CancellationToken cancellationToken = default)
    {
        var current = await GetUserAsync(expected.Id, cancellationToken);
        if (current != expected) throw new BusinessRuleException("Auth state changed; compensation will not overwrite it.");
        var payload = new Dictionary<string, object?>();
        if (original.Email != expected.Email) payload["email"] = original.Email;
        if (original.BannedUntil != expected.BannedUntil)
            payload["ban_duration"] = original.IsActive ? "none" :
                Math.Ceiling((original.BannedUntil!.Value - DateTimeOffset.UtcNow).TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s";
        payload["app_metadata"] = new { siger_operation_id = original.OperationId };
        var restored = await UpdateAsync(expected.Id, payload, cancellationToken);
        if (!string.Equals(restored.Email, original.Email, StringComparison.OrdinalIgnoreCase) || restored.IsActive != original.IsActive)
            throw new HttpRequestException("Auth compensation could not be verified.");
    }

    public async Task DeleteCreatedUserAsync(AuthUserStateDto expected, CancellationToken cancellationToken = default)
    {
        var current = await GetUserAsync(expected.Id, cancellationToken);
        if (current is null) return;
        if (current != expected || expected.OperationId is null)
            throw new BusinessRuleException("Auth state changed; compensation will not delete it.");
        using var response = await httpClient.DeleteAsync($"admin/users/{expected.Id:D}", cancellationToken);
        await EnsureAdminSuccessAsync(response);
    }

    private async Task<AuthUserStateDto> UpdateAsync(Guid id, object payload, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync($"admin/users/{id:D}", payload, JsonOptions, cancellationToken);
        await EnsureAdminSuccessAsync(response);
        return await ReadStateAsync(response, id, cancellationToken);
    }

    private static async Task<AuthUserStateDto> ReadStateAsync(HttpResponseMessage response, Guid expectedId, CancellationToken token)
    {
        var user = await ReadResponseAsync<UserResponse>(response, token);
        if (user.Id == Guid.Empty || user.Id != expectedId || string.IsNullOrWhiteSpace(user.Email) || user.UpdatedAt == default || user.CreatedAt == default)
            throw new HttpRequestException("Supabase returned an invalid user state.");
        Guid? operation = user.AppMetadata is not null && user.AppMetadata.TryGetValue("siger_operation_id", out var value) && value.ValueKind == JsonValueKind.String &&
            Guid.TryParse(value.GetString(), out var parsed) ? parsed : null;
        return new(user.Id, user.Email, user.UpdatedAt, user.CreatedAt, user.BannedUntil, operation);
    }

    private static Task EnsureAdminSuccessAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            throw new BusinessRuleException("Auth rejected the user operation; check the requested identity and values.");
        return EnsureSuccessAsync(response);
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
        public string Email { get; init; } = string.Empty;
        [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
        [JsonPropertyName("banned_until")] public DateTimeOffset? BannedUntil { get; init; }
        [JsonPropertyName("app_metadata")] public Dictionary<string, JsonElement> AppMetadata { get; init; } = [];
    }
}
