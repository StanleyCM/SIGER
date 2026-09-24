namespace SIGER.Application.DTOs.Categories;

public sealed class CreateCategoryRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
