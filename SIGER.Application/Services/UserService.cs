using System.Net.Mail;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Auth;
using SIGER.Application.Exceptions;
using SIGER.Domain.Exceptions;
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
    private readonly IUserOperationReporter _reporter;

    public UserService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IAuthProvider authProvider,
        IUnitOfWork unitOfWork, IUserOperationReporter reporter)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _authProvider = authProvider;
        _unitOfWork = unitOfWork;
        _reporter = reporter;
    }

    public Task<Result<UserDto>> CreateAsync(CreateUserRequestDto request, CancellationToken cancellationToken = default)
        => ExecuteAsync("Create", null, async (operation, token) =>
        {
            var password = request.Password ?? string.Empty;
            var errors = Validate(request.FirstName, request.LastName, request.Email, password);
            if (errors.Count > 0) return Result<UserDto>.Failure(errors);
            var role = await _roleRepository.GetByIdAsync(request.RoleId, token);
            if (role is null || !role.IsActive) return Result<UserDto>.Failure("The selected role does not exist or is inactive.");
            var email = request.Email.Trim().ToLowerInvariant();
            if (await _userRepository.GetByEmailAsync(email, token) is not null)
                return Result<UserDto>.Failure("A user with this email already exists.");
            operation.AuthId = Guid.NewGuid();
            operation.Creation = true;
            operation.AuthAttempted = true;
            operation.Applied = await _authProvider.CreateUserAsync(operation.AuthId, operation.Id, email, password, token);
            ValidateApplied(operation, email, true);
            var now = DateTimeOffset.UtcNow;
            var user = new User
            {
                RoleId = role.Id, Role = role, AuthUserId = operation.AuthId,
                FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(), Email = email,
                Phone = NormalizeOptional(request.Phone), IsActive = true, CreatedAt = now, UpdatedAt = now
            };
            await _userRepository.AddAsync(user, token);
            await _unitOfWork.SaveChangesAsync(token);
            operation.LocalAfter = LocalState.From(user);
            return Result<UserDto>.Success(MapUser(user));
        }, cancellationToken);

    public async Task<Result<UserDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        return user is null
            ? Result<UserDto>.Failure("User not found.")
            : Result<UserDto>.Success(MapUser(user));
    }

    public async Task<Result<PaginatedResult<UserDto>>> GetPagedAsync(int pageNumber, int pageSize, string? search = null, bool? isActive = null, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 200 || ((long)pageNumber - 1) * pageSize > int.MaxValue)
        {
            return Result<PaginatedResult<UserDto>>.Failure("Page number and size must be positive, size at most 200, and offset within the supported range.");
        }

        var page = await _userRepository.GetPagedAsync(pageNumber, pageSize, search, isActive, cancellationToken);
        return Result<PaginatedResult<UserDto>>.Success(
            new PaginatedResult<UserDto>(page.Items.Select(MapUser), page.TotalCount, page.PageNumber, page.PageSize));
    }

    public Task<Result<UserDto>> UpdateAsync(long id, UpdateUserRequestDto request, CancellationToken cancellationToken = default)
        => ExecuteAsync("Update", id, async (operation, token) =>
        {
            var errors = Validate(request.FirstName, request.LastName, request.Email);
            if (errors.Count > 0) return Result<UserDto>.Failure(errors);
            var user = await _userRepository.GetByIdForUpdateAsync(id, token);
            if (user is null) return Result<UserDto>.Failure("User not found.");
            var role = await _roleRepository.GetByIdAsync(request.RoleId, token);
            if (role is null || !role.IsActive) return Result<UserDto>.Failure("The selected role does not exist or is inactive.");
            var email = request.Email.Trim().ToLowerInvariant();
            var existing = await _userRepository.GetByEmailAsync(email, token);
            if (existing is not null && existing.Id != id) return Result<UserDto>.Failure("A user with this email already exists.");
            await PrepareExistingAsync(operation, user, token);
            if (!string.Equals(operation.Original!.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                operation.AuthAttempted = true;
                operation.Applied = await _authProvider.UpdateEmailAsync(user.AuthUserId, operation.Id, email, token);
                ValidateApplied(operation, email, user.IsActive);
            }
            user.RoleId = role.Id; user.Role = role;
            user.FirstName = request.FirstName.Trim(); user.LastName = request.LastName.Trim();
            user.Email = email; user.Phone = NormalizeOptional(request.Phone); user.UpdatedAt = DateTimeOffset.UtcNow;
            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(token);
            operation.LocalAfter = LocalState.From(user);
            return Result<UserDto>.Success(MapUser(user));
        }, cancellationToken);

    public Task<Result> UpdateStatusAsync(long id, UpdateUserStatusRequestDto request, CancellationToken cancellationToken = default)
        => ExecuteAsync("Status", id, async (operation, token) =>
        {
            var user = await _userRepository.GetByIdForUpdateAsync(id, token);
            if (user is null) return Result.Failure("User not found.");
            await PrepareExistingAsync(operation, user, token);
            if (user.IsActive == request.IsActive) return Result.Success();
            operation.AuthAttempted = true;
            operation.Applied = await _authProvider.SetActiveAsync(user.AuthUserId, operation.Id, request.IsActive, token);
            ValidateApplied(operation, user.Email, request.IsActive);
            user.IsActive = request.IsActive; user.UpdatedAt = DateTimeOffset.UtcNow;
            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(token);
            operation.LocalAfter = LocalState.From(user);
            return Result.Success();
        }, cancellationToken);

    private async Task PrepareExistingAsync(Operation operation, User user, CancellationToken token)
    {
        operation.AuthId = user.AuthUserId;
        operation.LocalBefore = LocalState.From(user);
        operation.Original = await _authProvider.GetUserAsync(user.AuthUserId, token)
            ?? throw new BusinessRuleException("The Auth identity is unavailable.");
        if (!string.Equals(operation.Original.Email, user.Email, StringComparison.OrdinalIgnoreCase) ||
            operation.Original.IsActive != user.IsActive)
            throw new BusinessRuleException("The local and Auth profiles require reconciliation.");
    }

    private static void ValidateApplied(Operation operation, string email, bool active)
    {
        if (operation.Applied is null || operation.Applied.Id != operation.AuthId || operation.Applied.OperationId != operation.Id ||
            !string.Equals(operation.Applied.Email, email, StringComparison.OrdinalIgnoreCase) || operation.Applied.IsActive != active)
            throw new InvalidOperationException("The provider did not confirm the requested user state.");
    }

    private async Task<T> ExecuteAsync<T>(string name, long? localId, Func<Operation, CancellationToken, Task<T>> action, CancellationToken token)
    {
        var operation = new Operation();
        T? completed = default;
        var finished = false;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
            {
                completed = await action(operation, transactionToken);
                finished = true;
                return completed;
            }, token);
        }
        catch (Exception failure)
        {
            Exception? compensationFailure = null;
            if (operation.AuthAttempted)
            {
                // Request cancellation must not cancel recovery. Recovery itself is bounded.
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    // Only a completed delegate can have reached COMMIT. If the delegate failed,
                    // UoW rolls back/disposes its transaction: recovery must still work while DB is unavailable.
                    if (finished)
                    {
                        var local = await _userRepository.GetByAuthUserIdAsync(operation.AuthId, recovery.Token);
                        var localState = local is null ? null : LocalState.From(local);
                        // A commit acknowledgement can fail after PostgreSQL committed. Never undo Auth in that case.
                        if (operation.LocalAfter is not null && localState == operation.LocalAfter)
                        {
                            _reporter.ReportFailure(operation.Id, name + "CommitAcknowledgement", localId, operation.AuthId, failure.GetType().Name, null);
                            return completed!;
                        }
                        if (localState != operation.LocalBefore)
                            throw new InvalidOperationException("Local state changed or commit outcome is uncertain; recovery refused to overwrite it.");
                    }
                    var current = await _authProvider.GetUserAsync(operation.AuthId, recovery.Token);
                    if (operation.Creation)
                    {
                        if (current is not null)
                        {
                            if (current.OperationId != operation.Id || (operation.Applied is not null && current != operation.Applied) ||
                                (operation.Applied is null && current.CreatedAt != current.UpdatedAt))
                                throw new InvalidOperationException("Created Auth identity has subsequent changes.");
                            await _authProvider.DeleteCreatedUserAsync(current, recovery.Token);
                        }
                    }
                    else if (current != operation.Original)
                    {
                        if (operation.Applied is null || current != operation.Applied)
                            throw new InvalidOperationException("Auth outcome is uncertain or has subsequent changes.");
                        await _authProvider.RestoreUserAsync(operation.Original!, operation.Applied, recovery.Token);
                    }
                }
                catch (Exception error) { compensationFailure = error; }
            }
            _reporter.ReportFailure(operation.Id, name, localId, operation.AuthId, failure.GetType().Name, compensationFailure?.GetType().Name);
            // Do not propagate exception messages/inner exceptions from a provider, database or recovery to logging.
            if (failure is BusinessRuleException && compensationFailure is null)
                throw new BusinessRuleException("The user operation conflicts with the current Auth or local state.");
            throw new UserOperationException(operation.Id, compensationFailure is not null);
        }
    }

    private sealed class Operation
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid AuthId { get; set; }
        public bool Creation { get; set; }
        public bool AuthAttempted { get; set; }
        public AuthUserStateDto? Original { get; set; }
        public AuthUserStateDto? Applied { get; set; }
        public LocalState? LocalBefore { get; set; }
        public LocalState? LocalAfter { get; set; }
    }

    private sealed record LocalState(long Id, Guid AuthId, long RoleId, string FirstName, string LastName,
        string Email, string? Phone, bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
    {
        public static LocalState From(User user) => new(user.Id, user.AuthUserId, user.RoleId, user.FirstName,
            user.LastName, user.Email, user.Phone, user.IsActive, user.CreatedAt, user.UpdatedAt);
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
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 150 ||
            !MailAddress.TryCreate(email.Trim(), out var parsed) || parsed.Address != email.Trim())
            errors.Add("A valid email is required.");
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
