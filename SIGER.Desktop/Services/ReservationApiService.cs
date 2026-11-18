using System.Net;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Services;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Services;

internal sealed record ReservationArrivalStatus(bool CanReceive, bool IsLate, string Label);

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

    internal async Task<OrderDto?> TryGetPreOrderAsync(long reservationId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(reservationId, 0);
        try
        {
            return await api.SendAsync<OrderDto>(
                HttpMethod.Get,
                $"api/v1/reservations/{reservationId}/preorder",
                cancellationToken: cancellationToken);
        }
        catch (ApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    internal Task<ReservationDto> ReceiveAsync(long reservationId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(reservationId, 0);
        return api.SendAsync<ReservationDto>(
            HttpMethod.Post,
            $"api/v1/reservations/{reservationId}/receive",
            cancellationToken: cancellationToken);
    }

    internal Task<OrderDto> ActivatePreOrderAsync(long reservationId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(reservationId, 0);
        return api.SendAsync<OrderDto>(
            HttpMethod.Post,
            $"api/v1/reservations/{reservationId}/activate-preorder",
            cancellationToken: cancellationToken);
    }

    internal static ReservationArrivalStatus EvaluateArrivalWindow(DateTimeOffset reservationDateTime, DateTimeOffset now)
    {
        var offset = now - reservationDateTime;
        if (offset < TimeSpan.FromMinutes(-ReservationService.MaxEarlyArrivalMinutes))
            return new(false, false, "Futura (fuera de ventana de recepción anticipada)");
        if (offset >= TimeSpan.FromHours(ReservationService.ArrivalDurationHours))
            return new(false, true, "Vencida (ventana de 2 horas expirada)");
        if (now > reservationDateTime)
            return new(true, true, "Llegada tardía (dentro del turno de 2 horas)");
        return new(true, false, "En horario de recepción");
    }

    internal static string FormatOperationalState(ReservationDto reservation, OrderDto? linkedOrder)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        return reservation.Status switch
        {
            ReservationStatus.Cancelled => "Reserva cancelada",
            ReservationStatus.Completed when linkedOrder is null =>
                "Cliente recibido (sin preorden · lista para abrir orden en mesa)",
            ReservationStatus.Completed when linkedOrder.Status == OrderStatus.Paid =>
                $"Reserva completada · Orden #{linkedOrder.Id} pagada",
            ReservationStatus.Completed when linkedOrder.Status == OrderStatus.Cancelled =>
                $"Cliente recibido · Orden #{linkedOrder.Id} cancelada",
            ReservationStatus.Completed =>
                $"Cliente recibido · Orden #{linkedOrder.Id} activa ({OrderApiService.FormatStatus(linkedOrder.Status)})",
            ReservationStatus.Confirmed when linkedOrder?.Status == OrderStatus.PreOrdered =>
                $"Reserva confirmada · Preorden Web #{linkedOrder.Id} lista para activar",
            ReservationStatus.Confirmed =>
                "Reserva confirmada (pendiente de llegada del cliente)",
            _ when linkedOrder?.Status == OrderStatus.PreOrdered =>
                $"Reserva pendiente · Preorden Web #{linkedOrder.Id} lista para activar",
            _ => "Reserva pendiente (pendiente de llegada del cliente)"
        };
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
