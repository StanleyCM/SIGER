using System.Net;
using SIGER.Application.DTOs.Auth;
using SIGER.Desktop.Models;

namespace SIGER.Desktop.Services;

internal sealed class AuthApiService(ApiClient api, SessionService session)
{
    internal async Task SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        LoginResponseDto response;
        try
        {
            response = await api.SendAsync<LoginResponseDto>(HttpMethod.Post, "api/v1/auth/login",
                new LoginRequestDto { Email = email.Trim(), Password = password }, authenticated: false,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ApiException("El correo o la contraseña no son válidos, o tu cuenta está inactiva.", error.StatusCode);
        }

        if (response.User is not { Id: > 0, IsActive: true } user
            || string.IsNullOrWhiteSpace(response.AccessToken)
            || response.AccessToken.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
            throw new ApiException("SIGER devolvió una sesión inválida. Inténtalo de nuevo.");

        if (RoleNavigation.For(user.RoleName) is null)
            throw new ApiException("Acceso denegado. Tu rol no está habilitado para SIGER Desktop.", HttpStatusCode.Forbidden);

        var name = $"{user.FirstName} {user.LastName}".Trim();
        var identity = new SessionIdentity(user.Id, name.Length > 0 ? name : user.Email, user.RoleName, response.ExpiresAt);
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.TryStart(identity, response.AccessToken))
            throw new ApiException("La sesión ya venció. Inicia sesión de nuevo.", HttpStatusCode.Unauthorized);
    }
}
