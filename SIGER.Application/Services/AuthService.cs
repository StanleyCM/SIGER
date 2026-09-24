using SIGER.Application.Base;
using SIGER.Application.DTOs.Auth;
using SIGER.Application.DTOs.Users;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;

namespace SIGER.Application.Services;

public class AuthService : IAuthService
{
    private readonly IAuthProvider _authProvider;
    private readonly IUserRepository _userRepository;

    public AuthService(IAuthProvider authProvider, IUserRepository userRepository)
    {
        _authProvider = authProvider;
        _userRepository = userRepository;
    }

    public async Task<Result<LoginResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<LoginResponseDto>.Failure("Email and password are required.");
        }

        var session = await _authProvider.SignInAsync(request.Email.Trim(), request.Password, cancellationToken);
        if (session is null)
        {
            return Result<LoginResponseDto>.Failure("Invalid credentials.");
        }

        var user = await _userRepository.GetByAuthUserIdAsync(session.AuthUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result<LoginResponseDto>.Failure("The user profile is unavailable or inactive.");
        }

        return Result<LoginResponseDto>.Success(new LoginResponseDto
        {
            AccessToken = session.AccessToken,
            ExpiresAt = session.ExpiresAt,
            User = MapUser(user)
        });
    }

    private static UserDto MapUser(User user) => new()
    {
        Id = user.Id,
        RoleId = user.RoleId,
        RoleName = user.Role?.Name ?? string.Empty,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email,
        Phone = user.Phone,
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };
}
