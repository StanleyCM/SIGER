using SIGER.Application.Base;
using SIGER.Domain.Entities;

namespace SIGER.Application.Interfaces.Repositories;

public interface IAuditRepository
{
    Task<Audit?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<PaginatedResult<Audit>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        long? userId = null,
        string? entity = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken cancellationToken = default);
    Task AddAsync(Audit audit, CancellationToken cancellationToken = default);
}
