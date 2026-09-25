using Microsoft.EntityFrameworkCore;
using SIGER.Application.Base;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public sealed class AuditRepository(SIGERDbContext context) : IAuditRepository
{
    public Task<Audit?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        context.Audits
            .AsNoTracking()
            .Include(audit => audit.User)
            .FirstOrDefaultAsync(audit => audit.Id == id, cancellationToken);

    public async Task<PaginatedResult<Audit>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        long? userId = null,
        string? entity = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Audits.AsNoTracking().AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(audit => audit.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(entity))
        {
            query = query.Where(audit => audit.Entity == entity);
        }

        if (startDate.HasValue)
        {
            query = query.Where(audit => audit.Timestamp >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(audit => audit.Timestamp <= endDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(audit => audit.Timestamp).ThenByDescending(audit => audit.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Include(audit => audit.User)
            .ToListAsync(cancellationToken);

        return new PaginatedResult<Audit>(items, totalCount, pageNumber, pageSize);
    }

    public Task AddAsync(Audit audit, CancellationToken cancellationToken = default) =>
        context.Audits.AddAsync(audit, cancellationToken).AsTask();
}
