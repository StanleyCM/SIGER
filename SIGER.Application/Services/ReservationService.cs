using SIGER.Application.Base;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Application.Services;

public class ReservationService : IReservationService
{
    private readonly IReservationRepository _reservationRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITableRepository _tableRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orders;

    public ReservationService(IReservationRepository reservationRepository, IUserRepository userRepository, ITableRepository tableRepository, IUnitOfWork unitOfWork, IOrderRepository orders)
    {
        _reservationRepository = reservationRepository;
        _userRepository = userRepository;
        _tableRepository = tableRepository;
        _unitOfWork = unitOfWork;
        _orders = orders;
    }

    public Task<Result<ReservationDto>> CreateAsync(CreateReservationRequestDto request, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(token => CreateInTransactionAsync(request, token), cancellationToken);

    private async Task<Result<ReservationDto>> CreateInTransactionAsync(CreateReservationRequestDto request, CancellationToken cancellationToken)
    {
        var error = Validate(request.NumberOfPeople, request.ReservationDateTime);
        if (error is not null) return Result<ReservationDto>.Failure(error);
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null) return Result<ReservationDto>.Failure("User not found.");
        if (!user.IsActive) return Result<ReservationDto>.Failure("User is inactive.");
        var table = await _tableRepository.GetByIdForUpdateAsync(request.TableId, cancellationToken);
        if (table is null) return Result<ReservationDto>.Failure("Table not found.");
        error = await ValidateTableAsync(table, request.NumberOfPeople, request.ReservationDateTime, null, true, cancellationToken);
        if (error is not null) return Result<ReservationDto>.Failure(error);
        var now = DateTimeOffset.UtcNow;
        var reservation = new Reservation
        {
            UserId = user.Id, User = user, TableId = table.Id, Table = table,
            ReservationDateTime = request.ReservationDateTime, NumberOfPeople = request.NumberOfPeople,
            Status = ReservationStatus.Pending, Notes = Normalize(request.Notes), CreatedAt = now, UpdatedAt = now
        };
        await _reservationRepository.AddAsync(reservation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ReservationDto>.Success(Map(reservation));
    }

    public async Task<Result<ReservationDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var reservation = await _reservationRepository.GetByIdAsync(id, cancellationToken);
        return reservation is null ? Result<ReservationDto>.Failure("Reservation not found.") : Result<ReservationDto>.Success(Map(reservation));
    }

