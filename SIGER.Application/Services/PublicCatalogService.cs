using SIGER.Application.Base;
using SIGER.Application.DTOs.Catalog;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;

namespace SIGER.Application.Services;

public sealed class PublicCatalogService(ICategoryRepository categories, IProductRepository products,
    IPromotionRepository promotions, TimeProvider clock) : IPublicCatalogService
{
    public async Task<Result<IReadOnlyCollection<PublicCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        => Result<IReadOnlyCollection<PublicCategoryDto>>.Success((await categories.GetActiveAsync(cancellationToken))
            .Where(c => c.IsActive).Select(c => new PublicCategoryDto(c.Id, c.Name, c.Description)).ToArray());

    public async Task<Result<PaginatedResult<PublicProductDto>>> GetProductsAsync(int pageNumber, int pageSize, long? categoryId, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100 || ((long)pageNumber - 1) * pageSize > int.MaxValue || categoryId is <= 0)
            return Result<PaginatedResult<PublicProductDto>>.Failure("Invalid catalog page or category.");
        var page = await products.GetPublicPagedAsync(pageNumber, pageSize, categoryId, cancellationToken);
        return Result<PaginatedResult<PublicProductDto>>.Success(new(page.Items.Select(Map), page.TotalCount, pageNumber, pageSize));
    }

    public async Task<Result<PublicProductDto>> GetProductAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdAsync(id, cancellationToken);
        return product is null || !product.IsAvailable || product.Category?.IsActive != true
            ? Result<PublicProductDto>.Failure("Product not found.") : Result<PublicProductDto>.Success(Map(product));
    }

    public async Task<Result<IReadOnlyCollection<PublicPromotionDto>>> GetPromotionsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var active = await promotions.GetActiveAsync(now, cancellationToken);
        return Result<IReadOnlyCollection<PublicPromotionDto>>.Success(active
            .Where(p => p.IsActive && p.StartDate <= now && p.EndDate >= now)
            .Select(p => new PublicPromotionDto(p.Id, p.Name, p.Description, p.DiscountPercentage, p.StartDate, p.EndDate,
                p.PromotionProducts.Where(x => x.Product.IsAvailable && x.Product.Category?.IsActive == true).Select(x => x.ProductId).ToArray()))
            .ToArray());
    }

    private static PublicProductDto Map(Product p) => new(p.Id, p.CategoryId, p.Name, p.Description, p.Price, p.ImageUrl);
}
