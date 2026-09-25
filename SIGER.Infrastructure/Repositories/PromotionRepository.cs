using Microsoft.EntityFrameworkCore;
using SIGER.Application.Base;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public sealed class PromotionRepository(SIGERDbContext context) : IPromotionRepository
{
    public Task<Promotion?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        context.Promotions
            .Include(promotion => promotion.PromotionProducts)
                .ThenInclude(promotionProduct => promotionProduct.Product)
            .AsSplitQuery()
            .FirstOrDefaultAsync(promotion => promotion.Id == id, cancellationToken);

    public async Task<PaginatedResult<Promotion>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.Promotions
            .AsNoTracking()
            .OrderByDescending(promotion => promotion.StartDate).ThenByDescending(promotion => promotion.Id);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Include(promotion => promotion.PromotionProducts)
                .ThenInclude(promotionProduct => promotionProduct.Product)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new PaginatedResult<Promotion>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyCollection<Promotion>> GetActiveAsync(
        DateTimeOffset at,
        CancellationToken cancellationToken = default) =>
        await context.Promotions
            .AsNoTracking()
            .Where(promotion => promotion.IsActive && promotion.StartDate <= at && promotion.EndDate >= at)
            .OrderBy(promotion => promotion.EndDate)
            .Include(promotion => promotion.PromotionProducts)
                .ThenInclude(promotionProduct => promotionProduct.Product)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public Task AddAsync(Promotion promotion, CancellationToken cancellationToken = default) =>
        context.Promotions.AddAsync(promotion, cancellationToken).AsTask();

    public void Update(Promotion promotion) => context.Promotions.Update(promotion);

    public async Task AddProductAsync(
        PromotionProduct promotionProduct,
        CancellationToken cancellationToken = default)
    {
        var isTracked = context.PromotionProducts.Local.Any(existing =>
            existing.PromotionId == promotionProduct.PromotionId &&
            existing.ProductId == promotionProduct.ProductId);

        if (isTracked || await context.PromotionProducts.AnyAsync(existing =>
                existing.PromotionId == promotionProduct.PromotionId &&
                existing.ProductId == promotionProduct.ProductId,
                cancellationToken))
        {
            return;
        }

        await context.PromotionProducts.AddAsync(promotionProduct, cancellationToken);
    }

    public async Task RemoveProductAsync(
        long promotionId,
        long productId,
        CancellationToken cancellationToken = default)
    {
        var promotionProduct = await context.PromotionProducts.FirstOrDefaultAsync(existing =>
            existing.PromotionId == promotionId && existing.ProductId == productId,
            cancellationToken);

        if (promotionProduct is not null)
        {
            context.PromotionProducts.Remove(promotionProduct);
        }
    }
}
