using SIGER.Application.Base;
using SIGER.Application.DTOs.Roles;
using SIGER.Application.DTOs.Users;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;

namespace SIGER.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IAuthProvider _authProvider;
    private readonly IUnitOfWork _unitOfWork;

    public UserService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IAuthProvider authProvider,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _authProvider = authProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserRequestDto request, CancellationToken cancellationToken = default)
    {
        var password = request.Password ?? string.Empty;
        var errors = Validate(request.FirstName, request.LastName, request.Email, password);
        if (errors.Count > 0)
        {
            return Result<UserDto>.Failure(errors);
        }

        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role is null || !role.IsActive)
        {
            return Result<UserDto>.Failure("The selected role does not exist or is inactive.");
        }

        var normalizedEmail = request.Email.Trim();
        if (await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken) is not null)
        {
            return Result<UserDto>.Failure("A user with this email already exists.");
        }

        var authUserId = await _authProvider.CreateUserAsync(normalizedEmail, password, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            RoleId = role.Id,
            AuthUserId = authUserId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = normalizedEmail,
            Phone = NormalizeOptional(request.Phone),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Role = role
        };

        await _userRepository.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<UserDto>.Success(MapUser(user));
    }

    public async Task<Result<UserDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        return user is null
            ? Result<UserDto>.Failure("User not found.")
            : Result<UserDto>.Success(MapUser(user));
    }

    public async Task<Result<PaginatedResult<UserDto>>> GetPagedAsync(int pageNumber, int pageSize, string? search = null, bool? isActive = null, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1)
        {
            return Result<PaginatedResult<UserDto>>.Failure("Page number and page size must be greater than zero.");
        }

        var page = await _userRepository.GetPagedAsync(pageNumber, pageSize, search, isActive, cancellationToken);
        return Result<PaginatedResult<UserDto>>.Success(
            new PaginatedResult<UserDto>(page.Items.Select(MapUser), page.TotalCount, page.PageNumber, page.PageSize));
    }

    public async Task<Result<UserDto>> UpdateAsync(long id, UpdateUserRequestDto request, CancellationToken cancellationToken = default)
    {
        var errors = Validate(request.FirstName, request.LastName, request.Email);
        if (errors.Count > 0)
        {
            return Result<UserDto>.Failure(errors);
        }

        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Result<UserDto>.Failure("User not found.");
        }

        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role is null || !role.IsActive)
        {
            return Result<UserDto>.Failure("The selected role does not exist or is inactive.");
        }

        var normalizedEmail = request.Email.Trim();
        var existing = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existing is not null && existing.Id != id)
        {
            return Result<UserDto>.Failure("A user with this email already exists.");
        }

        user.RoleId = role.Id;
        user.Role = role;
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = normalizedEmail;
        user.Phone = NormalizeOptional(request.Phone);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        _userRepository.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<UserDto>.Success(MapUser(user));
    }

    public async Task<Result> UpdateStatusAsync(long id, UpdateUserStatusRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        if (request.IsActive)
        {
            await _authProvider.EnableUserAsync(user.AuthUserId, cancellationToken);
        }
        else
        {
            await _authProvider.DisableUserAsync(user.AuthUserId, cancellationToken);
        }

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        _userRepository.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyCollection<RoleDto>>> GetAvailableRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _roleRepository.GetAllActiveAsync(cancellationToken);
        return Result<IReadOnlyCollection<RoleDto>>.Success(roles.Select(role => new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            IsActive = role.IsActive
        }).ToArray());
    }

    private static List<string> Validate(string firstName, string lastName, string email, string? password = null)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(firstName)) errors.Add("First name is required.");
        if (string.IsNullOrWhiteSpace(lastName)) errors.Add("Last name is required.");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) errors.Add("A valid email is required.");
        if (password is not null && string.IsNullOrWhiteSpace(password)) errors.Add("Password is required.");
        return errors;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
