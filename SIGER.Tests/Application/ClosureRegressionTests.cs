using Moq;
using SIGER.Application.DTOs.Orders;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Tests.Application;

public class ClosureRegressionTests
{
    [Theory]
    [InlineData("users")] [InlineData("categories")] [InlineData("products")] [InlineData("tables")]
    [InlineData("orders")] [InlineData("reservations")] [InlineData("promotions")] [InlineData("audits")]
    public async Task Pagination_rejects_overflow_before_repository_access(string service)
    {
        var f = new ServiceFixture();
        SIGER.Application.Base.Result result = service switch
        {
            "users" => await f.UserService.GetPagedAsync(int.MaxValue, 200),
            "categories" => await f.CategoryService.GetPagedAsync(int.MaxValue, 200),
            "products" => await f.ProductService.GetPagedAsync(int.MaxValue, 200),
            "tables" => await f.TableService.GetPagedAsync(int.MaxValue, 200),
            "orders" => await f.OrderService.GetPagedAsync(int.MaxValue, 200),
            "reservations" => await f.ReservationService.GetPagedAsync(int.MaxValue, 200),
            "promotions" => await f.PromotionService.GetPagedAsync(int.MaxValue, 200),
            _ => await f.AuditService.GetPagedAsync(int.MaxValue, 200)
        };
        Assert.True(result.IsFailure);
    }
    [Fact]
    public async Task Order_reads_start_inside_the_transaction()
    {
        var f = new ServiceFixture();
        f.Users.Setup(x => x.GetByIdAsync(1, f.Token)).ReturnsAsync(() =>
        {
            Assert.True(f.InTransaction);
            return f.User;
        });
        Assert.True((await f.OrderService.CreateOrderAsync(new CreateOrderRequestDto
        {
            UserId = 1, TableId = 4, Type = OrderType.Table,
            Items = [new() { ProductId = 3, Quantity = 1 }]
        }, f.Token)).IsSuccess);
    }

    [Fact]
    public async Task Payment_reads_start_inside_the_transaction()
    {
        var f = new ServiceFixture();
        f.Orders.Setup(x => x.GetWithDetailsAsync(5, f.Token)).ReturnsAsync(() =>
        {
            Assert.True(f.InTransaction);
            return f.Order;
        });
        Assert.True((await f.PaymentService.ProcessPaymentAsync(new()
        {
            OrderId = 5, UserId = 1, Amount = 25, Method = PaymentMethod.Cash
        }, f.Token)).IsSuccess);
        f.Tables.Verify(x => x.GetByIdForUpdateAsync(4, f.Token), Times.Once);
    }

    [Fact]
    public async Task Repeated_order_products_are_not_queried_per_detail()
    {
        var f = new ServiceFixture();
        Assert.True((await f.OrderService.CreateOrderAsync(new()
        {
            UserId = 1, TableId = 4, Type = OrderType.Table,
            Items = [new() { ProductId = 3, Quantity = 1 }, new() { ProductId = 3, Quantity = 2 }]
        }, f.Token)).IsSuccess);
        f.Products.Verify(x => x.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Products.Verify(x => x.GetByIdsAsync(It.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new long[] { 3 })), f.Token), Times.Once);
    }

    [Fact]
    public async Task Promotion_fetches_products_in_one_batch()
    {
        var f = new ServiceFixture();
        var start = DateTimeOffset.UtcNow.AddDays(1);
        Assert.True((await f.PromotionService.CreateAsync(new()
        { Name = "Test", DiscountPercentage = 10, StartDate = start, EndDate = start.AddHours(1), ProductIds = [3, 3] }, f.Token)).IsSuccess);
        f.Products.Verify(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<long>>(), f.Token), Times.Once);
        f.Products.Verify(x => x.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

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
    [InlineData(OrderStatus.Ready, false)]
    [InlineData(OrderStatus.Ready, true)]
    [InlineData(OrderStatus.Served, false)]
    [InlineData(OrderStatus.Served, true)]
    [InlineData(OrderStatus.InPreparation, false)]
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

    [Theory]
    [InlineData("inactive")] [InlineData("capacity")] [InlineData("out-of-service")] [InlineData("overlap")]
    public async Task Reservation_create_and_update_reject_invalid_state(string scenario)
    {
        var f = new ServiceFixture(); var start = DateTimeOffset.UtcNow.AddDays(2);
        if (scenario == "inactive") f.User.IsActive = false;
        if (scenario == "capacity") f.Table.Capacity = 1;
        if (scenario == "out-of-service") f.Table.Status = TableStatus.OutOfService;
        f.Reservations.Setup(x => x.HasOverlapAsync(4, start, start.AddHours(2), It.IsAny<long?>(), f.Token))
            .ReturnsAsync(() => { Assert.True(f.InTransaction); return scenario == "overlap"; });
        f.Reservations.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(new Reservation { Id = 8, Status = ReservationStatus.Pending });
        Assert.True((await f.ReservationService.CreateAsync(new() { UserId = 1, TableId = 4, NumberOfPeople = 2, ReservationDateTime = start }, f.Token)).IsFailure);
        Assert.True((await f.ReservationService.UpdateAsync(8, new() { UserId = 1, TableId = 4, NumberOfPeople = 2, ReservationDateTime = start }, f.Token)).IsFailure);
        f.NoSave();
    }

    [Theory]
    [InlineData(ReservationStatus.Pending)] [InlineData(ReservationStatus.Confirmed)]
    public async Task Reactivating_reservation_rechecks_availability(ReservationStatus next)
    {
        var f = new ServiceFixture(); var start = DateTimeOffset.UtcNow.AddDays(2);
        var reservation = new Reservation { Id = 8, UserId = 1, TableId = 4, NumberOfPeople = 2,
            ReservationDateTime = start, Status = ReservationStatus.Cancelled };
        f.Reservations.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(reservation);
        f.Reservations.Setup(x => x.HasOverlapAsync(4, start, start.AddHours(2), 8, f.Token)).ReturnsAsync(true);
        Assert.True((await f.ReservationService.ChangeStatusAsync(8, new() { Status = next }, f.Token)).IsFailure);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status); f.NoSave();
    }

    [Theory]
    [InlineData(ReservationStatus.Cancelled)] [InlineData(ReservationStatus.Completed)]
    public async Task Terminal_reservations_do_not_need_a_free_slot(ReservationStatus next)
    {
        var f = new ServiceFixture();
        f.Reservations.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(new Reservation { Id = 8 });
        Assert.True((await f.ReservationService.ChangeStatusAsync(8, new() { Status = next }, f.Token)).IsSuccess);
        f.Reservations.Verify(x => x.HasOverlapAsync(It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
