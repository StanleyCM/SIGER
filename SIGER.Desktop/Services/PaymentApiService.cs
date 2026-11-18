using System.Net;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Payments;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Services;

internal sealed class PaymentApiService(ApiClient api)
{
    internal Task<PaymentDto> ProcessPaymentAsync(
        long orderId,
        decimal amount,
        PaymentMethod method,
        string? reference = null,
        OrderDto? currentOrder = null,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0)
            throw new ApiException("Selecciona una orden válida para registrar el pago.");
        if (amount <= 0)
            throw new ApiException("El monto a cobrar debe ser mayor que cero.");
        if (!Enum.IsDefined(method))
            throw new ApiException("Selecciona un método de pago válido.");
        if (reference is not null && reference.Trim().Length > 150)
            throw new ApiException("La referencia del pago no puede superar los 150 caracteres.");

        if (currentOrder is not null)
        {
            if (currentOrder.Id != orderId)
                throw new ApiException("La orden seleccionada no coincide con la solicitud de pago.");
            if (!CanProcessPayment(currentOrder.Status))
                throw new ApiException("El estado actual de la orden no permite registrar un pago.");
            if (amount != currentOrder.Total)
                throw new ApiException("El monto enviado debe coincidir exactamente con el total de la orden calculado por el servidor.");
        }

        var request = new ProcessPaymentRequestDto
        {
            OrderId = orderId,
            Amount = amount,
            Method = method,
            Reference = Normalize(reference)
        };

        return api.SendAsync<PaymentDto>(
            HttpMethod.Post,
            "api/v1/payments",
            request,
            cancellationToken: cancellationToken);
    }

    internal Task<PaymentDto> GetByOrderIdAsync(long orderId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(orderId, 0);
        return api.SendAsync<PaymentDto>(
            HttpMethod.Get,
            $"api/v1/payments/order/{orderId}",
            cancellationToken: cancellationToken);
    }

    internal async Task<PaymentDto?> TryGetByOrderIdAsync(long orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    internal static bool CanProcessPayment(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.InPreparation or OrderStatus.Ready or OrderStatus.Served;

    internal static bool CanProcessPayment(OrderDto? order, PaymentDto? existingPayment = null) =>
        order is not null && existingPayment is null && CanProcessPayment(order.Status) && order.Total > 0;

    internal static string FormatMethod(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Efectivo",
        PaymentMethod.Card => "Tarjeta",
        PaymentMethod.Transfer => "Transferencia",
        PaymentMethod.Other => "Otro",
        _ => method.ToString()
    };

    internal static string FormatStatus(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Pendiente",
        PaymentStatus.Completed => "Completado",
        PaymentStatus.Failed => "Fallido",
        PaymentStatus.Refunded => "Reembolsado",
        _ => status.ToString()
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
