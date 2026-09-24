using SIGER.Application.Base;
using SIGER.Application.DTOs.Promotions;

namespace SIGER.Application.Interfaces.Services;

public interface IPromotionService
{
    Task<Result<PromotionDto>> CreateAsync(CreatePromotionRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<PromotionDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<PaginatedResult<PromotionDto>>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<PromotionDto>>> GetActiveAsync(DateTimeOffset at, CancellationToken cancellationToken = default);
    Task<Result<PromotionDto>> UpdateAsync(long id, UpdatePromotionRequestDto request, CancellationToken cancellationToken = default);
    Task<Result> SetActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default);
    Task<Result> AddProductAsync(long promotionId, long productId, CancellationToken cancellationToken = default);
    Task<Result> RemoveProductAsync(long promotionId, long productId, CancellationToken cancellationToken = default);
}
