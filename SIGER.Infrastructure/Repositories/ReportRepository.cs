using Microsoft.EntityFrameworkCore;
using SIGER.Application.DTOs.Reports;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Enums;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public sealed class ReportRepository(SIGERDbContext context) : IReportRepository
{
    public async Task<SalesReportDto> GetSalesReportAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default)
    {
        var paidOrders = context.Orders
            .AsNoTracking()
            .Where(order =>
                order.Status == OrderStatus.Paid &&
                order.OrderDateTime >= startDate &&
                order.OrderDateTime <= endDate);

        var totalOrders = await paidOrders.CountAsync(cancellationToken);
        var totalRevenue = await paidOrders
            .Select(order => (decimal?)order.Total)
            .SumAsync(cancellationToken) ?? 0m;
        var topSellingProducts = await GetTopSellingProductsAsync(
            startDate,
            endDate,
            10,
            cancellationToken);

        return new SalesReportDto
        {
            StartDate = startDate,
            EndDate = endDate,
            TotalOrders = totalOrders,
            TotalRevenue = totalRevenue,
            TopSellingProducts = topSellingProducts
        };
    }

    public async Task<IReadOnlyCollection<TopSellingProductDto>> GetTopSellingProductsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Array.Empty<TopSellingProductDto>();
        }

        return await context.OrderDetails
            .AsNoTracking()
            .Where(detail =>
                detail.Order.Status == OrderStatus.Paid &&
                detail.Order.OrderDateTime >= startDate &&
                detail.Order.OrderDateTime <= endDate)
            .GroupBy(detail => new { detail.ProductId, detail.Product.Name })
            .Select(group => new TopSellingProductDto
            {
                ProductId = group.Key.ProductId,
                ProductName = group.Key.Name,
                QuantitySold = group.Sum(detail => detail.Quantity),
                Revenue = group.Sum(detail => detail.Subtotal)
            })
            .OrderByDescending(product => product.QuantitySold)
            .ThenByDescending(product => product.Revenue)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
