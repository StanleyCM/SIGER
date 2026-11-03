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

}
