using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SIGER.API.Authorization;
using SIGER.API.Authorization.Policies;
using SIGER.API.Controllers;

namespace SIGER.Tests.API;

public class AuditSecurityTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Mesero", HttpStatusCode.Forbidden)]
    [InlineData("Administrador", HttpStatusCode.OK)]
    public async Task Audit_queries_require_administrator(string? role, HttpStatusCode expected)
    {
        using var f = new ApiFactory(); using var c = f.Client(role);
        using var response = await c.GetAsync("/api/v1/audits"); Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task No_audit_write_endpoint_exists()
    {
        var verbs = typeof(AuditsController).GetMethods().SelectMany(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), true).Cast<HttpMethodAttribute>()).ToArray();
        Assert.Equal(2, verbs.Length); Assert.All(verbs, v => Assert.IsType<HttpGetAttribute>(v));
        using var f = new ApiFactory(); using var c = f.Client("Administrador");
        using var response = await c.PostAsync("/api/v1/audits", null); Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public void Actor_uses_local_claim_not_client_input_and_does_not_trust_forwarded_headers()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SigerClaims.UserId, "42")], "Bearer"));
        context.Request.Headers["X-Forwarded-For"] = "192.0.2.99";
        context.Request.QueryString = new QueryString("?userId=999");
        context.Connection.RemoteIpAddress = IPAddress.Parse("::1");
        var actor = new HttpAuditActor(new HttpContextAccessor { HttpContext = context });
        Assert.Equal(42, actor.UserId); Assert.Equal("::1", actor.IpAddress);
    }

    [Fact]
    public void Unauthenticated_claim_and_missing_request_do_not_invent_an_actor()
    {
        var accessor = new HttpContextAccessor(); var actor = new HttpAuditActor(accessor);
        Assert.Null(actor.UserId); Assert.Null(actor.IpAddress);
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SigerClaims.UserId, "999")])) };
        Assert.Null(actor.UserId); Assert.Null(actor.IpAddress);
    }

    [Fact]
    public void Authenticated_request_without_local_identity_fails_instead_of_anonymous_audit()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer")) };
        var actor = new HttpAuditActor(new HttpContextAccessor { HttpContext = context });
        Assert.Throws<InvalidOperationException>(() => actor.UserId);
    }
}
