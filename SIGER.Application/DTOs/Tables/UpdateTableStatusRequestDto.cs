using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Tables;

public sealed class UpdateTableStatusRequestDto
{
    public TableStatus Status { get; set; }
    public long Version { get; set; }
}
