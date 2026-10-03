using SIGER.Application.Base;
using SIGER.Application.DTOs.Catalog;

namespace SIGER.Application.Interfaces.Services;

public interface IPublicCatalogService
{
    Task<Result<IReadOnlyCollection<PublicCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<Result<PaginatedResult<PublicProductDto>>> GetProductsAsync(int pageNumber, int pageSize, long? categoryId, CancellationToken cancellationToken = default);
    Task<Result<PublicProductDto>> GetProductAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<PublicPromotionDto>>> GetPromotionsAsync(CancellationToken cancellationToken = default);
}
