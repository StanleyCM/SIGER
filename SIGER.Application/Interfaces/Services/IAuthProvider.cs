using SIGER.Application.DTOs.Auth;

namespace SIGER.Application.Interfaces.Services;

public interface IAuthProvider
{
    Task<AuthSessionDto?> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthUserStateDto> CreateUserAsync(Guid authUserId, Guid operationId, string email, string password, CancellationToken cancellationToken = default);
    Task<AuthUserStateDto?> GetUserAsync(Guid authUserId, CancellationToken cancellationToken = default);
    Task<AuthUserStateDto> UpdateEmailAsync(Guid authUserId, Guid operationId, string email, CancellationToken cancellationToken = default);
    Task<AuthUserStateDto> SetActiveAsync(Guid authUserId, Guid operationId, bool isActive, CancellationToken cancellationToken = default);
    Task RestoreUserAsync(AuthUserStateDto original, AuthUserStateDto expected, CancellationToken cancellationToken = default);
    Task DeleteCreatedUserAsync(AuthUserStateDto expected, CancellationToken cancellationToken = default);
}
