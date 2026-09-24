namespace SIGER.API.Configuration;

public sealed class JwtSettings
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string JwksUrl { get; init; }
}
