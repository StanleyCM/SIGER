using SIGER.Desktop.Models;

namespace SIGER.Desktop.Services;

internal sealed class SessionService(TimeProvider clock)
{
    private SessionCredential? current;

    internal SessionIdentity? Current => GetCredential()?.Identity;

    internal SessionCredential? GetCredential()
    {
        while (Volatile.Read(ref current) is { } credential)
        {
            if (credential.Identity.ExpiresAt > clock.GetUtcNow()) return credential;
            Invalidate(credential);
        }

        return null;
    }

    internal bool TryStart(SessionIdentity identity, string accessToken)
    {
        if (identity.ExpiresAt <= clock.GetUtcNow()) return false;
        Volatile.Write(ref current, new SessionCredential(identity, accessToken));
        return true;
    }

    internal void SignOut() => Interlocked.Exchange(ref current, null);

    internal void Invalidate(SessionCredential credential) =>
        Interlocked.CompareExchange(ref current, null, credential);
}

internal sealed class SessionCredential(SessionIdentity identity, string accessToken)
{
    internal SessionIdentity Identity { get; } = identity;
    internal string AccessToken { get; } = accessToken;
}
