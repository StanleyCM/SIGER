using SIGER.Application.Base;
using SIGER.Application.DTOs.Roles;
using SIGER.Application.DTOs.Users;

namespace SIGER.Application.Interfaces.Services;

public interface IUserService
{
    Task<Result<UserDto>> CreateAsync(CreateUserRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<UserDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<PaginatedResult<UserDto>>> GetPagedAsync(int pageNumber, int pageSize, string? search = null, bool? isActive = null, CancellationToken cancellationToken = default);
    Task<Result<UserDto>> UpdateAsync(long id, UpdateUserRequestDto request, CancellationToken cancellationToken = default);
    Task<Result> UpdateStatusAsync(long id, UpdateUserStatusRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<RoleDto>>> GetAvailableRolesAsync(CancellationToken cancellationToken = default);
}
