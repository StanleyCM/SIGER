using SIGER.Domain.Entities;

namespace SIGER.Application.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Role>> GetAllActiveAsync(CancellationToken cancellationToken = default);
}
