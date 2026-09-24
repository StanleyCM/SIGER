using Microsoft.EntityFrameworkCore;
using SIGER.Application.Base;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly SIGERDbContext _context;

    public ProductRepository(SIGERDbContext context) => _context = context;

    public Task<Product?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => _context.Products.Include(product => product.Category).FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    public async Task<PaginatedResult<Product>> GetPagedAsync(int pageNumber, int pageSize, long? categoryId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Products.AsNoTracking().Include(product => product.Category).AsQueryable();
        if (categoryId.HasValue) query = query.Where(product => product.CategoryId == categoryId.Value);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(product => product.Name).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new PaginatedResult<Product>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyCollection<Product>> GetAvailableAsync(long? categoryId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Products.AsNoTracking().Include(product => product.Category).Where(product => product.IsAvailable);
        if (categoryId.HasValue) query = query.Where(product => product.CategoryId == categoryId.Value);
        return await query.OrderBy(product => product.Name).ToArrayAsync(cancellationToken);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
        => await _context.Products.AddAsync(product, cancellationToken);

    public void Update(Product product) => _context.Products.Update(product);
}
