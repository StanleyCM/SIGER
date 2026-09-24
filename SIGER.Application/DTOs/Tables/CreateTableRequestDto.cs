using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Tables;

public sealed class CreateTableRequestDto
{
    public int Number { get; set; }
    public int Capacity { get; set; }
    public TableStatus Status { get; set; } = TableStatus.Available;
    public string? Location { get; set; }
}
