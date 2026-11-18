using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Application.Services;

public class ReservationService : IReservationService
{
    public const int MaxEarlyArrivalMinutes = 30;
    public const int ArrivalDurationHours = GuestReservationSchedule.DurationHours;
    internal const string ArrivalTooEarly = "The reservation cannot be received more than 30 minutes in advance.";
    internal const string ArrivalExpired = "The reservation arrival window has expired.";
    internal const string ArrivalOverlap = "The table already has an overlapping reservation.";

    private readonly IReservationRepository _reservationRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITableRepository _tableRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orders;
    private readonly TimeProvider _clock;

    public ReservationService(
        IReservationRepository reservationRepository,
        IUserRepository userRepository,
        ITableRepository tableRepository,
        IUnitOfWork unitOfWork,
        IOrderRepository orders,
        TimeProvider? clock = null)
    {
        _reservationRepository = reservationRepository;
        _userRepository = userRepository;
        _tableRepository = tableRepository;
        _unitOfWork = unitOfWork;
        _orders = orders;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<Result<ReservationDto>> CreateAsync(CreateReservationRequestDto request, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(token => CreateInTransactionAsync(request, token), cancellationToken);

    private async Task<Result<ReservationDto>> CreateInTransactionAsync(CreateReservationRequestDto request, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var error = Validate(request.NumberOfPeople, request.ReservationDateTime, now);
        if (error is not null) return Result<ReservationDto>.Failure(error);
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null) return Result<ReservationDto>.Failure("User not found.");
        if (!user.IsActive) return Result<ReservationDto>.Failure("User is inactive.");
        var table = await _tableRepository.GetByIdForUpdateAsync(request.TableId, cancellationToken);
        if (table is null) return Result<ReservationDto>.Failure("Table not found.");
        error = await ValidateTableAsync(table, request.NumberOfPeople, request.ReservationDateTime, null, true, cancellationToken);
        if (error is not null) return Result<ReservationDto>.Failure(error);
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
        var now = _clock.GetUtcNow();
        var error = Validate(request.NumberOfPeople, request.ReservationDateTime, now);
        if (error is not null) return Result<ReservationDto>.Failure(error);
        var reservation = await _reservationRepository.GetByIdForUpdateAsync(id, cancellationToken);
        if (reservation is null) return Result<ReservationDto>.Failure("Reservation not found.");
        if (reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Completed)
            return Result<ReservationDto>.Failure("Cancelled or completed reservations cannot be modified.");
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
            preorder.UpdatedAt = now;
            _orders.Update(preorder);
        }
        reservation.UpdatedAt = now;
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
        if (reservation.Status == ReservationStatus.Completed && request.Status != ReservationStatus.Completed)
            return Result.Failure("A completed reservation cannot change status.");
        if (reservation.Status == ReservationStatus.Cancelled && request.Status != ReservationStatus.Cancelled)
            return Result.Failure("A cancelled reservation cannot change status.");
        if (request.Status == reservation.Status && request.Status is ReservationStatus.Completed or ReservationStatus.Cancelled)
            return Result.Success();
        if (request.Status == ReservationStatus.Completed)
            return Result.Failure("Use the reception workflow to complete a reservation.");

        var preorder = await _orders.GetByReservationIdAsync(id, cancellationToken);
        if (preorder is not null && preorder.Status is not (OrderStatus.PreOrdered or OrderStatus.Cancelled))
            return Result.Failure("A reservation with an operational order cannot be cancelled or reopened.");

        var now = _clock.GetUtcNow();
        if (IsBlocking(request.Status))
        {
            if (reservation.UserId.HasValue)
            {
                var user = await _userRepository.GetByIdAsync(reservation.UserId.Value, cancellationToken);
                if (user is null || !user.IsActive) return Result.Failure("User does not exist or is inactive.");
            }
            var error = Validate(reservation.NumberOfPeople, reservation.ReservationDateTime, now);
            if (error is not null) return Result.Failure(error);
            var table = await _tableRepository.GetByIdForUpdateAsync(reservation.TableId, cancellationToken);
            if (table is null) return Result.Failure("Table not found.");
            error = await ValidateTableAsync(table, reservation.NumberOfPeople, reservation.ReservationDateTime, id, true, cancellationToken);
            if (error is not null) return Result.Failure(error);
        }
        if (request.Status == ReservationStatus.Cancelled && preorder?.Status == OrderStatus.PreOrdered)
        {
            preorder.Status = OrderStatus.Cancelled;
            preorder.UpdatedAt = now;
            _orders.Update(preorder);
        }
        reservation.Status = request.Status;
        reservation.UpdatedAt = now;
        _reservationRepository.Update(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<OrderDto>> GetReservationOrderAsync(long id, CancellationToken cancellationToken = default)
    {
        var reservation = await _reservationRepository.GetByIdAsync(id, cancellationToken);
        if (reservation is null) return Result<OrderDto>.Failure("Reservation not found.");
        var order = await _orders.GetByReservationIdAsync(id, cancellationToken);
        return order is null
            ? Result<OrderDto>.Failure("Order not found.")
            : Result<OrderDto>.Success(MapOrder(order, reservation.Table?.Number));
    }

    public Task<Result<ReservationDto>> ReceiveAsync(long id, long staffUserId, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(token => ReceiveInTransactionAsync(id, staffUserId, token), cancellationToken);

    private async Task<Result<ReservationDto>> ReceiveInTransactionAsync(long id, long staffUserId, CancellationToken cancellationToken)
    {
        var staff = await _userRepository.GetByIdAsync(staffUserId, cancellationToken);
        if (staff is null || !staff.IsActive) return Result<ReservationDto>.Failure("The responsible user does not exist or is inactive.");

        var reservation = await _reservationRepository.GetByIdForUpdateAsync(id, cancellationToken);
        if (reservation is null) return Result<ReservationDto>.Failure("Reservation not found.");
        if (reservation.Status == ReservationStatus.Cancelled)
            return Result<ReservationDto>.Failure("A cancelled reservation cannot be received or activated.");
        if (reservation.Status == ReservationStatus.Completed)
            return Result<ReservationDto>.Failure("The reservation has already been received.");

        var order = await _orders.GetByReservationIdAsync(id, cancellationToken);
        if (order is not null)
        {
            return Result<ReservationDto>.Failure(order.Status switch
            {
                OrderStatus.PreOrdered => "Use the preorder activation workflow to receive a reservation with a preorder.",
                OrderStatus.Cancelled => "A reservation with a cancelled order cannot be received.",
                _ => "The reservation has already been received."
            });
        }

        var now = _clock.GetUtcNow();
        var arrivalError = ValidateArrivalWindow(reservation.ReservationDateTime, now);
        if (arrivalError is not null) return Result<ReservationDto>.Failure(arrivalError);

        var table = await _tableRepository.GetByIdForUpdateAsync(reservation.TableId, cancellationToken);
        if (table is null) return Result<ReservationDto>.Failure("Table not found.");
        now = _clock.GetUtcNow();
        arrivalError = ValidateArrivalWindow(reservation.ReservationDateTime, now);
        if (arrivalError is not null) return Result<ReservationDto>.Failure(arrivalError);
        if (table.Status == TableStatus.OutOfService) return Result<ReservationDto>.Failure("Table is out of service.");
        if (table.Status == TableStatus.Occupied || await _orders.HasActiveOrdersAsync(table.Id, 0, cancellationToken))
            return Result<ReservationDto>.Failure("The selected table is not available.");
        if (await HasArrivalOverlapAsync(_reservationRepository, reservation, now, cancellationToken))
            return Result<ReservationDto>.Failure(ArrivalOverlap);

        reservation.Table = table;
        reservation.Status = ReservationStatus.Completed;
        reservation.UpdatedAt = now;
        _reservationRepository.Update(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ReservationDto>.Success(Map(reservation));
    }

    public Task<Result<OrderDto>> ActivatePreOrderAsync(long id, long staffUserId, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(token => ActivatePreOrderInTransactionAsync(id, staffUserId, token), cancellationToken);

    private async Task<Result<OrderDto>> ActivatePreOrderInTransactionAsync(long id, long staffUserId, CancellationToken cancellationToken)
    {
        var staff = await _userRepository.GetByIdAsync(staffUserId, cancellationToken);
        if (staff is null || !staff.IsActive) return Result<OrderDto>.Failure("The responsible user does not exist or is inactive.");

        var reservation = await _reservationRepository.GetByIdForUpdateAsync(id, cancellationToken);
        if (reservation is null) return Result<OrderDto>.Failure("Reservation not found.");
        if (reservation.Status == ReservationStatus.Cancelled)
            return Result<OrderDto>.Failure("A cancelled reservation cannot be received or activated.");

        var order = await _orders.GetByReservationIdAsync(id, cancellationToken);
        if (order is null) return Result<OrderDto>.Failure("Order not found.");
        if (order.Status == OrderStatus.Cancelled)
            return Result<OrderDto>.Failure("A cancelled reservation cannot be received or activated.");
        if (order.Status != OrderStatus.PreOrdered)
            return Result<OrderDto>.Failure("The preorder has already been activated.");
        if (reservation.Status == ReservationStatus.Completed)
            return Result<OrderDto>.Failure("The reservation has already been received.");
        if (order.Details.Count == 0)
            return Result<OrderDto>.Failure("The order must contain at least one product.");

        var now = _clock.GetUtcNow();
        var arrivalError = ValidateArrivalWindow(reservation.ReservationDateTime, now);
        if (arrivalError is not null) return Result<OrderDto>.Failure(arrivalError);

        var table = await _tableRepository.GetByIdForUpdateAsync(reservation.TableId, cancellationToken);
        if (table is null) return Result<OrderDto>.Failure("Table not found.");
        now = _clock.GetUtcNow();
        arrivalError = ValidateArrivalWindow(reservation.ReservationDateTime, now);
        if (arrivalError is not null) return Result<OrderDto>.Failure(arrivalError);
        if (table.Status == TableStatus.OutOfService) return Result<OrderDto>.Failure("Table is out of service.");
        if (table.Status == TableStatus.Occupied || await _orders.HasActiveOrdersAsync(table.Id, order.Id, cancellationToken))
            return Result<OrderDto>.Failure("The selected table is not available.");
        if (await HasArrivalOverlapAsync(_reservationRepository, reservation, now, cancellationToken))
            return Result<OrderDto>.Failure(ArrivalOverlap);

        order.TableId = table.Id;
        order.Table = table;
        order.UserId = staff.Id;
        order.User = staff;
        order.Status = OrderStatus.Pending;
        order.UpdatedAt = now;

        reservation.Table = table;
        reservation.Status = ReservationStatus.Completed;
        reservation.UpdatedAt = now;

        table.Status = TableStatus.Occupied;
        table.UpdatedAt = now;

        _orders.Update(order);
        _reservationRepository.Update(reservation);
        _tableRepository.Update(table);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<OrderDto>.Success(MapOrder(order, table.Number));
    }

    public static string? ValidateArrivalWindow(DateTimeOffset reservationDateTime, DateTimeOffset now)
    {
        var offset = now - reservationDateTime;
        if (offset < TimeSpan.FromMinutes(-MaxEarlyArrivalMinutes)) return ArrivalTooEarly;
        if (offset >= TimeSpan.FromHours(ArrivalDurationHours)) return ArrivalExpired;
        return null;
    }

    internal static Task<bool> HasArrivalOverlapAsync(IReservationRepository repository, Reservation reservation,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        repository.HasOverlapAsync(reservation.TableId, now,
            reservation.ReservationDateTime.AddHours(ArrivalDurationHours), reservation.Id, cancellationToken);

    private static string? Validate(int numberOfPeople, DateTimeOffset reservationDateTime, DateTimeOffset now)
        => numberOfPeople <= 0 ? "Number of people must be greater than zero."
            : reservationDateTime <= now ? "Reservation date must be in the future." : null;
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

    private static OrderDto MapOrder(Order order, int? fallbackTableNumber = null) => new()
    {
        Id = order.Id,
        ReservationId = order.ReservationId,
        TableId = order.TableId,
        TableNumber = order.Table?.Number ?? fallbackTableNumber,
        UserId = order.UserId,
        ClientId = order.ClientId,
        OrderDateTime = order.OrderDateTime,
        Status = order.Status,
        Origin = order.Origin,
        Type = order.Type,
        Total = order.Total,
        Notes = order.Notes,
        AccountRequested = order.AccountRequested,
        UpdatedAt = order.UpdatedAt,
        Version = order.Version,
        Details = order.Details.Select(detail => new OrderDetailDto
        {
            Id = detail.Id,
            ProductId = detail.ProductId,
            ProductName = detail.Product?.Name ?? string.Empty,
            Quantity = detail.Quantity,
            UnitPrice = detail.UnitPrice,
            Subtotal = detail.Subtotal,
            Note = detail.Note
        }).ToArray()
    };
}
