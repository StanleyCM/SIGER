using SIGER.Application.Base;
using SIGER.Application.DTOs.Categories;

namespace SIGER.Application.Interfaces.Services;

public interface ICategoryService
{
    Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<PaginatedResult<CategoryDto>>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> UpdateAsync(long id, UpdateCategoryRequestDto request, CancellationToken cancellationToken = default);
    Task<Result> SetActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default);
}
