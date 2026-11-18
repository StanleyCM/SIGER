using System.Text.Json;
using Moq;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.Exceptions;
using SIGER.Application.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Tests.Application;

public class PreOrderTests
{
    [Fact]
    public async Task Preorder_uses_server_prices_links_reservation_and_has_no_operational_side_effects()
    {
        var f = new GuestFixture(); f.F.Table.Status = TableStatus.Occupied;
        var result = await f.Preorders.CreateAsync(10, f.Credential, f.Items);
        Assert.True(result.IsSuccess); var o = f.SavedOrder!;
        Assert.Equal(10, o.ReservationId); Assert.Equal(4, o.TableId); Assert.Null(o.UserId); Assert.Null(o.ClientId);
        Assert.Equal(OrderStatus.PreOrdered, o.Status); Assert.Equal(OrderOrigin.Web, o.Origin); Assert.Equal(OrderType.Table, o.Type);
        Assert.Equal(25m, o.Total); Assert.Equal(12.5m, Assert.Single(o.Details).UnitPrice); Assert.Equal("Sin sal", o.Details.Single().Note);
        Assert.False(o.AccountRequested); Assert.Equal(TableStatus.Occupied, f.F.Table.Status);
        f.F.Tables.Verify(x => x.Update(It.IsAny<Table>()), Times.Never);
        var json = JsonSerializer.Serialize(result.Value); Assert.DoesNotContain("UserId", json); Assert.DoesNotContain("TableId", json);
    }

