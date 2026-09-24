using SIGER.Application.Base;
using SIGER.Application.DTOs.Products;

namespace SIGER.Application.Interfaces.Services;

public interface IProductService
{
    Task<Result<ProductDto>> CreateAsync(CreateProductRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<PaginatedResult<ProductDto>>> GetPagedAsync(int pageNumber, int pageSize, long? categoryId = null, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<ProductDto>>> GetAvailableAsync(long? categoryId = null, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> UpdateAsync(long id, UpdateProductRequestDto request, CancellationToken cancellationToken = default);
    Task<Result> ChangeAvailabilityAsync(long id, UpdateProductAvailabilityRequestDto request, CancellationToken cancellationToken = default);
}
