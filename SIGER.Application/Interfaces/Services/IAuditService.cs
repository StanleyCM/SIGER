using SIGER.Application.Base;
using SIGER.Application.DTOs.Audits;

namespace SIGER.Application.Interfaces.Services;

public interface IAuditService
{
    Task<Result<AuditDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<PaginatedResult<AuditDto>>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        long? userId = null,
        string? entity = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken cancellationToken = default);
}
