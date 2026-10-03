using System.ComponentModel.DataAnnotations;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Exceptions;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Application.Services;

public sealed class GuestReservationService(IReservationRepository reservations, ITableRepository tables,
    IGuestReservationTokenService tokens, IUnitOfWork work, TimeProvider clock) : IGuestReservationService
{
    public Task<Result<GuestReservationCreatedDto>> CreateAsync(CreateGuestReservationRequestDto request, CancellationToken cancellationToken = default)
        => work.ExecuteInTransactionAsync(async ct =>
        {
            var now = clock.GetUtcNow();
            if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true))
                return Result<GuestReservationCreatedDto>.Failure("Invalid guest contact, party size or notes.");
            if (request.ReservationDateTime <= now || request.ReservationDateTime > DateTimeOffset.MaxValue.AddHours(-2))
                return Result<GuestReservationCreatedDto>.Failure("Reservation date must be in the future and within the supported range.");

            // All reservation writers lock the table before checking overlap. Use a stable lock order.
            var candidates = await tables.GetReservationCandidateIdsAsync(request.NumberOfPeople, ct);
            foreach (var id in candidates.Order())
            {
                var table = await tables.GetByIdForUpdateAsync(id, ct);
                if (table is null || table.Status == TableStatus.OutOfService || table.Capacity < request.NumberOfPeople) continue;
                if (await reservations.HasOverlapAsync(id, request.ReservationDateTime, request.ReservationDateTime.AddHours(2), null, ct)) continue;
                now = clock.GetUtcNow();
                if (request.ReservationDateTime <= now)
                    return Result<GuestReservationCreatedDto>.Failure("Reservation date must be in the future and within the supported range.");
                var credential = tokens.Generate();
                var reservation = new Reservation
                {
                    TableId = id, Table = table, UserId = null, Status = ReservationStatus.Pending,
                    ContactName = request.Name.Trim(), ContactPhone = request.Phone.Trim(), ContactEmail = Normalize(request.Email),
                    ReservationDateTime = request.ReservationDateTime.ToUniversalTime(), NumberOfPeople = request.NumberOfPeople,
                    Notes = Normalize(request.Notes), CreatedAt = now, UpdatedAt = now,
                    AccessTokenHash = tokens.Hash(credential), AccessTokenExpiresAt = request.ReservationDateTime.ToUniversalTime().AddHours(2)
                };
                await reservations.AddAsync(reservation, ct);
                await work.SaveChangesAsync(ct);
                return Result<GuestReservationCreatedDto>.Success(new()
                {
                    Reservation = Map(reservation), AccessToken = credential, AccessTokenExpiresAt = reservation.AccessTokenExpiresAt.Value
                });
            }
            return Result<GuestReservationCreatedDto>.Failure("No table is available for this reservation.");
        }, cancellationToken);

    public async Task<Result<GuestReservationDto>> GetAsync(long id, string? credential, CancellationToken cancellationToken = default)
    {
        var reservation = await reservations.GetByIdAsync(id, cancellationToken);
        RequireCredential(reservation, credential, tokens, clock.GetUtcNow());
        return Result<GuestReservationDto>.Success(Map(reservation!));
    }

    internal static void RequireCredential(Reservation? reservation, string? credential, IGuestReservationTokenService tokens, DateTimeOffset now)
    {
        // Identical response for nonexistent IDs, missing credentials and expired credentials.
        if (!tokens.Validate(credential, reservation?.AccessTokenHash, reservation?.AccessTokenExpiresAt, now) || reservation is null)
            throw new UnauthorizedException("Invalid reservation credential.");
    }

    internal static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static GuestReservationDto Map(Reservation r) => new()
    {
        Id = r.Id, ReservationDateTime = r.ReservationDateTime, NumberOfPeople = r.NumberOfPeople, Status = r.Status, Notes = r.Notes
    };
}
