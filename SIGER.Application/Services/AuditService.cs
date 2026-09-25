using SIGER.Application.Base;
using SIGER.Application.DTOs.Audits;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;

namespace SIGER.Application.Services;

public class AuditService : IAuditService
{
    private readonly IAuditRepository _auditRepository;

    public AuditService(IAuditRepository auditRepository)
    {
        _auditRepository = auditRepository;
    }

    public async Task<Result<AuditDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var audit = await _auditRepository.GetByIdAsync(id, cancellationToken);
        return audit is null ? Result<AuditDto>.Failure("Audit record not found.") : Result<AuditDto>.Success(Map(audit));
    }

    public async Task<Result<PaginatedResult<AuditDto>>> GetPagedAsync(int pageNumber, int pageSize, long? userId = null, string? entity = null, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 200 || ((long)pageNumber - 1) * pageSize > int.MaxValue) return Result<PaginatedResult<AuditDto>>.Failure("Page number and size must be positive, size at most 200, and offset within the supported range.");
        if (startDate.HasValue && endDate.HasValue && endDate < startDate) return Result<PaginatedResult<AuditDto>>.Failure("End date cannot be earlier than start date.");
        var page = await _auditRepository.GetPagedAsync(pageNumber, pageSize, userId, entity, startDate, endDate, cancellationToken);
        return Result<PaginatedResult<AuditDto>>.Success(new(page.Items.Select(Map), page.TotalCount, page.PageNumber, page.PageSize));
    }

    private static AuditDto Map(Audit audit) => new()
    {
        Id = audit.Id,
        UserId = audit.UserId,
        UserName = audit.User is null ? null : $"{audit.User.FirstName} {audit.User.LastName}".Trim(),
        Action = audit.Action,
        Entity = audit.Entity,
        EntityId = audit.EntityId,
        PreviousData = audit.PreviousData,
        NewData = audit.NewData,
        IpAddress = audit.IpAddress,
        Timestamp = audit.Timestamp
    };
}
