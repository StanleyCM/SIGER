using System.Globalization;
using System.Security.Claims;

namespace SIGER.API.Authorization.Policies;

public static class SigerClaims
{
    public const string Role = "siger_role";
    public const string UserId = "siger_user_id";
    public const string Administrator = "Administrator";
    public const string Waiter = "Waiter";
    public const string Cook = "Cook";
    public const string Cashier = "Cashier";
    public const string Client = "Client";

    public static long GetLocalUserId(this ClaimsPrincipal principal) =>
        long.Parse(principal.FindFirstValue(UserId) ??
            throw new InvalidOperationException("Local user identity is missing."), CultureInfo.InvariantCulture);
}
