using System.Text.Json;
using Moq;
using SIGER.Application.Exceptions;
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
    [InlineData("missing")] [InlineData("unavailable")] [InlineData("inactive")] [InlineData("zero")]
    [InlineData("negative")] [InlineData("many")] [InlineData("notes")] [InlineData("empty")]
    [InlineData("null")] [InlineData("nullitem")] [InlineData("duplicate")] [InlineData("tooManyItems")]
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
    public async Task Query_requires_credential_for_same_reservation()
    {
        var f = new GuestFixture(); await f.Preorders.CreateAsync(10, f.Credential, f.Items);
        Assert.True((await f.Preorders.GetAsync(10, f.Credential)).IsSuccess);
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Preorders.GetAsync(11, f.Credential));
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Preorders.GetAsync(10, null));
    }

    [Fact]
    public async Task Preorder_rejects_payment_account_kitchen_and_generic_order_mutations()
    {
        var f = new GuestFixture(); f.F.Order.Status = OrderStatus.PreOrdered;
        Assert.True((await f.F.PaymentService.ProcessPaymentAsync(new() { OrderId = 5, UserId = 1, Amount = 25 })).IsFailure);
        Assert.True((await f.F.OrderService.RequestAccountAsync(5, new() { Version = 5 })).IsFailure);
        Assert.True((await f.F.OrderService.UpdateStatusAsync(5, new() { Status = OrderStatus.Cancelled, Version = 5 })).IsFailure);
        Assert.True((await f.F.OrderService.AddItemAsync(5, new() { ProductId = 3, Quantity = 1 })).IsFailure);
        Assert.True((await f.F.OrderService.UpdateItemAsync(5, new() { Quantity = 1 })).IsFailure);
        Assert.True((await f.F.OrderService.RemoveItemAsync(5, 6)).IsFailure);
        Assert.True((await f.F.KitchenService.MarkInPreparationAsync(5)).IsFailure);
        Assert.True((await f.F.KitchenService.MarkReadyAsync(5)).IsFailure);
        f.F.NoSave(); f.F.Tables.Verify(x => x.Update(It.IsAny<Table>()), Times.Never);
    }
}
