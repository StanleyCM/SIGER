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

    public ReservationService(IReservationRepository reservationRepository, IUserRepository userRepository, ITableRepository tableRepository, IUnitOfWork unitOfWork)
    {
        _reservationRepository = reservationRepository;
        _userRepository = userRepository;
        _tableRepository = tableRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReservationDto>> CreateAsync(CreateReservationRequestDto request, CancellationToken cancellationToken = default)
    {
        var error = Validate(request.NumberOfPeople, request.ReservationDateTime);
        if (error is not null) return Result<ReservationDto>.Failure(error);
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null) return Result<ReservationDto>.Failure("User not found.");
        var table = await _tableRepository.GetByIdAsync(request.TableId, cancellationToken);
        if (table is null) return Result<ReservationDto>.Failure("Table not found.");
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
        if (pageNumber < 1 || pageSize < 1) return Result<PaginatedResult<ReservationDto>>.Failure("Page number and page size must be greater than zero.");
        var page = await _reservationRepository.GetPagedAsync(pageNumber, pageSize, userId, tableId, status, cancellationToken);
        return Result<PaginatedResult<ReservationDto>>.Success(new(page.Items.Select(Map), page.TotalCount, page.PageNumber, page.PageSize));
    }

    public async Task<Result<ReservationDto>> UpdateAsync(long id, UpdateReservationRequestDto request, CancellationToken cancellationToken = default)
    {
        var error = Validate(request.NumberOfPeople, request.ReservationDateTime);
        if (error is not null) return Result<ReservationDto>.Failure(error);
        var reservation = await _reservationRepository.GetByIdAsync(id, cancellationToken);
        if (reservation is null) return Result<ReservationDto>.Failure("Reservation not found.");
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null) return Result<ReservationDto>.Failure("User not found.");
        var table = await _tableRepository.GetByIdAsync(request.TableId, cancellationToken);
        if (table is null) return Result<ReservationDto>.Failure("Table not found.");
        reservation.UserId = user.Id;
        reservation.User = user;
        reservation.TableId = table.Id;
        reservation.Table = table;
        reservation.ReservationDateTime = request.ReservationDateTime;
        reservation.NumberOfPeople = request.NumberOfPeople;
        reservation.Notes = Normalize(request.Notes);
        reservation.UpdatedAt = DateTimeOffset.UtcNow;
        _reservationRepository.Update(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ReservationDto>.Success(Map(reservation));
    }

    public async Task<Result> ChangeStatusAsync(long id, UpdateReservationStatusRequestDto request, CancellationToken cancellationToken = default)
    {
        var reservation = await _reservationRepository.GetByIdAsync(id, cancellationToken);
        if (reservation is null) return Result.Failure("Reservation not found.");
        reservation.Status = request.Status;
        reservation.UpdatedAt = DateTimeOffset.UtcNow;
        _reservationRepository.Update(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static string? Validate(int numberOfPeople, DateTimeOffset reservationDateTime)
        => numberOfPeople <= 0 ? "Number of people must be greater than zero."
            : reservationDateTime <= DateTimeOffset.UtcNow ? "Reservation date must be in the future." : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ReservationDto Map(Reservation reservation) => new()
    {
        Id = reservation.Id, UserId = reservation.UserId, TableId = reservation.TableId, TableNumber = reservation.Table?.Number,
        ReservationDateTime = reservation.ReservationDateTime, NumberOfPeople = reservation.NumberOfPeople, Status = reservation.Status,
        Notes = reservation.Notes, CreatedAt = reservation.CreatedAt, UpdatedAt = reservation.UpdatedAt
    };
}
