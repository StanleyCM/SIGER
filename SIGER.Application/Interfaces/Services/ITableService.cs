using SIGER.Application.Base;
using SIGER.Application.DTOs.Tables;

namespace SIGER.Application.Interfaces.Services;

public interface ITableService
{
    Task<Result<TableDto>> CreateAsync(CreateTableRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<TableDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<PaginatedResult<TableDto>>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<TableDto>>> GetAvailableAsync(CancellationToken cancellationToken = default);
    Task<Result<TableDto>> UpdateAsync(long id, UpdateTableRequestDto request, CancellationToken cancellationToken = default);
    Task<Result> ChangeStatusAsync(long id, UpdateTableStatusRequestDto request, CancellationToken cancellationToken = default);
}
