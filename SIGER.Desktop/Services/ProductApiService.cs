using SIGER.Application.Base;
using SIGER.Application.DTOs.Products;

namespace SIGER.Desktop.Services;

internal sealed class ProductApiService(ApiClient api)
{
    internal Task<IReadOnlyCollection<ProductDto>> GetAvailableAsync(long? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        if (categoryId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(categoryId), "El identificador de categoría debe ser positivo.");

        var path = categoryId.HasValue
            ? $"api/v1/products/available?categoryId={categoryId.Value}"
            : "api/v1/products/available";
        return api.SendAsync<IReadOnlyCollection<ProductDto>>(HttpMethod.Get, path, cancellationToken: cancellationToken);
    }

    internal Task<PaginatedResult<ProductDto>> GetPagedAsync(int pageNumber = 1, int pageSize = 100,
        long? categoryId = null, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "La paginación de productos debe estar entre 1 y 200.");
        if (categoryId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(categoryId), "El identificador de categoría debe ser positivo.");

        var path = $"api/v1/products?pageNumber={pageNumber}&pageSize={pageSize}"
            + (categoryId.HasValue ? $"&categoryId={categoryId.Value}" : "");
        return api.SendAsync<PaginatedResult<ProductDto>>(HttpMethod.Get, path, cancellationToken: cancellationToken);
    }

    internal Task<ProductDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync<ProductDto>(HttpMethod.Get, $"api/v1/products/{id}", cancellationToken: cancellationToken);
    }

    internal Task<ProductDto> CreateAsync(CreateProductRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProduct(request.CategoryId, request.Name, request.Price);

        var body = new CreateProductRequestDto
        {
            CategoryId = request.CategoryId,
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            Price = decimal.Round(request.Price, 2),
            IsAvailable = request.IsAvailable,
            ImageUrl = Normalize(request.ImageUrl)
        };
        return api.SendAsync<ProductDto>(HttpMethod.Post, "api/v1/products", body, cancellationToken: cancellationToken);
    }

    internal Task<ProductDto> UpdateAsync(long id, UpdateProductRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.Version);
        ValidateProduct(request.CategoryId, request.Name, request.Price);

        var body = new UpdateProductRequestDto
        {
            CategoryId = request.CategoryId,
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            Price = decimal.Round(request.Price, 2),
            ImageUrl = Normalize(request.ImageUrl),
            Version = request.Version
        };
        return api.SendAsync<ProductDto>(HttpMethod.Put, $"api/v1/products/{id}", body, cancellationToken: cancellationToken);
    }

    internal Task ChangeAvailabilityAsync(long id, bool isAvailable, long version, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        return api.SendAsync(
            HttpMethod.Patch,
            $"api/v1/products/{id}/availability",
            new UpdateProductAvailabilityRequestDto { IsAvailable = isAvailable, Version = version },
            cancellationToken: cancellationToken);
    }

    private static void ValidateProduct(long categoryId, string name, decimal price)
    {
        if (categoryId <= 0)
            throw new ApiException("Selecciona una categoría válida para el producto.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiException("El nombre del producto es obligatorio.");
        if (price < 0m)
            throw new ApiException("El precio del producto no puede ser negativo.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
