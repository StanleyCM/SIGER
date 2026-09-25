using SIGER.Application.Base;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Application.Interfaces.Repositories;

public interface IReservationRepository
{
    Task<Reservation?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Reservation?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default);
    Task<bool> HasOverlapAsync(long tableId, DateTimeOffset start, DateTimeOffset end, long? excludingId = null, CancellationToken cancellationToken = default);
    Task<PaginatedResult<Reservation>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        long? userId = null,
        long? tableId = null,
        ReservationStatus? status = null,
        CancellationToken cancellationToken = default);
    Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default);
    void Update(Reservation reservation);
}
