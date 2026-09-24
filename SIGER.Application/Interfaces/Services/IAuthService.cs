using SIGER.Application.Base;
using SIGER.Application.DTOs.Auth;

namespace SIGER.Application.Interfaces.Services;

public interface IAuthService
{
    Task<Result<LoginResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
}
