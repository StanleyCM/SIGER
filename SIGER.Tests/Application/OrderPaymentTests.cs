using Moq;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Payments;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Domain.Exceptions;

namespace SIGER.Tests.Application;

public class OrderPaymentTests
{
    private readonly ServiceFixture f = new();
    private static CreateOrderRequestDto CreateRequest() => new()
    {
        UserId = 1, TableId = 4, Type = OrderType.Table, Origin = OrderOrigin.Desktop,
        Notes = " note ", Items = [new() { ProductId = 3, Quantity = 2 }]
    };

    [Fact]
    public async Task Create_uses_catalog_prices_and_atomic_table_occupation()
    {
        f.Orders.Setup(x => x.AddAsync(It.IsAny<Order>(), f.Token))
            .Callback<Order, CancellationToken>((o, _) =>
            {
                Assert.True(f.InTransaction); Assert.Equal(TableStatus.Occupied, f.Table.Status);
                Assert.Equal(25m, o.Total); Assert.Equal(12.50m, Assert.Single(o.Details).UnitPrice);
            }).Returns(Task.CompletedTask);
        var result = await f.OrderService.CreateOrderAsync(CreateRequest(), f.Token);
        Assert.True(result.IsSuccess);
        Assert.Equal(25m, result.Value!.Total);
        Assert.Equal("note", result.Value.Notes);
        Assert.Equal("Soup", Assert.Single(result.Value.Details).ProductName);
        Assert.DoesNotContain(typeof(CreateOrderDetailRequestDto).GetProperties(), p => p.Name is "Price" or "UnitPrice" or "Subtotal");
        f.Work.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<SIGER.Application.Base.Result<OrderDto>>>>(), f.Token), Times.Once);
        f.Work.Verify(x => x.SaveChangesAsync(f.Token), Times.Once);
    }

    [Fact]
    public async Task Item_changes_recalculate_totals()
    {
        var added = await f.OrderService.AddItemAsync(5, new() { ProductId = 3, Quantity = 1, Note = " less salt " }, f.Token);
        Assert.Equal(37.50m, added.Value!.Total);
        Assert.Equal("less salt", added.Value.Details.Last().Note);
        var updated = await f.OrderService.UpdateItemAsync(5, new() { OrderDetailId = 6, Quantity = 3 }, f.Token);
        Assert.Equal(50m, updated.Value!.Total);
        Assert.Equal(37.50m, updated.Value.Details.First().Subtotal);
        var removed = await f.OrderService.RemoveItemAsync(5, 6, f.Token);
        Assert.Equal(12.50m, removed.Value!.Total); Assert.Single(removed.Value.Details);
        f.Work.Verify(x => x.SaveChangesAsync(f.Token), Times.Exactly(3));
    }

    [Fact]
    public async Task Paid_status_requires_payment_workflow()
    {
        Assert.True((await f.OrderService.UpdateStatusAsync(5, new() { Status = OrderStatus.Paid, Version = 5 })).IsFailure);
        Assert.Equal(OrderStatus.Pending, f.Order.Status);
        f.NoSave();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stale_order_version_is_conflict_before_mutation(bool account)
    {
        if (account)
            await Assert.ThrowsAsync<BusinessRuleException>(() => f.OrderService.RequestAccountAsync(5, new() { Version = 4 }));
        else
            await Assert.ThrowsAsync<BusinessRuleException>(() => f.OrderService.UpdateStatusAsync(5, new() { Version = 4, Status = OrderStatus.Served }));
        Assert.Equal(5, f.Order.Version); Assert.False(f.Order.AccountRequested);
        Assert.Equal(OrderStatus.Pending, f.Order.Status); f.NoSave();
    }

    [Fact]
    public async Task Payment_coordinates_payment_order_and_table_inside_transaction()
    {
        f.Table.Status = TableStatus.Occupied;
        f.Payments.Setup(x => x.AddAsync(It.IsAny<Payment>(), f.Token)).Callback<Payment, CancellationToken>((p, _) =>
        {
            Assert.True(f.InTransaction); Assert.Equal(25m, p.Amount); Assert.Equal(PaymentStatus.Completed, p.Status);
        }).Returns(Task.CompletedTask);
        f.Work.Setup(x => x.SaveChangesAsync(f.Token)).Callback(() =>
        {
            Assert.True(f.InTransaction);
            Assert.Equal(OrderStatus.Paid, f.Order.Status); Assert.Equal(TableStatus.Available, f.Table.Status);
        }).ReturnsAsync(1);
        var result = await f.PaymentService.ProcessPaymentAsync(new() { OrderId = 5, UserId = 1, Amount = 25m,
            Method = PaymentMethod.Card, Reference = " ticket " }, f.Token);
        Assert.True(result.IsSuccess); Assert.Equal("ticket", result.Value!.Reference);
        Assert.Equal(PaymentMethod.Card, result.Value.Method);
        f.Work.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<SIGER.Application.Base.Result<PaymentDto>>>>(), f.Token), Times.Once);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("amount")]
    [InlineData("duplicate")]
    public async Task Payment_rejects_invalid_state_without_saving(string scenario)
    {
        var request = new ProcessPaymentRequestDto { OrderId = 5, UserId = 1, Amount = 25m };
        switch (scenario)
        {
            case "order": request.OrderId = 99; break;
            case "paid": f.Order.Status = OrderStatus.Paid; break;
            case "cancelled": f.Order.Status = OrderStatus.Cancelled; break;
            case "user": request.UserId = 99; break;
            case "inactive": f.User.IsActive = false; break;
            case "zero": request.Amount = 0; break;
            case "amount": request.Amount = 24; break;
            case "duplicate": f.Payments.Setup(x => x.GetByOrderIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(new Payment()); break;
            case "table": f.Order.Table = null; f.Order.TableId = 99; break;
        }
        Assert.True((await f.PaymentService.ProcessPaymentAsync(request)).IsFailure);
        f.NoSave(); f.Payments.Verify(x => x.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

}