    public async Task<Result<PaginatedResult<ReservationDto>>> GetPagedAsync(int pageNumber, int pageSize, long? userId = null, long? tableId = null, ReservationStatus? status = null, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 200 || ((long)pageNumber - 1) * pageSize > int.MaxValue) return Result<PaginatedResult<ReservationDto>>.Failure("Page number and size must be positive, size at most 200, and offset within the supported range.");
        var page = await _reservationRepository.GetPagedAsync(pageNumber, pageSize, userId, tableId, status, cancellationToken);
        return Result<PaginatedResult<ReservationDto>>.Success(new(page.Items.Select(Map), page.TotalCount, page.PageNumber, page.PageSize));
    }

    public Task<Result<ReservationDto>> UpdateAsync(long id, UpdateReservationRequestDto request, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(token => UpdateInTransactionAsync(id, request, token), cancellationToken);

    private async Task<Result<ReservationDto>> UpdateInTransactionAsync(long id, UpdateReservationRequestDto request, CancellationToken cancellationToken)
    {
        var error = Validate(request.NumberOfPeople, request.ReservationDateTime);
        if (error is not null) return Result<ReservationDto>.Failure(error);
        var reservation = await _reservationRepository.GetByIdForUpdateAsync(id, cancellationToken);
        if (reservation is null) return Result<ReservationDto>.Failure("Reservation not found.");
        // Guest identity cannot be converted to a registered user (or vice versa) by an edit.
        if (reservation.UserId.HasValue != request.UserId.HasValue) return Result<ReservationDto>.Failure("Reservation identity type cannot be changed.");
        User? user = null;
        if (request.UserId.HasValue)
        {
            user = await _userRepository.GetByIdAsync(request.UserId.Value, cancellationToken);
            if (user is null) return Result<ReservationDto>.Failure("User not found.");
            if (!user.IsActive) return Result<ReservationDto>.Failure("User is inactive.");
        }
        var preorder = await _orders.GetByReservationIdAsync(id, cancellationToken);
        if (preorder is not null && preorder.Status is not (OrderStatus.PreOrdered or OrderStatus.Cancelled))
            return Result<ReservationDto>.Failure("A reservation with an operational order cannot be rescheduled.");
        var table = await _tableRepository.GetByIdForUpdateAsync(request.TableId, cancellationToken);
        if (table is null) return Result<ReservationDto>.Failure("Table not found.");
        error = await ValidateTableAsync(table, request.NumberOfPeople, request.ReservationDateTime, id,
            IsBlocking(reservation.Status), cancellationToken);
        if (error is not null) return Result<ReservationDto>.Failure(error);
        reservation.UserId = user?.Id;
        reservation.User = user;
        reservation.TableId = table.Id;
        reservation.Table = table;
        reservation.ReservationDateTime = request.ReservationDateTime;
        reservation.NumberOfPeople = request.NumberOfPeople;
        reservation.Notes = Normalize(request.Notes);
        if (reservation.AccessTokenExpiresAt.HasValue)
            reservation.AccessTokenExpiresAt = request.ReservationDateTime.ToUniversalTime().AddHours(2);
        if (preorder?.Status == OrderStatus.PreOrdered)
        {
            preorder.TableId = table.Id;
            preorder.UpdatedAt = DateTimeOffset.UtcNow;
            _orders.Update(preorder);
        }
        reservation.UpdatedAt = DateTimeOffset.UtcNow;
        _reservationRepository.Update(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ReservationDto>.Success(Map(reservation));
    }

    public Task<Result> ChangeStatusAsync(long id, UpdateReservationStatusRequestDto request, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(token => ChangeStatusInTransactionAsync(id, request, token), cancellationToken);

    private async Task<Result> ChangeStatusInTransactionAsync(long id, UpdateReservationStatusRequestDto request, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdForUpdateAsync(id, cancellationToken);
        if (reservation is null) return Result.Failure("Reservation not found.");
        if (IsBlocking(request.Status))
        {
            if (reservation.UserId.HasValue)
            {
                var user = await _userRepository.GetByIdAsync(reservation.UserId.Value, cancellationToken);
                if (user is null || !user.IsActive) return Result.Failure("User does not exist or is inactive.");
            }
            var error = Validate(reservation.NumberOfPeople, reservation.ReservationDateTime);
            if (error is not null) return Result.Failure(error);
            var table = await _tableRepository.GetByIdForUpdateAsync(reservation.TableId, cancellationToken);
            if (table is null) return Result.Failure("Table not found.");
            error = await ValidateTableAsync(table, reservation.NumberOfPeople, reservation.ReservationDateTime, id, true, cancellationToken);
            if (error is not null) return Result.Failure(error);
        }
        if (request.Status == ReservationStatus.Cancelled)
        {
            var preorder = await _orders.GetByReservationIdAsync(id, cancellationToken);
            if (preorder?.Status == OrderStatus.PreOrdered)
            {
                preorder.Status = OrderStatus.Cancelled;
                preorder.UpdatedAt = DateTimeOffset.UtcNow;
                _orders.Update(preorder);
            }
        }
        reservation.Status = request.Status;
        reservation.UpdatedAt = DateTimeOffset.UtcNow;
        _reservationRepository.Update(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static string? Validate(int numberOfPeople, DateTimeOffset reservationDateTime)
        => numberOfPeople <= 0 ? "Number of people must be greater than zero."
            : reservationDateTime <= DateTimeOffset.UtcNow ? "Reservation date must be in the future." : null;
    private static bool IsBlocking(ReservationStatus status) => status is ReservationStatus.Pending or ReservationStatus.Confirmed;

    private async Task<string?> ValidateTableAsync(Table table, int people, DateTimeOffset start, long? excludingId,
        bool blocks, CancellationToken cancellationToken)
    {
        // Occupied/Reserved describes the current table, not availability two hours on a future date.
        if (table.Status == TableStatus.OutOfService) return "Table is out of service.";
        if (people > table.Capacity) return "Number of people exceeds table capacity.";
        if (start > DateTimeOffset.MaxValue.AddHours(-2)) return "Reservation date is outside the supported range.";
        if (blocks && await _reservationRepository.HasOverlapAsync(table.Id, start, start.AddHours(2), excludingId, cancellationToken))
            return "The table already has an overlapping reservation.";
        return null;
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ReservationDto Map(Reservation reservation) => new()
    {
        Id = reservation.Id, UserId = reservation.UserId, TableId = reservation.TableId, TableNumber = reservation.Table?.Number,
        ContactName = reservation.ContactName, ContactPhone = reservation.ContactPhone, ContactEmail = reservation.ContactEmail,
        ReservationDateTime = reservation.ReservationDateTime, NumberOfPeople = reservation.NumberOfPeople, Status = reservation.Status,
        Notes = reservation.Notes, CreatedAt = reservation.CreatedAt, UpdatedAt = reservation.UpdatedAt
    };
}
