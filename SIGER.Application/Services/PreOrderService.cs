using System.ComponentModel.DataAnnotations;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Application.Services;

public sealed class PreOrderService(IReservationRepository reservations, IOrderRepository orders, IProductRepository products,
    IGuestReservationTokenService tokens, IUnitOfWork work, TimeProvider clock) : IPreOrderService
{
    public async Task<Result<PreOrderDto>> CreateAsync(long reservationId, string? credential, CreatePreOrderRequestDto request, CancellationToken cancellationToken = default)
    {
        // Reject unauthenticated requests before acquiring a row lock; revalidate under the lock.
        var existing = await reservations.GetByIdAsync(reservationId, cancellationToken);
        GuestReservationService.RequireCredential(existing, credential, tokens, clock.GetUtcNow());
        return await work.ExecuteInTransactionAsync(async ct =>
        {
            var reservation = await reservations.GetByIdForUpdateAsync(reservationId, ct);
            var now = clock.GetUtcNow();
            GuestReservationService.RequireCredential(reservation, credential, tokens, now);
            if (reservation!.Status is ReservationStatus.Cancelled or ReservationStatus.Completed || reservation.ReservationDateTime <= now)
                return Result<PreOrderDto>.Failure("This reservation no longer accepts a preorder.");
            if (await orders.GetByReservationIdAsync(reservationId, ct) is not null)
                return Result<PreOrderDto>.Failure("A preorder already exists for this reservation.");
            if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true) ||
                request.Items.Any(i => i is null || !Validator.TryValidateObject(i, new ValidationContext(i), [], true)))
                return Result<PreOrderDto>.Failure("Invalid preorder items, quantities or notes.");
            if (request.Items.Select(i => i.ProductId).Distinct().Count() != request.Items.Count)
                return Result<PreOrderDto>.Failure("Combine repeated products into one item.");

            var catalog = (await products.GetWithCategoriesByIdsAsync(request.Items.Select(i => i.ProductId).ToArray(), ct)).ToDictionary(p => p.Id);
            var order = new Order
            {
                ReservationId = reservation.Id, TableId = reservation.TableId, UserId = null, ClientId = null,
                Origin = OrderOrigin.Web, Type = OrderType.Table, Status = OrderStatus.PreOrdered,
                OrderDateTime = now, UpdatedAt = now, AccountRequested = false
            };
            foreach (var item in request.Items)
            {
                if (!catalog.TryGetValue(item.ProductId, out var product) || !product.IsAvailable || product.Category?.IsActive != true)
                    return Result<PreOrderDto>.Failure("A product does not exist or is unavailable in the public catalog.");
                // Keep monetary values inside the existing numeric(12,2) contract.
                if (product.Price < 0 || product.Price > 9999999999.99m / item.Quantity)
                    return Result<PreOrderDto>.Failure("The product price is outside the supported range.");
                order.Details.Add(new()
                {
                    Order = order, ProductId = product.Id, Product = product, Quantity = item.Quantity,
                    UnitPrice = product.Price, Subtotal = product.Price * item.Quantity, Note = GuestReservationService.Normalize(item.Notes)
                });
            }
            order.Total = order.Details.Sum(d => d.Subtotal);
            if (order.Total > 9999999999.99m) return Result<PreOrderDto>.Failure("The preorder total exceeds the supported range.");
            await orders.AddAsync(order, ct);
            await work.SaveChangesAsync(ct);
            return Result<PreOrderDto>.Success(Map(order));
        }, cancellationToken);
    }

    public async Task<Result<PreOrderDto>> GetAsync(long reservationId, string? credential, CancellationToken cancellationToken = default)
    {
        var reservation = await reservations.GetByIdAsync(reservationId, cancellationToken);
        GuestReservationService.RequireCredential(reservation, credential, tokens, clock.GetUtcNow());
        var order = await orders.GetByReservationIdAsync(reservationId, cancellationToken);
        return order is null ? Result<PreOrderDto>.Failure("Order not found.") : Result<PreOrderDto>.Success(Map(order));
    }

    private static PreOrderDto Map(Order order) => new()
    {
        Id = order.Id, ReservationId = order.ReservationId!.Value, Status = order.Status, Total = order.Total,
        Items = order.Details.Select(d => new PreOrderItemDto(d.ProductId, d.Product?.Name ?? string.Empty, d.Quantity, d.UnitPrice, d.Subtotal, d.Note)).ToArray()
    };
}
