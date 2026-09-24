namespace SIGER.Application.DTOs.Orders;

public sealed class RequestAccountDto
{
    public bool AccountRequested { get; set; } = true;
    public long Version { get; set; }
}
