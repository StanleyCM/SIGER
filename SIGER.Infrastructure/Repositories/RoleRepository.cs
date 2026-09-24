using Microsoft.EntityFrameworkCore;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly SIGERDbContext _context;

    public RoleRepository(SIGERDbContext context) => _context = context;

    public Task<Role?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => _context.Roles.FirstOrDefaultAsync(role => role.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<Role>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        => await _context.Roles.AsNoTracking().Where(role => role.IsActive).OrderBy(role => role.Name).ToArrayAsync(cancellationToken);
}
