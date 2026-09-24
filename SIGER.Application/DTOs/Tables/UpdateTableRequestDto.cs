namespace SIGER.Application.DTOs.Tables;

public sealed class UpdateTableRequestDto
{
    public int Number { get; set; }
    public int Capacity { get; set; }
    public string? Location { get; set; }
    public long Version { get; set; }
}
