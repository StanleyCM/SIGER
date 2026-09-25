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

    public async Task<Table?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required to lock a table.");
        var table = await _context.Tables.FromSqlInterpolated($"SELECT * FROM public.mesa WHERE id_mesa = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        // A preceding relationship load may already have tracked this table before acquiring the lock.
        if (table is not null) await _context.Entry(table).ReloadAsync(cancellationToken);
        return table;
    }

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
