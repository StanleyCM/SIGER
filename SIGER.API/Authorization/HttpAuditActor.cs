using SIGER.API.Authorization.Policies;
using SIGER.Application.Interfaces.Persistence;

namespace SIGER.API.Authorization;

public sealed class HttpAuditActor(IHttpContextAccessor accessor) : IAuditActor
{
    public long? UserId => accessor.HttpContext?.User.Identity?.IsAuthenticated == true
        ? accessor.HttpContext.User.GetLocalUserId() : null;

    // Forwarded headers are not interpreted here. Only the server's established connection address.
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
