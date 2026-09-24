using SIGER.Application.DTOs.Reports;

namespace SIGER.Application.Interfaces.Repositories;

public interface IReportRepository
{
    Task<SalesReportDto> GetSalesReportAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<TopSellingProductDto>> GetTopSellingProductsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        int limit,
        CancellationToken cancellationToken = default);
}