    [Fact]
    public async Task Duplicate_is_conflict_and_does_not_replace_history()
    {
        var f = new GuestFixture(); await f.Preorders.CreateAsync(10, f.Credential, f.Items); var first = f.SavedOrder;
        Assert.Equal("A preorder already exists for this reservation.", (await f.Preorders.CreateAsync(10, f.Credential, f.Items)).Error);
        Assert.Same(first, f.SavedOrder);
        f.F.Orders.Verify(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unavailable")]
    [InlineData("inactive")]
    public async Task Invalid_items_are_rejected_without_writes(string kind)
    {
        var f = new GuestFixture(); var request = f.Items;
        switch (kind)
        {
            case "missing": request.Items[0].ProductId = 99; break;
            case "unavailable": f.F.Product.IsAvailable = false; break;
            case "inactive": f.F.Category.IsActive = false; break;
            case "zero": request.Items[0].Quantity = 0; break;
            case "negative": request.Items[0].Quantity = -1; break;
            case "many": request.Items[0].Quantity = 101; break;
            case "notes": request.Items[0].Notes = new string('x', 301); break;
            case "empty": request.Items.Clear(); break;
            case "null": request.Items = null!; break;
            case "nullitem": request.Items = [null!]; break;
            case "duplicate": request.Items.Add(request.Items[0]); break;
            case "tooManyItems": request.Items = Enumerable.Repeat(request.Items[0], 51).ToList(); break;
        }
        Assert.True((await f.Preorders.CreateAsync(10, f.Credential, request)).IsFailure); f.F.NoSave();
    }

    [Theory]
    [InlineData(ReservationStatus.Cancelled)] [InlineData(ReservationStatus.Completed)]
    public async Task Terminal_reservation_rejects_preorder(ReservationStatus status)
    {
        var f = new GuestFixture(); f.Reservation.Status = status;
        Assert.True((await f.Preorders.CreateAsync(10, f.Credential, f.Items)).IsFailure); f.F.NoSave();
    }

    [Fact]
    public async Task Past_reservation_is_rejected_even_with_valid_credential()
    {
        var f = new GuestFixture(); f.Reservation.ReservationDateTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.True((await f.Preorders.CreateAsync(10, f.Credential, f.Items)).IsFailure); f.F.NoSave();
    }

    [Fact]
    public async Task Credential_is_revalidated_after_lock_and_before_writes()
    {
        var f = new GuestFixture();
        f.F.Reservations.Setup(x => x.GetByIdForUpdateAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(() =>
        { Assert.True(f.F.InTransaction); f.Reservation.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); return f.Reservation; });
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Preorders.CreateAsync(10, f.Credential, f.Items)); f.F.NoSave();
    }

    [Fact]
    public async Task Preorder_rejects_payment_account_kitchen_and_generic_order_mutations()
    {
        var f = new GuestFixture(); f.F.Order.Status = OrderStatus.PreOrdered;
        Assert.True((await f.F.OrderService.RequestAccountAsync(5, new() { Version = 5 })).IsFailure);
        Assert.True((await f.F.OrderService.UpdateStatusAsync(5, new() { Status = OrderStatus.Cancelled, Version = 5 })).IsFailure);
        Assert.True((await f.F.OrderService.AddItemAsync(5, new() { ProductId = 3, Quantity = 1 })).IsFailure);
        Assert.True((await f.F.OrderService.UpdateItemAsync(5, new() { Quantity = 1 })).IsFailure);
        Assert.True((await f.F.OrderService.RemoveItemAsync(5, 6)).IsFailure);
        Assert.True((await f.F.KitchenService.MarkInPreparationAsync(5)).IsFailure);
        Assert.True((await f.F.KitchenService.MarkReadyAsync(5)).IsFailure);
        f.F.NoSave(); f.F.Tables.Verify(x => x.Update(It.IsAny<Table>()), Times.Never);
    }

    [Fact]
    public async Task Staff_lookup_and_preorder_activation_preserve_ids_prices_notes_and_prevent_duplicate_activation()
    {
        var f = new GuestFixture();
        Assert.True((await f.Preorders.CreateAsync(10, f.Credential, f.Items)).IsSuccess);
        f.SavedOrder!.Details.Single().Id = 31;

        // Catalog price changes and guest token expires before arrival; staff lookup and activation must still succeed and preserve snapshot prices.
        f.F.Product.Price = 99m;
        f.Reservation.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        f.Reservation.ReservationDateTime = DateTimeOffset.UtcNow;

        var lookup = await f.F.ReservationService.GetReservationOrderAsync(10);
        Assert.True(lookup.IsSuccess);
        Assert.Equal(20, lookup.Value!.Id);
        Assert.Equal(10, lookup.Value.ReservationId);
        Assert.Equal(OrderStatus.PreOrdered, lookup.Value.Status);
        Assert.Equal(25m, lookup.Value.Total);
        Assert.DoesNotContain("token", JsonSerializer.Serialize(lookup.Value), StringComparison.OrdinalIgnoreCase);

        var activated = await f.F.ReservationService.ActivatePreOrderAsync(10, 1);
        Assert.True(activated.IsSuccess);
        Assert.Equal(20, activated.Value!.Id);
        Assert.Equal(10, activated.Value.ReservationId);
        Assert.Equal(4, activated.Value.TableId);
        Assert.Equal(1, activated.Value.UserId);
        Assert.Equal(OrderStatus.Pending, activated.Value.Status);
        Assert.Equal(OrderOrigin.Web, activated.Value.Origin);
        Assert.Equal(25m, activated.Value.Total);
        var line = Assert.Single(activated.Value.Details);
        Assert.Equal(31, line.Id);
        Assert.Equal(3, line.ProductId);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(12.50m, line.UnitPrice);
        Assert.Equal(25m, line.Subtotal);
        Assert.Equal("Sin sal", line.Note);

        Assert.Equal(ReservationStatus.Completed, f.Reservation.Status);
        Assert.Equal(TableStatus.Occupied, f.F.Table.Status);

        var duplicate = await f.F.ReservationService.ActivatePreOrderAsync(10, 1);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("The preorder has already been activated.", duplicate.Error);

        Assert.Equal("Cancelled or completed reservations cannot be modified.",
            (await f.F.ReservationService.UpdateAsync(10, new() { TableId = 4, NumberOfPeople = 2, ReservationDateTime = DateTimeOffset.UtcNow.AddDays(1) })).Error);
        Assert.Equal("A completed reservation cannot change status.",
            (await f.F.ReservationService.ChangeStatusAsync(10, new() { Status = ReservationStatus.Cancelled })).Error);
    }

    [Fact]
    public async Task Reception_without_preorder_completes_reservation_and_allows_single_linked_operational_order()
    {
        var f = new GuestFixture();
        f.Reservation.ReservationDateTime = DateTimeOffset.UtcNow;
        Assert.Equal("Order not found.", (await f.F.ReservationService.GetReservationOrderAsync(10)).Error);
        Assert.Equal("Order not found.", (await f.F.ReservationService.ActivatePreOrderAsync(10, 1)).Error);

        var received = await f.F.ReservationService.ReceiveAsync(10, 1);
        Assert.True(received.IsSuccess);
        Assert.Equal(ReservationStatus.Completed, f.Reservation.Status);
        Assert.Equal(TableStatus.Available, f.F.Table.Status);
        Assert.Equal("The reservation has already been received.", (await f.F.ReservationService.ReceiveAsync(10, 1)).Error);

        var createdOrder = await f.F.OrderService.CreateOrderAsync(new()
        {
            UserId = 1,
            ReservationId = 10,
            TableId = 4,
            Origin = OrderOrigin.Desktop,
            Type = OrderType.Table,
            Items = [new() { ProductId = 3, Quantity = 2, Note = "Recién ordenada" }]
        });
        Assert.True(createdOrder.IsSuccess);
        Assert.Equal(10, createdOrder.Value!.ReservationId);
        Assert.Equal(OrderStatus.Pending, createdOrder.Value.Status);
        Assert.Equal(TableStatus.Occupied, f.F.Table.Status);

        Assert.Equal("An order already exists for this reservation.",
            (await f.F.OrderService.CreateOrderAsync(new()
            {
                UserId = 1,
                ReservationId = 10,
                TableId = 4,
                Origin = OrderOrigin.Desktop,
                Type = OrderType.Table,
                Items = [new() { ProductId = 3, Quantity = 1 }]
            })).Error);
    }

    [Theory]
    [InlineData(-31, false)]
    [InlineData(-30, true)]
    [InlineData(119, true)]
    [InlineData(120, false)]
    public void Arrival_window_includes_early_boundary_and_excludes_turn_end(int minutes, bool allowed)
    {
        var scheduled = DateTimeOffset.Parse("2026-11-18T19:00:00-04:00");
        Assert.Equal(allowed, ReservationService.ValidateArrivalWindow(scheduled, scheduled.AddMinutes(minutes)) is null);
    }

    [Fact]
    public async Task Direct_link_and_generic_status_cannot_bypass_reception_policy()
    {
        var f = new GuestFixture();
        f.Reservation.ReservationDateTime = DateTimeOffset.UtcNow.AddHours(1);
        var request = new CreateOrderRequestDto
        {
            UserId = 1, ReservationId = 10, TableId = 4, Type = OrderType.Table,
            Items = [new() { ProductId = 3, Quantity = 1 }]
        };
        Assert.Equal("The reservation cannot be received more than 30 minutes in advance.",
            (await f.F.OrderService.CreateOrderAsync(request)).Error);
        Assert.Equal("Use the reception workflow to complete a reservation.",
            (await f.F.ReservationService.ChangeStatusAsync(10, new() { Status = ReservationStatus.Completed })).Error);
        Assert.Equal(ReservationStatus.Pending, f.Reservation.Status);
        Assert.Equal(TableStatus.Available, f.F.Table.Status);
        f.F.NoSave();
    }

    [Fact]
    public async Task Cancelled_link_cannot_leave_guest_received_without_a_way_to_open_order()
    {
        var f = new GuestFixture();
        Assert.True((await f.Preorders.CreateAsync(10, f.Credential, f.Items)).IsSuccess);
        f.Reservation.ReservationDateTime = DateTimeOffset.UtcNow;
        f.SavedOrder!.Status = OrderStatus.Cancelled;
        f.F.Work.Invocations.Clear();
        Assert.Equal("A reservation with a cancelled order cannot be received.",
            (await f.F.ReservationService.ReceiveAsync(10, 1)).Error);
        Assert.Equal(ReservationStatus.Pending, f.Reservation.Status);
        f.F.NoSave();
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("outOfService")]
    [InlineData("occupied")]
    [InlineData("activeOrder")]
    [InlineData("tooEarly")]
    [InlineData("expiredWindow")]
    [InlineData("receiveWithPreorder")]
    public async Task Reception_and_activation_enforce_reservation_table_and_arrival_window_rules(string scenario)
    {
        var f = new GuestFixture();
        await f.Preorders.CreateAsync(10, f.Credential, f.Items);
        var savesBefore = f.F.Work.Invocations.Count(i => i.Method.Name == nameof(SIGER.Application.Interfaces.Persistence.IUnitOfWork.SaveChangesAsync));

        switch (scenario)
        {
            case "cancelled":
                f.Reservation.Status = ReservationStatus.Cancelled;
                break;
            case "outOfService":
                f.F.Table.Status = TableStatus.OutOfService;
                break;
            case "occupied":
                f.F.Table.Status = TableStatus.Occupied;
                break;
            case "activeOrder":
                f.F.Orders.Setup(x => x.HasActiveOrdersAsync(4, 20, It.IsAny<CancellationToken>())).ReturnsAsync(true);
                break;
            case "tooEarly":
                f.Reservation.ReservationDateTime = DateTimeOffset.UtcNow.AddHours(72);
                break;
            case "expiredWindow":
                f.Reservation.ReservationDateTime = DateTimeOffset.UtcNow.AddHours(-3);
                break;
            case "receiveWithPreorder":
                Assert.Equal("Use the preorder activation workflow to receive a reservation with a preorder.",
                    (await f.F.ReservationService.ReceiveAsync(10, 1)).Error);
                Assert.Equal(OrderStatus.PreOrdered, f.SavedOrder!.Status);
                return;
        }

        var result = await f.F.ReservationService.ActivatePreOrderAsync(10, 1);
        Assert.True(result.IsFailure);
        Assert.Equal(OrderStatus.PreOrdered, f.SavedOrder!.Status);
        Assert.Equal(savesBefore, f.F.Work.Invocations.Count(i => i.Method.Name == nameof(SIGER.Application.Interfaces.Persistence.IUnitOfWork.SaveChangesAsync)));
    }
}
