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

    [Theory]
    [InlineData("user")] [InlineData("inactive")] [InlineData("table")] [InlineData("occupied")]
    [InlineData("missing-table")] [InlineData("client")] [InlineData("empty")] [InlineData("product")]
    [InlineData("unavailable")] [InlineData("quantity")]
    public async Task Create_rejects_invalid_inputs_without_writes(string scenario)
    {
        var request = CreateRequest();
        switch (scenario)
        {
            case "user": request.UserId = 99; break;
            case "inactive": f.User.IsActive = false; break;
            case "table": request.TableId = 99; break;
            case "occupied": f.Table.Status = TableStatus.Occupied; break;
            case "missing-table": request.TableId = null; break;
            case "client": request.ClientId = 99; break;
            case "empty": request.Items.Clear(); break;
            case "product": request.Items.First().ProductId = 99; break;
            case "unavailable": f.Product.IsAvailable = false; break;
            case "quantity": request.Items.First().Quantity = 0; break;
        }
        Assert.True((await f.OrderService.CreateOrderAsync(request, f.Token)).IsFailure);
        f.NoSave();
        f.Orders.Verify(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Takeaway_supports_optional_table_and_client()
    {
        var request = CreateRequest(); request.Type = OrderType.TakeAway; request.TableId = null; request.ClientId = 1;
        var result = await f.OrderService.CreateOrderAsync(request);
        Assert.True(result.IsSuccess); Assert.Null(result.Value!.TableId); Assert.Equal(1, result.Value.ClientId);
        Assert.Equal(TableStatus.Available, f.Table.Status);
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

    [Theory]
    [InlineData(OrderStatus.Paid)] [InlineData(OrderStatus.Cancelled)]
    public async Task Terminal_orders_cannot_be_edited_or_reopened(OrderStatus status)
    {
        f.Order.Status = status;
        Assert.True((await f.OrderService.AddItemAsync(5, new() { ProductId = 3, Quantity = 1 })).IsFailure);
        Assert.True((await f.OrderService.UpdateItemAsync(5, new() { OrderDetailId = 6, Quantity = 3 })).IsFailure);
        Assert.True((await f.OrderService.RemoveItemAsync(5, 6)).IsFailure);
        Assert.True((await f.OrderService.RequestAccountAsync(5, new() { Version = 5 })).IsFailure);
        Assert.True((await f.OrderService.UpdateStatusAsync(5, new() { Status = OrderStatus.Served, Version = 5 })).IsFailure);
        Assert.Equal(status, f.Order.Status);
        f.NoSave();
    }

    [Fact]
    public async Task Paid_status_requires_payment_workflow()
    {
        Assert.True((await f.OrderService.UpdateStatusAsync(5, new() { Status = OrderStatus.Paid, Version = 5 })).IsFailure);
        Assert.Equal(OrderStatus.Pending, f.Order.Status);
        f.NoSave();
    }

    [Theory]
    [InlineData("add")] [InlineData("update")] [InlineData("remove")] [InlineData("status")] [InlineData("account")] [InlineData("get")]
    public async Task Missing_order_is_failure(string action)
    {
        var result = action switch
        {
            "add" => await f.OrderService.AddItemAsync(99, new() { Quantity = 1 }),
            "update" => await f.OrderService.UpdateItemAsync(99, new() { Quantity = 1 }),
            "remove" => await f.OrderService.RemoveItemAsync(99, 6),
            "status" => await f.OrderService.UpdateStatusAsync(99, new()),
            "account" => await f.OrderService.RequestAccountAsync(99, new()),
            _ => await f.OrderService.GetByIdAsync(99)
        };
        Assert.True(result.IsFailure); f.NoSave();
    }

    [Fact]
    public async Task Item_validation_has_no_side_effects()
    {
        Assert.True((await f.OrderService.AddItemAsync(5, new() { Quantity = 0 })).IsFailure);
        Assert.True((await f.OrderService.UpdateItemAsync(5, new() { Quantity = -1 })).IsFailure);
        Assert.True((await f.OrderService.AddItemAsync(5, new() { ProductId = 99, Quantity = 1 })).IsFailure);
        Assert.True((await f.OrderService.UpdateItemAsync(5, new() { OrderDetailId = 99, Quantity = 1 })).IsFailure);
        Assert.True((await f.OrderService.RemoveItemAsync(5, 99)).IsFailure);
        f.Product.IsAvailable = false;
        Assert.True((await f.OrderService.UpdateItemAsync(5, new() { OrderDetailId = 6, Quantity = 1 })).IsFailure);
        Assert.Equal(25m, f.Order.Total); f.NoSave();
    }

    [Fact]
    public async Task Account_and_status_update_preserve_mapping()
    {
        var account = await f.OrderService.RequestAccountAsync(5, new() { Version = 5 }, f.Token);
        Assert.True(account.Value!.AccountRequested);
        f.Order.Status = OrderStatus.Ready;
        var status = await f.OrderService.UpdateStatusAsync(5, new() { Status = OrderStatus.Served, Version = 5 }, f.Token);
        Assert.Equal(OrderStatus.Served, status.Value!.Status);
        var get = await f.OrderService.GetByIdAsync(5, f.Token);
        Assert.Equal(7, get.Value!.TableNumber); Assert.Single(get.Value.Details);
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
    public async Task Kitchen_happy_path_and_list()
    {
        f.Orders.Setup(x => x.GetKitchenOrdersAsync(f.Token)).ReturnsAsync([f.Order]);
        Assert.Single((await f.KitchenService.GetKitchenOrdersAsync(f.Token)).Value!);
        Assert.Equal(OrderStatus.InPreparation, (await f.KitchenService.MarkInPreparationAsync(5, f.Token)).Value!.Status);
        Assert.Equal(OrderStatus.Ready, (await f.KitchenService.MarkReadyAsync(5, f.Token)).Value!.Status);
        Assert.True((await f.KitchenService.MarkReadyAsync(99)).IsFailure);
    }

    [Theory]
    [InlineData(OrderStatus.Paid)] [InlineData(OrderStatus.Cancelled)]
    public async Task Kitchen_rejects_terminal_states(OrderStatus status)
    {
        f.Order.Status = status;
        Assert.True((await f.KitchenService.MarkReadyAsync(5)).IsFailure);
        Assert.True((await f.KitchenService.MarkInPreparationAsync(5)).IsFailure);
        f.NoSave();
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
    [InlineData("order")] [InlineData("paid")] [InlineData("cancelled")] [InlineData("user")]
    [InlineData("inactive")] [InlineData("zero")] [InlineData("amount")] [InlineData("duplicate")] [InlineData("table")]
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

    [Fact]
    public async Task Payment_failure_is_propagated_not_reported_as_success()
    {
        var failure = new InvalidOperationException("simulated save failure");
        f.Work.Setup(x => x.SaveChangesAsync(f.Token)).ThrowsAsync(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.PaymentService.ProcessPaymentAsync(new() { OrderId = 5, UserId = 1, Amount = 25m }, f.Token)));
        Assert.False(f.InTransaction);
        // A mock transaction cannot establish database rollback; real rollback remains an integration test.
    }

    [Fact]
    public async Task Takeaway_payment_and_payment_lookup()
    {
        f.Order.Table = null; f.Order.TableId = null;
        Assert.True((await f.PaymentService.ProcessPaymentAsync(new() { OrderId = 5, UserId = 1, Amount = 25m })).IsSuccess);
        f.Tables.Verify(x => x.Update(It.IsAny<Table>()), Times.Never);
        Assert.True((await f.PaymentService.GetByOrderIdAsync(99)).IsFailure);
        f.Payments.Setup(x => x.GetByOrderIdAsync(5, f.Token)).ReturnsAsync(new Payment { OrderId = 5, Amount = 25m });
        Assert.Equal(25m, (await f.PaymentService.GetByOrderIdAsync(5, f.Token)).Value!.Amount);
    }
}
