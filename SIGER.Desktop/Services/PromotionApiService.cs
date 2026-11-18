using System.Globalization;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Promotions;

namespace SIGER.Desktop.Services;

internal sealed class PromotionApiService(ApiClient api)
{
    internal Task<PaginatedResult<PromotionDto>> GetPagedAsync(int pageNumber = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "La paginación de promociones debe estar entre 1 y 200.");

        return api.SendAsync<PaginatedResult<PromotionDto>>(
            HttpMethod.Get,
            $"api/v1/promotions?pageNumber={pageNumber}&pageSize={pageSize}",
            cancellationToken: cancellationToken);
    }

    internal Task<IReadOnlyCollection<PromotionDto>> GetActiveAsync(DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        var path = at.HasValue
            ? $"api/v1/promotions/active?at={Uri.EscapeDataString(at.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}"
            : "api/v1/promotions/active";
        return api.SendAsync<IReadOnlyCollection<PromotionDto>>(HttpMethod.Get, path, cancellationToken: cancellationToken);
    }

    internal Task<PromotionDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync<PromotionDto>(HttpMethod.Get, $"api/v1/promotions/{id}", cancellationToken: cancellationToken);
    }

    internal Task<PromotionDto> CreateAsync(CreatePromotionRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePromotion(request.Name, request.DiscountPercentage, request.StartDate, request.EndDate);

        var body = new CreatePromotionRequestDto
        {
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            DiscountPercentage = decimal.Round(request.DiscountPercentage, 2),
            StartDate = request.StartDate.ToUniversalTime(),
            EndDate = request.EndDate.ToUniversalTime(),
            IsActive = request.IsActive,
            ProductIds = request.ProductIds.Where(id => id > 0).Distinct().ToList()
        };
        return api.SendAsync<PromotionDto>(HttpMethod.Post, "api/v1/promotions", body, cancellationToken: cancellationToken);
    }

    internal Task<PromotionDto> UpdateAsync(long id, UpdatePromotionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        ArgumentNullException.ThrowIfNull(request);
        ValidatePromotion(request.Name, request.DiscountPercentage, request.StartDate, request.EndDate);

        var body = new UpdatePromotionRequestDto
        {
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            DiscountPercentage = decimal.Round(request.DiscountPercentage, 2),
            StartDate = request.StartDate.ToUniversalTime(),
            EndDate = request.EndDate.ToUniversalTime()
        };
        return api.SendAsync<PromotionDto>(HttpMethod.Put, $"api/v1/promotions/{id}", body, cancellationToken: cancellationToken);
    }

    internal Task SetActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync(
            HttpMethod.Patch,
            $"api/v1/promotions/{id}/status",
            new { isActive },
            cancellationToken: cancellationToken);
    }

    internal Task AddProductAsync(long promotionId, long productId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(promotionId, 0);
        if (productId <= 0)
            throw new ApiException("Selecciona un producto válido para asociar a la promoción.");

        return api.SendAsync(
            HttpMethod.Post,
            $"api/v1/promotions/{promotionId}/products/{productId}",
            cancellationToken: cancellationToken);
    }

    internal Task RemoveProductAsync(long promotionId, long productId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(promotionId, 0);
        if (productId <= 0)
            throw new ApiException("Selecciona un producto asociado válido para desasociar.");

        return api.SendAsync(
            HttpMethod.Delete,
            $"api/v1/promotions/{promotionId}/products/{productId}",
            cancellationToken: cancellationToken);
    }

    private static void ValidatePromotion(string name, decimal discount, DateTimeOffset startDate, DateTimeOffset endDate)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiException("El nombre de la promoción es obligatorio.");
        if (discount is <= 0m or > 100m)
            throw new ApiException("El porcentaje de descuento debe ser mayor que 0 y como máximo 100.");
        if (endDate <= startDate)
            throw new ApiException("La fecha de fin debe ser posterior a la fecha de inicio.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
