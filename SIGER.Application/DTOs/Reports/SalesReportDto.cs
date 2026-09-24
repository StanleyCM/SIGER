namespace SIGER.Application.DTOs.Reports;

public sealed class SalesReportDto
{
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public IReadOnlyCollection<TopSellingProductDto> TopSellingProducts { get; set; } = Array.Empty<TopSellingProductDto>();
}
