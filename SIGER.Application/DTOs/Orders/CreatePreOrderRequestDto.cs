using System.ComponentModel.DataAnnotations;

namespace SIGER.Application.DTOs.Orders;

public sealed class CreatePreOrderRequestDto
{
    [Required, MinLength(1), MaxLength(50)]
    public List<PreOrderItemRequestDto> Items { get; set; } = [];
}

public sealed class PreOrderItemRequestDto
{
    [Range(1, long.MaxValue)] public long ProductId { get; set; }
    [Range(1, 100)] public int Quantity { get; set; }
    [StringLength(300)] public string? Notes { get; set; }
}
