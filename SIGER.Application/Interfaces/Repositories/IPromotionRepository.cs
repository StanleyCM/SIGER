using SIGER.Application.Base;
using SIGER.Domain.Entities;

namespace SIGER.Application.Interfaces.Repositories;

public interface IPromotionRepository
{
    Task<Promotion?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<PaginatedResult<Promotion>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Promotion>> GetActiveAsync(DateTimeOffset at, CancellationToken cancellationToken = default);
    Task AddAsync(Promotion promotion, CancellationToken cancellationToken = default);
    void Update(Promotion promotion);
    Task AddProductAsync(PromotionProduct promotionProduct, CancellationToken cancellationToken = default);
    Task RemoveProductAsync(long promotionId, long productId, CancellationToken cancellationToken = default);
}
