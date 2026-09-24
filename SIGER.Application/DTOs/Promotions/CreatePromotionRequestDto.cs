namespace SIGER.Application.DTOs.Promotions;

public sealed class CreatePromotionRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal DiscountPercentage { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<long> ProductIds { get; set; } = new List<long>();
}
