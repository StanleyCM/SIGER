namespace SIGER.Application.DTOs.Auth;

// Internal provider snapshot for conditional compensation; never returned by a controller.
public sealed record AuthUserStateDto(Guid Id, string Email, DateTimeOffset UpdatedAt,
    DateTimeOffset CreatedAt, DateTimeOffset? BannedUntil, Guid? OperationId)
{
    public bool IsActive => BannedUntil is null || BannedUntil <= DateTimeOffset.UtcNow;
}
