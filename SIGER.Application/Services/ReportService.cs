using SIGER.Application.Base;
using SIGER.Application.DTOs.Reports;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;

namespace SIGER.Application.Services;

public class ReportService : IReportService
{
    private readonly IReportRepository _reportRepository;

    public ReportService(IReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    public async Task<Result<SalesReportDto>> GetSalesReportAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate) return Result<SalesReportDto>.Failure("End date cannot be earlier than start date.");
        var report = await _reportRepository.GetSalesReportAsync(startDate, endDate, cancellationToken);
        return Result<SalesReportDto>.Success(report);
    }

    public async Task<Result<IReadOnlyCollection<TopSellingProductDto>>> GetTopSellingProductsAsync(DateTimeOffset startDate, DateTimeOffset endDate, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate) return Result<IReadOnlyCollection<TopSellingProductDto>>.Failure("End date cannot be earlier than start date.");
        if (limit <= 0) return Result<IReadOnlyCollection<TopSellingProductDto>>.Failure("Limit must be greater than zero.");
        var products = await _reportRepository.GetTopSellingProductsAsync(startDate, endDate, limit, cancellationToken);
        return Result<IReadOnlyCollection<TopSellingProductDto>>.Success(products);
    }
}
