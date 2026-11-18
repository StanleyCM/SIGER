using System.ComponentModel.DataAnnotations;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Roles;
using SIGER.Application.DTOs.Users;

namespace SIGER.Desktop.Services;

internal sealed class UserApiService(ApiClient api)
{
    internal Task<PaginatedResult<UserDto>> GetPagedAsync(int pageNumber = 1, int pageSize = 20,
        string? search = null, bool? isActive = null, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "La paginación de usuarios debe estar entre 1 y 200.");

        var query = new List<string>(4)
        {
            $"pageNumber={pageNumber}",
            $"pageSize={pageSize}"
        };
        if (!string.IsNullOrWhiteSpace(search))
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (isActive.HasValue)
            query.Add($"isActive={(isActive.Value ? "true" : "false")}");

        return api.SendAsync<PaginatedResult<UserDto>>(
            HttpMethod.Get,
            "api/v1/users?" + string.Join('&', query),
            cancellationToken: cancellationToken);
    }

    internal Task<UserDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync<UserDto>(HttpMethod.Get, $"api/v1/users/{id}", cancellationToken: cancellationToken);
    }

    internal Task<IReadOnlyCollection<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        api.SendAsync<IReadOnlyCollection<RoleDto>>(HttpMethod.Get, "api/v1/users/roles", cancellationToken: cancellationToken);

    internal Task<UserDto> CreateAsync(CreateUserRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFields(request.RoleId, request.FirstName, request.LastName, request.Email);
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new ApiException("Escribe una contraseña inicial para el usuario.");

        var body = new CreateUserRequestDto
        {
            RoleId = request.RoleId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email.Trim(),
            Phone = Normalize(request.Phone),
            Password = request.Password
        };
        return api.SendAsync<UserDto>(HttpMethod.Post, "api/v1/users", body, cancellationToken: cancellationToken);
    }

    internal Task<UserDto> UpdateAsync(long id, UpdateUserRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        ArgumentNullException.ThrowIfNull(request);
        ValidateFields(request.RoleId, request.FirstName, request.LastName, request.Email);

        var body = new UpdateUserRequestDto
        {
            RoleId = request.RoleId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email.Trim(),
            Phone = Normalize(request.Phone)
        };
        return api.SendAsync<UserDto>(HttpMethod.Put, $"api/v1/users/{id}", body, cancellationToken: cancellationToken);
    }

    internal Task UpdateStatusAsync(long id, bool isActive, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync(
            HttpMethod.Patch,
            $"api/v1/users/{id}/status",
            new UpdateUserStatusRequestDto { IsActive = isActive },
            cancellationToken: cancellationToken);
    }

    private static void ValidateFields(long roleId, string firstName, string lastName, string email)
    {
        if (roleId <= 0)
            throw new ApiException("Selecciona un rol válido.");
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ApiException("El nombre del usuario es obligatorio.");
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ApiException("El apellido del usuario es obligatorio.");
        var trimmedEmail = email?.Trim() ?? string.Empty;
        if (trimmedEmail.Length == 0 || trimmedEmail.Length > 150 || !new EmailAddressAttribute().IsValid(trimmedEmail))
            throw new ApiException("Escribe un correo electrónico válido.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
