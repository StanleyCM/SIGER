using SIGER.Application.Base;
using SIGER.Application.DTOs.Reservations;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Services;

internal sealed class ReservationApiService(ApiClient api)
{
    internal Task<PaginatedResult<ReservationDto>> GetPagedAsync(
        int pageNumber = 1,
        int pageSize = 20,
        long? userId = null,
        long? tableId = null,
        ReservationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "La paginación de reservaciones debe estar entre 1 y 200.");
        if (userId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId), "El identificador de usuario debe ser positivo.");
        if (tableId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tableId), "El identificador de mesa debe ser positivo.");

        var query = new List<string>(5)
        {
            $"pageNumber={pageNumber}",
            $"pageSize={pageSize}"
        };
        if (userId.HasValue) query.Add($"userId={userId.Value}");
        if (tableId.HasValue) query.Add($"tableId={tableId.Value}");
        if (status.HasValue) query.Add($"status={Uri.EscapeDataString(status.Value.ToString())}");

        return api.SendAsync<PaginatedResult<ReservationDto>>(
            HttpMethod.Get,
            "api/v1/reservations?" + string.Join('&', query),
            cancellationToken: cancellationToken);
    }

    internal Task<ReservationDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync<ReservationDto>(HttpMethod.Get, $"api/v1/reservations/{id}", cancellationToken: cancellationToken);
    }

    internal Task<ReservationDto> CreateAsync(CreateReservationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UserId <= 0)
            throw new ApiException("Selecciona un usuario válido para la reservación interna.");
        if (request.TableId <= 0)
            throw new ApiException("Selecciona una mesa válida para la reservación.");
        if (request.NumberOfPeople <= 0)
            throw new ApiException("La cantidad de personas debe ser mayor que cero.");
        if (request.ReservationDateTime <= DateTimeOffset.UtcNow)
            throw new ApiException("La fecha y hora de la reservación debe ser futura.");

        var body = new CreateReservationRequestDto
        {
            UserId = request.UserId,
            TableId = request.TableId,
            ReservationDateTime = request.ReservationDateTime.ToUniversalTime(),
            NumberOfPeople = request.NumberOfPeople,
            Notes = Normalize(request.Notes)
        };
        return api.SendAsync<ReservationDto>(HttpMethod.Post, "api/v1/reservations", body, cancellationToken: cancellationToken);
    }

    internal Task<ReservationDto> UpdateAsync(long id, UpdateReservationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        ArgumentNullException.ThrowIfNull(request);
        if (request.UserId is <= 0)
            throw new ApiException("El usuario asociado a la reservación no es válido.");
        if (request.TableId <= 0)
            throw new ApiException("Selecciona una mesa válida para la reservación.");
        if (request.NumberOfPeople <= 0)
            throw new ApiException("La cantidad de personas debe ser mayor que cero.");
        if (request.ReservationDateTime <= DateTimeOffset.UtcNow)
            throw new ApiException("La fecha y hora de la reservación debe ser futura.");

        var body = new UpdateReservationRequestDto
        {
            UserId = request.UserId,
            TableId = request.TableId,
            ReservationDateTime = request.ReservationDateTime.ToUniversalTime(),
            NumberOfPeople = request.NumberOfPeople,
            Notes = Normalize(request.Notes)
        };
        return api.SendAsync<ReservationDto>(HttpMethod.Put, $"api/v1/reservations/{id}", body, cancellationToken: cancellationToken);
    }

    internal Task ChangeStatusAsync(long id, ReservationStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        if (!Enum.IsDefined(status))
            throw new ApiException("Selecciona un estado de reservación válido.");

        return api.SendAsync(
            HttpMethod.Patch,
            $"api/v1/reservations/{id}/status",
            new UpdateReservationStatusRequestDto { Status = status },
            cancellationToken: cancellationToken);
    }

    internal static string FormatStatus(ReservationStatus status) => status switch
    {
        ReservationStatus.Pending => "Pendiente",
        ReservationStatus.Confirmed => "Confirmada",
        ReservationStatus.Cancelled => "Cancelada",
        ReservationStatus.Completed => "Completada",
        _ => status.ToString()
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
