using SIGER.Domain.Base;

namespace SIGER.Domain.Entities;

public class Promotion : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public decimal DiscountPercentage { get; set; }

    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<PromotionProduct> PromotionProducts { get; set; } = new List<PromotionProduct>();
}
