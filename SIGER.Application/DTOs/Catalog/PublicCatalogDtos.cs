namespace SIGER.Application.DTOs.Catalog;

public sealed record PublicCategoryDto(long Id, string Name, string? Description);
public sealed record PublicProductDto(long Id, long CategoryId, string Name, string? Description, decimal Price, string? ImageUrl);
public sealed record PublicPromotionDto(long Id, string Name, string? Description, decimal DiscountPercentage,
    DateTimeOffset StartDate, DateTimeOffset EndDate, IReadOnlyCollection<long> ProductIds);
