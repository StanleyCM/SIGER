namespace SIGER.Desktop.Models;

internal sealed record SessionIdentity(long UserId, string DisplayName, string RoleName, DateTimeOffset ExpiresAt);
