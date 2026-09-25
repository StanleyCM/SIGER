using SIGER.Application.Base;
using SIGER.Domain.Entities;

namespace SIGER.Application.Interfaces.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Product>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);
    Task<PaginatedResult<Product>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        long? categoryId = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Product>> GetAvailableAsync(long? categoryId = null, CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    void Update(Product product);
}
