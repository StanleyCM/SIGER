using Moq;
using SIGER.Application.DTOs.Orders;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Tests.Application;

public class ClosureRegressionTests
{

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Cancellation_releases_table_only_without_other_active_orders(bool anotherOrder)
    {
        var f = new ServiceFixture();
        f.Table.Status = TableStatus.Occupied;
        f.Orders.Setup(x => x.HasActiveOrdersAsync(4, 5, f.Token)).ReturnsAsync(() =>
        { Assert.True(f.InTransaction); return anotherOrder; });
        var result = await f.OrderService.UpdateStatusAsync(5, new() { Status = OrderStatus.Cancelled, Version = 5 }, f.Token);
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, f.Order.Status);
        Assert.Equal(anotherOrder ? TableStatus.Occupied : TableStatus.Available, f.Table.Status);
        f.Tables.Verify(x => x.GetByIdForUpdateAsync(4, f.Token), Times.Once);
        f.Work.Verify(x => x.SaveChangesAsync(f.Token), Times.Once);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Payment_releases_table_only_without_other_active_orders(bool anotherOrder)
    {
        var f = new ServiceFixture();
        f.Table.Status = TableStatus.Occupied;
        f.Orders.Setup(x => x.HasActiveOrdersAsync(4, 5, f.Token)).ReturnsAsync(() =>
        { Assert.True(f.InTransaction); return anotherOrder; });
        var result = await f.PaymentService.ProcessPaymentAsync(
            new() { OrderId = 5, UserId = 1, Amount = 25m, Method = PaymentMethod.Cash }, f.Token);
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Paid, f.Order.Status);
        Assert.Equal(anotherOrder ? TableStatus.Occupied : TableStatus.Available, f.Table.Status);
        f.Tables.Verify(x => x.GetByIdForUpdateAsync(4, f.Token), Times.Once);
        f.Orders.Verify(x => x.HasActiveOrdersAsync(4, 5, f.Token), Times.Once);
        f.Work.Verify(x => x.SaveChangesAsync(f.Token), Times.Once);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Table_status_change_rejects_releasing_table_with_active_orders(bool hasActiveOrder)
    {
        var f = new ServiceFixture();
        f.Table.Status = TableStatus.Occupied;
        f.Orders.Setup(x => x.HasActiveOrdersAsync(4, 0, f.Token)).ReturnsAsync(() =>
        { Assert.True(f.InTransaction); return hasActiveOrder; });
        var result = await f.TableService.ChangeStatusAsync(
            4, new() { Status = TableStatus.Available, Version = 5 }, f.Token);
        Assert.Equal(!hasActiveOrder, result.IsSuccess);
        Assert.Equal(hasActiveOrder ? TableStatus.Occupied : TableStatus.Available, f.Table.Status);
        f.Tables.Verify(x => x.GetByIdForUpdateAsync(4, f.Token), Times.Once);
        if (hasActiveOrder) f.NoSave();
        else f.Work.Verify(x => x.SaveChangesAsync(f.Token), Times.Once);
    }

    [Fact]
    public async Task Quantity_change_preserves_original_price()
    {
        var f = new ServiceFixture();
        f.Product.Price = 99;
        var result = await f.OrderService.UpdateItemAsync(5, new() { OrderDetailId = 6, Quantity = 3 }, f.Token);
        Assert.True(result.IsSuccess);
        Assert.Equal(12.50m, Assert.Single(result.Value!.Details).UnitPrice);
        Assert.Equal(37.50m, result.Value.Total);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, true)]
    [InlineData(OrderStatus.Ready, true)]
    [InlineData(OrderStatus.Served, true)]
    public async Task Kitchen_rejects_skips_repeats_and_backward_transitions(OrderStatus initial, bool ready)
    {
        var f = new ServiceFixture(); f.Order.Status = initial;
        var result = ready ? await f.KitchenService.MarkReadyAsync(5) : await f.KitchenService.MarkInPreparationAsync(5);
        Assert.True(result.IsFailure); Assert.Equal(initial, f.Order.Status); f.NoSave();
    }

    [Theory]
    [InlineData(OrderStatus.Pending)] [InlineData(OrderStatus.InPreparation)]
    public async Task Waiter_cannot_serve_before_ready(OrderStatus initial)
    {
        var f = new ServiceFixture(); f.Order.Status = initial;
        Assert.True((await f.OrderService.UpdateStatusAsync(5, new() { Status = OrderStatus.Served, Version = 5 })).IsFailure);
        f.NoSave();
    }

}
