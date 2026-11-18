using System.Net;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Payments;
using SIGER.Application.Services;
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
            if (GetPaymentBlockReason(currentOrder) is { } reason)
                throw new ApiException(reason);
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

    internal static bool CanProcessPayment(OrderDto? order, PaymentDto? existingPayment = null) =>
        order is not null && GetPaymentBlockReason(order, existingPayment) is null;

    internal static string? GetPaymentBlockReason(OrderDto order, PaymentDto? existingPayment = null)
    {
        if (existingPayment is not null) return "Esta orden ya tiene un pago registrado.";
        if (PaymentService.GetPaymentStateError(order.Type, order.Status, order.AccountRequested) is { } error)
            return TranslatePaymentConflict(error) ?? $"El estado actual ({OrderApiService.FormatStatus(order.Status)}) no admite cobro.";
        return order.Total <= 0 ? "El total de la orden debe ser mayor que cero." : null;
    }

    internal static string? TranslatePaymentConflict(string? detail) => detail switch
    {
        PaymentService.TableOrderNotServed => "La orden de mesa debe estar servida antes de cobrar.",
        PaymentService.AccountNotRequested => "El mesero debe solicitar la cuenta antes de cobrar.",
        "The order is already paid." or "A payment already exists for this order." => "Esta orden ya tiene un pago registrado.",
        "A cancelled order cannot be paid." => "No se puede cobrar una orden cancelada.",
        "A preorder is not operational." => "No se puede cobrar una preorden sin activar.",
        _ => null
    };

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
