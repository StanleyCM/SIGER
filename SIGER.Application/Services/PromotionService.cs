using SIGER.Application.Base;
using SIGER.Application.DTOs.Promotions;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;

namespace SIGER.Application.Services;

public class PromotionService : IPromotionService
{
    private readonly IPromotionRepository _promotionRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PromotionService(IPromotionRepository promotionRepository, IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _promotionRepository = promotionRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PromotionDto>> CreateAsync(CreatePromotionRequestDto request, CancellationToken cancellationToken = default)
    {
        var error = Validate(request.Name, request.DiscountPercentage, request.StartDate, request.EndDate);
        if (error is not null) return Result<PromotionDto>.Failure(error);
        var productIds = request.ProductIds.Distinct().ToArray();
        var products = await _productRepository.GetByIdsAsync(productIds, cancellationToken);
        var foundIds = products.Select(product => product.Id).ToHashSet();
        foreach (var productId in productIds)
        {
            if (!foundIds.Contains(productId)) return Result<PromotionDto>.Failure($"Product {productId} not found.");
        }
        var promotion = new Promotion
        {
            Name = request.Name.Trim(), Description = Normalize(request.Description), DiscountPercentage = request.DiscountPercentage,
            StartDate = request.StartDate, EndDate = request.EndDate, IsActive = request.IsActive, CreatedAt = DateTimeOffset.UtcNow
        };
        foreach (var product in products)
        {
            promotion.PromotionProducts.Add(new PromotionProduct { Promotion = promotion, ProductId = product.Id, Product = product });
        }
        await _unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
        {
            await _promotionRepository.AddAsync(promotion, transactionToken);
            foreach (var relation in promotion.PromotionProducts)
            {
                await _promotionRepository.AddProductAsync(relation, transactionToken);
            }
            await _unitOfWork.SaveChangesAsync(transactionToken);
        }, cancellationToken);
        return Result<PromotionDto>.Success(Map(promotion));
    }

    public async Task<Result<PromotionDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.GetByIdAsync(id, cancellationToken);
        return promotion is null ? Result<PromotionDto>.Failure("Promotion not found.") : Result<PromotionDto>.Success(Map(promotion));
    }

    public async Task<Result<PaginatedResult<PromotionDto>>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 200 || ((long)pageNumber - 1) * pageSize > int.MaxValue) return Result<PaginatedResult<PromotionDto>>.Failure("Page number and size must be positive, size at most 200, and offset within the supported range.");
        var page = await _promotionRepository.GetPagedAsync(pageNumber, pageSize, cancellationToken);
        return Result<PaginatedResult<PromotionDto>>.Success(new(page.Items.Select(Map), page.TotalCount, page.PageNumber, page.PageSize));
    }

    public async Task<Result<IReadOnlyCollection<PromotionDto>>> GetActiveAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var promotions = await _promotionRepository.GetActiveAsync(at, cancellationToken);
        return Result<IReadOnlyCollection<PromotionDto>>.Success(promotions.Select(Map).ToArray());
    }

    public async Task<Result<PromotionDto>> UpdateAsync(long id, UpdatePromotionRequestDto request, CancellationToken cancellationToken = default)
    {
        var error = Validate(request.Name, request.DiscountPercentage, request.StartDate, request.EndDate);
        if (error is not null) return Result<PromotionDto>.Failure(error);
        var promotion = await _promotionRepository.GetByIdAsync(id, cancellationToken);
        if (promotion is null) return Result<PromotionDto>.Failure("Promotion not found.");
        promotion.Name = request.Name.Trim();
        promotion.Description = Normalize(request.Description);
        promotion.DiscountPercentage = request.DiscountPercentage;
        promotion.StartDate = request.StartDate;
        promotion.EndDate = request.EndDate;
        _promotionRepository.Update(promotion);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<PromotionDto>.Success(Map(promotion));
    }

    public async Task<Result> SetActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.GetByIdAsync(id, cancellationToken);
        if (promotion is null) return Result.Failure("Promotion not found.");
        promotion.IsActive = isActive;
        _promotionRepository.Update(promotion);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AddProductAsync(long promotionId, long productId, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.GetByIdAsync(promotionId, cancellationToken);
        if (promotion is null) return Result.Failure("Promotion not found.");
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken);
        if (product is null) return Result.Failure("Product not found.");
        if (promotion.PromotionProducts.Any(item => item.ProductId == productId)) return Result.Failure("Product is already associated with the promotion.");
        await _promotionRepository.AddProductAsync(new PromotionProduct
        {
            PromotionId = promotion.Id, Promotion = promotion, ProductId = product.Id, Product = product
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RemoveProductAsync(long promotionId, long productId, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.GetByIdAsync(promotionId, cancellationToken);
        if (promotion is null) return Result.Failure("Promotion not found.");
        if (!promotion.PromotionProducts.Any(item => item.ProductId == productId)) return Result.Failure("Product is not associated with the promotion.");
        await _promotionRepository.RemoveProductAsync(promotionId, productId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static string? Validate(string name, decimal discount, DateTimeOffset startDate, DateTimeOffset endDate)
        => string.IsNullOrWhiteSpace(name) ? "Promotion name is required."
            : discount <= 0 || discount > 100 ? "Discount percentage must be greater than zero and at most 100."
            : endDate <= startDate ? "End date must be later than start date." : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static PromotionDto Map(Promotion promotion) => new()
    {
        Id = promotion.Id, Name = promotion.Name, Description = promotion.Description, DiscountPercentage = promotion.DiscountPercentage,
        StartDate = promotion.StartDate, EndDate = promotion.EndDate, IsActive = promotion.IsActive, CreatedAt = promotion.CreatedAt,
        Products = promotion.PromotionProducts.Select(item => new PromotionProductDto
        {
            ProductId = item.ProductId, ProductName = item.Product?.Name ?? string.Empty
        }).ToArray()
    };
}
