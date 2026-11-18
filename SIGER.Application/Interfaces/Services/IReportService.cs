using SIGER.Application.Base;
using SIGER.Application.DTOs.Reports;

namespace SIGER.Application.Interfaces.Services;

public interface IReportService
{
    Task<Result<SalesReportDto>> GetSalesReportAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken = default, int productLimit = 10);
    Task<Result<IReadOnlyCollection<TopSellingProductDto>>> GetTopSellingProductsAsync(DateTimeOffset startDate, DateTimeOffset endDate, int limit = 10, CancellationToken cancellationToken = default);
}
