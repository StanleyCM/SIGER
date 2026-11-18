using System.Data;
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
        CancellationToken cancellationToken = default,
        int productLimit = 10)
    {
        // Revenue, order count and product ranking must describe the same set of paid orders.
        await using var snapshot = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var totals = await CompletedPayments(startDate, endDate).GroupBy(_ => 1)
            .Select(payments => new { Count = payments.Count(), Revenue = payments.Sum(payment => payment.Amount) })
            .SingleOrDefaultAsync(cancellationToken);
        var topSellingProducts = await GetTopSellingProductsAsync(
            startDate,
            endDate,
            productLimit,
            cancellationToken);
        await snapshot.CommitAsync(cancellationToken);

        return new SalesReportDto
        {
            StartDate = startDate,
            EndDate = endDate,
            TotalOrders = totals?.Count ?? 0,
            TotalRevenue = totals?.Revenue ?? 0m,
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

        var payments = CompletedPayments(startDate, endDate);
        return await context.OrderDetails
            .AsNoTracking()
            .Where(detail =>
                payments.Any(payment => payment.OrderId == detail.OrderId))
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
            .ThenBy(product => product.ProductId)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<SIGER.Domain.Entities.Payment> CompletedPayments(DateTimeOffset startDate, DateTimeOffset endDate) =>
        context.Payments.AsNoTracking().Where(payment => payment.Status == PaymentStatus.Completed
            && payment.PaymentDate >= startDate && payment.PaymentDate < endDate);
}
