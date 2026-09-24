using Microsoft.EntityFrameworkCore;
using SIGER.Application.Base;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public class TableRepository : ITableRepository
{
    private readonly SIGERDbContext _context;

    public TableRepository(SIGERDbContext context) => _context = context;

    public Task<Table?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => _context.Tables.FirstOrDefaultAsync(table => table.Id == id, cancellationToken);

    public async Task<PaginatedResult<Table>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Tables.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(table => table.Number).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new PaginatedResult<Table>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyCollection<Table>> GetAvailableAsync(CancellationToken cancellationToken = default)
        => await _context.Tables.AsNoTracking().Where(table => table.Status == TableStatus.Available).OrderBy(table => table.Number).ToArrayAsync(cancellationToken);

    public async Task AddAsync(Table table, CancellationToken cancellationToken = default)
        => await _context.Tables.AddAsync(table, cancellationToken);

    public void Update(Table table) => _context.Tables.Update(table);
}
