using SIGER.Application.Base;
using SIGER.Domain.Entities;

namespace SIGER.Application.Interfaces.Repositories;

public interface ITableRepository
{
    Task<Table?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<PaginatedResult<Table>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Table>> GetAvailableAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Table table, CancellationToken cancellationToken = default);
    void Update(Table table);
}
