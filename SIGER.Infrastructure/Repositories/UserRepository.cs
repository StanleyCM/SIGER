using Microsoft.EntityFrameworkCore;
using SIGER.Application.Base;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly SIGERDbContext _context;

    public UserRepository(SIGERDbContext context) => _context = context;

    public Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => _context.Users.Include(user => user.Role).FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public async Task<User?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required to lock a user.");
        var user = await _context.Users.FromSqlInterpolated($"SELECT * FROM public.usuario WHERE id_usuario = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is not null) await _context.Entry(user).ReloadAsync(cancellationToken);
        return user;
    }

    public Task<User?> GetByAuthUserIdAsync(Guid authUserId, CancellationToken cancellationToken = default)
        => _context.Users.AsNoTracking().Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.AuthUserId == authUserId, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _context.Users.AsNoTracking().Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.Email.ToLower() == email.ToLower(), cancellationToken);

    public async Task<PaginatedResult<User>> GetPagedAsync(int pageNumber, int pageSize, string? search = null, bool? isActive = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Users.AsNoTracking().Include(user => user.Role).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(user => EF.Functions.ILike(user.FirstName, pattern)
                || EF.Functions.ILike(user.LastName, pattern)
                || EF.Functions.ILike(user.Email, pattern));
        }
        if (isActive.HasValue) query = query.Where(user => user.IsActive == isActive.Value);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(user => user.Id).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new PaginatedResult<User>(items, totalCount, pageNumber, pageSize);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
        => await _context.Users.AddAsync(user, cancellationToken);

    public void Update(User user) => _context.Users.Update(user);
}
