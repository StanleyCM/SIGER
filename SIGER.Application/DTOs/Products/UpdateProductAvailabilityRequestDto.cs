namespace SIGER.Application.DTOs.Products;

public sealed class UpdateProductAvailabilityRequestDto
{
    public bool IsAvailable { get; set; }
    public long Version { get; set; }
}
