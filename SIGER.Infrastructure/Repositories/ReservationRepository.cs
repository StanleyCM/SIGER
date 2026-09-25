using Microsoft.EntityFrameworkCore;
using SIGER.Application.Base;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public class ReservationRepository : IReservationRepository
{
    private readonly SIGERDbContext _context;

    public ReservationRepository(SIGERDbContext context) => _context = context;

    public Task<Reservation?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => _context.Reservations.Include(reservation => reservation.User).Include(reservation => reservation.Table)
            .FirstOrDefaultAsync(reservation => reservation.Id == id, cancellationToken);

    public async Task<Reservation?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required to lock a reservation.");
        var reservation = await _context.Reservations.FromSqlInterpolated($"SELECT * FROM public.reserva WHERE id_reserva = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        // The API's ownership check may already have tracked this reservation before the lock.
        if (reservation is not null) await _context.Entry(reservation).ReloadAsync(cancellationToken);
        return reservation;
    }

    public Task<bool> HasOverlapAsync(long tableId, DateTimeOffset start, DateTimeOffset end, long? excludingId = null, CancellationToken cancellationToken = default)
        => _context.Reservations.AnyAsync(reservation => reservation.TableId == tableId &&
            (!excludingId.HasValue || reservation.Id != excludingId.Value) &&
            (reservation.Status == ReservationStatus.Pending || reservation.Status == ReservationStatus.Confirmed) &&
            reservation.ReservationDateTime < end && reservation.ReservationDateTime.AddHours(2) > start, cancellationToken);

    public async Task<PaginatedResult<Reservation>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        long? userId = null,
        long? tableId = null,
        ReservationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Reservations.AsNoTracking().Include(reservation => reservation.User).Include(reservation => reservation.Table).AsQueryable();
        if (userId.HasValue) query = query.Where(reservation => reservation.UserId == userId.Value);
        if (tableId.HasValue) query = query.Where(reservation => reservation.TableId == tableId.Value);
        if (status.HasValue) query = query.Where(reservation => reservation.Status == status.Value);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(reservation => reservation.ReservationDateTime).ThenBy(reservation => reservation.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new PaginatedResult<Reservation>(items, totalCount, pageNumber, pageSize);
    }

    public async Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default)
        => await _context.Reservations.AddAsync(reservation, cancellationToken);

    public void Update(Reservation reservation) => _context.Reservations.Update(reservation);
}
