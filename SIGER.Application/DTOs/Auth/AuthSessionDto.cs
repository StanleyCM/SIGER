namespace SIGER.Application.DTOs.Auth;

public sealed class AuthSessionDto
{
    public Guid AuthUserId { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
