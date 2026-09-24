namespace SIGER.Application.DTOs.Users;

public sealed class UpdateUserRequestDto
{
    public long RoleId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
