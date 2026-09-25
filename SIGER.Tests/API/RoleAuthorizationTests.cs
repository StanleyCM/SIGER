using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SIGER.API.Authorization;
using SIGER.API.Authorization.Policies;

namespace SIGER.Tests.API;

public class RoleAuthorizationTests
{
    // Literal expectations intentionally independent of SigerClaims constants: these are DB names.
    [Theory]
    [InlineData("AdministratorOnly", new[] { "Administrador" })]
    [InlineData("StaffOnly", new[] { "Administrador", "Mesero", "Cocinero", "Cajero" })]
    [InlineData("WaiterOrAdministrator", new[] { "Mesero", "Administrador" })]
    [InlineData("KitchenOrAdministrator", new[] { "Cocinero", "Administrador" })]
    [InlineData("CashierOrAdministrator", new[] { "Cajero", "Administrador" })]
    [InlineData("Reservations", new[] { "Administrador", "Mesero", "Cliente" })]
    public async Task Policies_use_exact_persisted_role_names(string name, string[] expectedRoles)
    {
        using var factory = new ApiFactory();
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetPolicyAsync(name);
        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        var roleRequirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(expectedRoles.Order(), roleRequirement.AllowedRoles.Order());
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Mesero")]
    [InlineData("Cocinero")]
    [InlineData("Cajero")]
    [InlineData("Cliente")]
    public async Task Enrichment_preserves_one_local_role_and_local_user_identity(string persistedRole)
    {
        using var factory = new ApiFactory();
        factory.LocalUser.Role.Name = persistedRole;
        var context = new TokenValidatedContext(new DefaultHttpContext(),
            new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", factory.LocalUser.AuthUserId.ToString()),
                new Claim(SigerClaims.Role, "untrusted-token-role"),
                new Claim("role", "untrusted-provider-role"),
                new Claim(SigerClaims.UserId, "999")
            }, "Bearer"))
        };
        await new SigerJwtBearerEvents(factory.Users.Object).TokenValidated(context);
        var principal = Assert.IsType<ClaimsPrincipal>(context.Principal);
        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal(persistedRole, Assert.Single(principal.FindAll(SigerClaims.Role)).Value);
        Assert.Equal("42", Assert.Single(principal.FindAll(SigerClaims.UserId)).Value);
        Assert.Equal(factory.LocalUser.AuthUserId.ToString(), principal.FindFirstValue("sub"));
        Assert.True(principal.IsInRole(persistedRole));
        Assert.DoesNotContain(principal.Claims, claim => claim.Type == "role" || claim.Value.StartsWith("untrusted-"));
        Assert.Equal(4, principal.Claims.Count());
    }
}
