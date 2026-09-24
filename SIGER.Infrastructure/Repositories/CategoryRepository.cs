using Microsoft.EntityFrameworkCore;
using SIGER.Application.Base;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly SIGERDbContext _context;

    public CategoryRepository(SIGERDbContext context) => _context = context;

    public Task<Category?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => _context.Categories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    public async Task<PaginatedResult<Category>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Categories.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(category => category.Name).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new PaginatedResult<Category>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyCollection<Category>> GetActiveAsync(CancellationToken cancellationToken = default)
        => await _context.Categories.AsNoTracking().Where(category => category.IsActive).OrderBy(category => category.Name).ToArrayAsync(cancellationToken);

    public async Task AddAsync(Category category, CancellationToken cancellationToken = default)
        => await _context.Categories.AddAsync(category, cancellationToken);

    public void Update(Category category) => _context.Categories.Update(category);
}
