using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Interfaces.Repositories;

namespace SIGER.API.Authorization;

public sealed class SigerJwtBearerEvents(IUserRepository users) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var authUserId))
        {
            context.Fail("Invalid subject.");
            return;
        }

        // JwtBearer authenticates once per request. The repository includes Role in this query.
        var user = await users.GetByAuthUserIdAsync(authUserId, context.HttpContext.RequestAborted);
        if (user is null || !user.IsActive || user.Role is null || !user.Role.IsActive)
        {
            context.Fail("Local user or role is inactive or unavailable.");
            return;
        }

        // Build a trusted local identity: token role/user metadata never grants SIGER permissions.
        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", authUserId.ToString()),
            new Claim(SigerClaims.UserId, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new Claim(SigerClaims.Role, user.Role.Name)
        ], context.Scheme.Name, ClaimTypes.Name, SigerClaims.Role));
    }

    public override async Task Challenge(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await ApiProblems.WriteAsync(context.HttpContext, 401, "Authentication required.",
            "A valid Supabase access token and an active SIGER profile are required.");
    }

    public override Task Forbidden(ForbiddenContext context) =>
        ApiProblems.WriteAsync(context.HttpContext, 403, "Forbidden.", "You do not have permission for this operation.");
}
