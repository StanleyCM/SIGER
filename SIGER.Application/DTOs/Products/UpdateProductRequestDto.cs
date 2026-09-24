namespace SIGER.Application.DTOs.Products;

public sealed class UpdateProductRequestDto
{
    public long CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public long Version { get; set; }
}
