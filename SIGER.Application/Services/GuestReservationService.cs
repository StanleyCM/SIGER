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

public sealed class GuestReservationService(IReservationRepository reservations, ITableRepository tables, IOrderRepository orders,
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
            var table = await FindAvailableTableAsync(request.ReservationDateTime, request.NumberOfPeople, null, tables.GetByIdForUpdateAsync, ct);
            if (table is not null)
            {
                now = clock.GetUtcNow();
                if (request.ReservationDateTime <= now)
                    return Result<GuestReservationCreatedDto>.Failure("Reservation date must be in the future and within the supported range.");
                var credential = tokens.Generate();
                var reservation = new Reservation
                {
                    TableId = table.Id, Table = table, UserId = null, Status = ReservationStatus.Pending,
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

    public async Task<Result<IReadOnlyList<ReservationAvailabilityDto>>> GetAvailabilityAsync(
        ReservationAvailabilityRequestDto request, string? credential = null, CancellationToken cancellationToken = default)
    {
        var today = GuestReservationSchedule.Today(clock.GetUtcNow());
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true)
            || request.Date is not { } date || date < today || date > today.AddDays(GuestReservationSchedule.MaxAdvanceDays))
            return Result<IReadOnlyList<ReservationAvailabilityDto>>.Failure("Select a date within the next 30 days and a valid party size.");
        if (request.ReservationId is { } id)
            RequireCredential(await reservations.GetByIdAsync(id, cancellationToken), credential, tokens, clock.GetUtcNow());

        var candidates = await tables.GetReservationCandidateIdsAsync(request.NumberOfPeople, cancellationToken);
        // Request-local read cache: availability never takes write locks or modifies records.
        var cache = new Dictionary<long, Table?>();
        async Task<Table?> ReadTable(long id, CancellationToken ct)
        {
            if (!cache.TryGetValue(id, out var table)) cache[id] = table = await tables.GetByIdAsync(id, ct);
            return table;
        }
        var result = new List<ReservationAvailabilityDto>();
        foreach (var time in GuestReservationSchedule.Slots)
        {
            var start = GuestReservationSchedule.Start(date, time);
            var available = start > clock.GetUtcNow()
                && await FindAvailableTableAsync(start, request.NumberOfPeople, request.ReservationId, ReadTable, cancellationToken, candidates) is not null;
            result.Add(new(time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), available && start > clock.GetUtcNow()));
        }
        return Result<IReadOnlyList<ReservationAvailabilityDto>>.Success(result);
    }

    // Creation/editing supply locked reads; the advisory GET supplies ordinary reads.
    // Both paths share capacity/status/overlap rules and the same repository query.
    private async Task<Table?> FindAvailableTableAsync(DateTimeOffset start, int people, long? excludingId,
        Func<long, CancellationToken, Task<Table?>> readTable, CancellationToken ct, IReadOnlyCollection<long>? candidates = null)
    {
        candidates ??= await tables.GetReservationCandidateIdsAsync(people, ct);
        foreach (var id in candidates.Order())
        {
            var table = await readTable(id, ct);
            if (table is null || table.Status == TableStatus.OutOfService || table.Capacity < people) continue;
            if (await reservations.HasOverlapAsync(id, start, start.AddHours(GuestReservationSchedule.DurationHours), excludingId, ct)) continue;
            return table;
        }
        return null;
    }

    public async Task<Result<GuestReservationDto>> GetAsync(long id, string? credential, CancellationToken cancellationToken = default)
    {
        var reservation = await reservations.GetByIdAsync(id, cancellationToken);
        RequireCredential(reservation, credential, tokens, clock.GetUtcNow());
        return Result<GuestReservationDto>.Success(Map(reservation!));
    }

    public async Task<Result<GuestReservationDto>> UpdateAsync(long id, string? credential, UpdateGuestReservationRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireCredential(await reservations.GetByIdAsync(id, cancellationToken), credential, tokens, clock.GetUtcNow());
        return await work.ExecuteInTransactionAsync(async ct =>
        {
            // Serialize public edits with preorder creation and administrative reservation changes.
            var reservation = await reservations.GetByIdForUpdateAsync(id, ct);
            var now = clock.GetUtcNow();
            RequireCredential(reservation, credential, tokens, now);
            if (reservation!.Status != ReservationStatus.Pending || reservation.ReservationDateTime <= now)
                return Result<GuestReservationDto>.Failure("Only future pending reservations can be edited.");
            if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true))
                return Result<GuestReservationDto>.Failure("Invalid guest contact, party size or notes.");

            var start = (request.ReservationDateTime ?? reservation.ReservationDateTime).ToUniversalTime();
            var people = request.NumberOfPeople ?? reservation.NumberOfPeople;
            if (start <= now || start > DateTimeOffset.MaxValue.AddHours(-2))
                return Result<GuestReservationDto>.Failure("Reservation date must be in the future and within the supported range.");
            var preorder = await orders.GetByReservationIdAsync(id, ct);
            if (preorder is not null && preorder.Status is not (OrderStatus.PreOrdered or OrderStatus.Cancelled))
                return Result<GuestReservationDto>.Failure("A reservation with an operational order cannot be rescheduled.");

            Table? assigned = null;
            if (start != reservation.ReservationDateTime || people != reservation.NumberOfPeople)
            {
                assigned = await FindAvailableTableAsync(start, people, id, tables.GetByIdForUpdateAsync, ct);
                if (assigned is null) return Result<GuestReservationDto>.Failure("No table is available for this reservation.");
            }
            now = clock.GetUtcNow();
            RequireCredential(reservation, credential, tokens, now);
            if (reservation.ReservationDateTime <= now || start <= now)
                return Result<GuestReservationDto>.Failure("Only future pending reservations can be edited.");

            // Mutate only after every check; both records commit or roll back together.
            if (assigned is not null)
            {
                reservation.TableId = assigned.Id;
                reservation.Table = assigned;
                if (preorder?.Status == OrderStatus.PreOrdered && preorder.TableId != assigned.Id)
                {
                    preorder.TableId = assigned.Id;
                    preorder.Table = assigned;
                    preorder.UpdatedAt = now;
                }
            }
            if (request.HasName) reservation.ContactName = request.Name!.Trim();
            if (request.HasPhone) reservation.ContactPhone = request.Phone!;
            if (request.HasEmail) reservation.ContactEmail = Normalize(request.Email);
            if (request.HasNotes) reservation.Notes = Normalize(request.Notes);
            reservation.NumberOfPeople = people;
            reservation.ReservationDateTime = start;
            reservation.AccessTokenExpiresAt = start.AddHours(2);
            reservation.UpdatedAt = now;
            // Locked repository reads are tracked. Save only changed properties, not the entire related graph.
            await work.SaveChangesAsync(ct);
            return Result<GuestReservationDto>.Success(Map(reservation));
        }, cancellationToken);
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
        Id = r.Id, Name = r.ContactName ?? string.Empty, Phone = r.ContactPhone ?? string.Empty, Email = r.ContactEmail,
        AccessExpiresAt = r.AccessTokenExpiresAt,
        ReservationDateTime = r.ReservationDateTime, NumberOfPeople = r.NumberOfPeople, Status = r.Status, Notes = r.Notes
    };
}
