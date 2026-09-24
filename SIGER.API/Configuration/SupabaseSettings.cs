namespace SIGER.API.Configuration;

public sealed class SupabaseSettings
{
    public const string SectionName = "Supabase";
    public string Url { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string? JwtIssuer { get; set; }
    public string? JwtAudience { get; set; }
}
