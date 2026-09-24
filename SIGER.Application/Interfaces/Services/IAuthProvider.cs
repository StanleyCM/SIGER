using SIGER.Application.DTOs.Auth;

namespace SIGER.Application.Interfaces.Services;

public interface IAuthProvider
{
    Task<AuthSessionDto?> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<Guid> CreateUserAsync(string email, string password, CancellationToken cancellationToken = default);
    Task EnableUserAsync(Guid authUserId, CancellationToken cancellationToken = default);
    Task DisableUserAsync(Guid authUserId, CancellationToken cancellationToken = default);
}
