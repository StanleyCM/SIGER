using SIGER.Application.Base;
using SIGER.Application.DTOs.Categories;

namespace SIGER.Desktop.Services;

internal sealed class CategoryApiService(ApiClient api)
{
    internal Task<PaginatedResult<CategoryDto>> GetPagedAsync(int pageNumber = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "La paginación de categorías debe estar entre 1 y 200.");

        return api.SendAsync<PaginatedResult<CategoryDto>>(
            HttpMethod.Get,
            $"api/v1/categories?pageNumber={pageNumber}&pageSize={pageSize}",
            cancellationToken: cancellationToken);
    }

    internal Task<CategoryDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync<CategoryDto>(HttpMethod.Get, $"api/v1/categories/{id}", cancellationToken: cancellationToken);
    }

    internal Task<CategoryDto> CreateAsync(string name, string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiException("El nombre de la categoría es obligatorio.");

        var body = new CreateCategoryRequestDto
        {
            Name = name.Trim(),
            Description = Normalize(description)
        };
        return api.SendAsync<CategoryDto>(HttpMethod.Post, "api/v1/categories", body, cancellationToken: cancellationToken);
    }

    internal Task<CategoryDto> UpdateAsync(long id, string name, string? description = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiException("El nombre de la categoría es obligatorio.");

        var body = new UpdateCategoryRequestDto
        {
            Name = name.Trim(),
            Description = Normalize(description)
        };
        return api.SendAsync<CategoryDto>(HttpMethod.Put, $"api/v1/categories/{id}", body, cancellationToken: cancellationToken);
    }

    internal Task SetActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync(
            HttpMethod.Patch,
            $"api/v1/categories/{id}/status",
            new { isActive },
            cancellationToken: cancellationToken);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
