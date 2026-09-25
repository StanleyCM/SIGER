using System.Globalization;
using System.Security.Claims;

namespace SIGER.API.Authorization.Policies;

public static class SigerClaims
{
    public const string Role = "siger_role";
    public const string UserId = "siger_user_id";
    // Canonical persisted Role.Name values from public.rol; technical identifiers stay in English.
    public const string Administrator = "Administrador";
    public const string Waiter = "Mesero";
    public const string Cook = "Cocinero";
    public const string Cashier = "Cajero";
    public const string Client = "Cliente";

    public static long GetLocalUserId(this ClaimsPrincipal principal) =>
        long.Parse(principal.FindFirstValue(UserId) ??
            throw new InvalidOperationException("Local user identity is missing."), CultureInfo.InvariantCulture);
}
