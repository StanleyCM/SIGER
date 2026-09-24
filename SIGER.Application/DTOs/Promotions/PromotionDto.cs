namespace SIGER.Application.DTOs.Promotions;

public sealed class PromotionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal DiscountPercentage { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public IReadOnlyCollection<PromotionProductDto> Products { get; set; } = Array.Empty<PromotionProductDto>();
}
