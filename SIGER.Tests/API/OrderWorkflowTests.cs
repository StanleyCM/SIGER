using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Payments;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Application.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Tests.API;

public class OrderWorkflowTests
{
    [Fact]
    public async Task Real_services_complete_order_kitchen_payment_flow_through_authenticated_HTTP()
    {
        using var f = new ApiFactory();
        var table = new Table { Id = 7, Number = 7, Status = TableStatus.Available };
        var product = new Product { Id = 3, Name = "Soup", Price = 12.50m, IsAvailable = true };
        Order? order = null;
        Payment? payment = null;
        var orders = new Mock<IOrderRepository>();
        var tables = new Mock<ITableRepository>();
        var products = new Mock<IProductRepository>();
        var payments = new Mock<IPaymentRepository>();
        var reservations = new Mock<IReservationRepository>();
        var work = new Mock<IUnitOfWork>();
        var inTransaction = false;
        var transactionCount = 0;
        var saves = 0;
        f.Users.Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(f.LocalUser);
        tables.Setup(x => x.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(table);
        tables.Setup(x => x.GetByIdForUpdateAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(table);
        products.Setup(x => x.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        products.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>())).ReturnsAsync([product]);
        orders.Setup(x => x.GetWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(() => order);
        orders.Setup(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((value, _) =>
            {
                Assert.True(inTransaction);
                order = value; order.Id = 1;
                Assert.Single(order.Details).Id = 1;
            }).Returns(Task.CompletedTask);
        payments.Setup(x => x.GetByOrderIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(() => payment);
        payments.Setup(x => x.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((value, _) =>
            {
                Assert.True(inTransaction); payment = value; payment.Id = 1;
            }).Returns(Task.CompletedTask);
        work.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task> operation, CancellationToken token) =>
            {
                Assert.False(inTransaction); inTransaction = true; transactionCount++;
                try { await operation(token); } finally { inTransaction = false; }
            });
        work.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { saves++; return 1; });
        ConfigureTransaction<SIGER.Application.Base.Result<OrderDto>>();
        ConfigureTransaction<SIGER.Application.Base.Result<PaymentDto>>();
        void ConfigureTransaction<T>() => work
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<T>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<T>> operation, CancellationToken token) =>
            {
                Assert.False(inTransaction); inTransaction = true; transactionCount++;
                try { return await operation(token); } finally { inTransaction = false; }
            });
        f.ConfigureBackend = services =>
        {
            services.RemoveAll<IOrderService>(); services.AddScoped<IOrderService, OrderService>();
            services.RemoveAll<IKitchenService>(); services.AddScoped<IKitchenService, KitchenService>();
            services.RemoveAll<IPaymentService>(); services.AddScoped<IPaymentService, PaymentService>();
            services.RemoveAll<IOrderRepository>(); services.AddSingleton(orders.Object);
            services.RemoveAll<ITableRepository>(); services.AddSingleton(tables.Object);
            services.RemoveAll<IReservationRepository>(); services.AddSingleton(reservations.Object);
            services.RemoveAll<IProductRepository>(); services.AddSingleton(products.Object);
            services.RemoveAll<IPaymentRepository>(); services.AddSingleton(payments.Object);
            services.RemoveAll<IUnitOfWork>(); services.AddSingleton(work.Object);
        };

        using var waiter = f.Client("Mesero");
        var created = await waiter.PostAsJsonAsync("/api/v1/orders", new
        {
            userId = 999, tableId = 7, origin = "Desktop", type = "Table",
            items = new[] { new { productId = 3, quantity = 2, unitPrice = 0.01m } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        Assert.Equal(42, order!.UserId); Assert.Equal(25m, order.Total);
        Assert.Equal(12.50m, Assert.Single(order.Details).UnitPrice);
        Assert.Equal(TableStatus.Occupied, table.Status);
        using var cook = f.Client("Cocinero");
        Assert.Equal(HttpStatusCode.OK, (await cook.PatchAsJsonAsync("/api/v1/kitchen/orders/1/in-preparation", new { })).StatusCode);
        Assert.Equal(OrderStatus.InPreparation, order.Status);
        Assert.Equal(HttpStatusCode.OK, (await cook.PatchAsJsonAsync("/api/v1/kitchen/orders/1/ready", new { })).StatusCode);
        Assert.Equal(OrderStatus.Ready, order.Status);
        using var cashier = f.Client("Cajero");
        var tooEarly = await cashier.PostAsJsonAsync("/api/v1/payments", new { orderId = 1, amount = 25m, method = "Cash" });
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Contains(PaymentService.TableOrderNotServed, await tooEarly.Content.ReadAsStringAsync());
        f.LocalUser.Role.Name = "Mesero";
        Assert.Equal(HttpStatusCode.OK, (await waiter.PatchAsJsonAsync("/api/v1/orders/1/status", new { status = "Served", version = 0 })).StatusCode);
        f.LocalUser.Role.Name = "Cajero";
        var noAccount = await cashier.PostAsJsonAsync("/api/v1/payments", new { orderId = 1, amount = 25m, method = "Cash" });
        Assert.Equal(HttpStatusCode.Conflict, noAccount.StatusCode);
        Assert.Contains(PaymentService.AccountNotRequested, await noAccount.Content.ReadAsStringAsync());
        Assert.Null(payment);
        f.LocalUser.Role.Name = "Mesero";
        Assert.Equal(HttpStatusCode.OK, (await waiter.PostAsJsonAsync("/api/v1/orders/1/request-account", new { accountRequested = true, version = 0 })).StatusCode);
        Assert.True(order.AccountRequested);
        f.LocalUser.Role.Name = "Cajero";
        var paid = await cashier.PostAsJsonAsync("/api/v1/payments", new { orderId = 1, userId = 999, amount = 25m, method = "Cash" });
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        Assert.Equal(PaymentStatus.Completed, payment!.Status); Assert.Equal(42, payment.UserId);
        Assert.Equal(OrderStatus.Paid, order.Status); Assert.Equal(TableStatus.Available, table.Status);
        var savedAtPayment = saves;
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsJsonAsync("/api/v1/payments", new { orderId = 1, amount = 25m, method = "Cash" })).StatusCode);
        f.LocalUser.Role.Name = "Mesero";
        Assert.Equal(HttpStatusCode.Conflict, (await waiter.PostAsJsonAsync("/api/v1/orders/1/items", new { productId = 3, quantity = 1 })).StatusCode);
        Assert.Equal(savedAtPayment, saves);
        Assert.Equal(6, transactionCount); // Create, serve and four payment attempts.
        Assert.False(inTransaction);
        // Real services and HTTP authorization; repositories/transactions are fakes, not a physical DB commit.
    }

    [Fact]
    public async Task Web_reservation_and_preorder_complete_reception_activation_kitchen_waiter_and_cashier_payment_flow()
    {
        var g = new SIGER.Tests.Application.GuestFixture();
        using var f = new ApiFactory();
        Payment? payment = null;
        f.Users.Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(f.LocalUser);
        g.F.Orders.Setup(x => x.GetWithDetailsAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(() => g.SavedOrder);
        g.F.Orders.Setup(x => x.GetKitchenOrdersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => g.SavedOrder is { Status: OrderStatus.Pending or OrderStatus.InPreparation or OrderStatus.Ready } o ? [o] : []);
        g.F.Orders.Setup(x => x.HasActiveOrdersAsync(4, It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        g.F.Payments.Setup(x => x.GetByOrderIdAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(() => payment);
        g.F.Payments.Setup(x => x.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((value, _) => { value.Id = 99; payment = value; }).Returns(Task.CompletedTask);
        g.F.Transaction<SIGER.Application.Base.Result<SIGER.Application.DTOs.Reservations.ReservationDto>>();

        f.ConfigureBackend = services =>
        {
            services.RemoveAll<IGuestReservationService>(); services.AddSingleton<IGuestReservationService>(g.Guests);
            services.RemoveAll<IPreOrderService>(); services.AddSingleton<IPreOrderService>(g.Preorders);
            services.RemoveAll<IReservationService>(); services.AddScoped<IReservationService, ReservationService>();
            services.RemoveAll<IOrderService>(); services.AddScoped<IOrderService, OrderService>();
            services.RemoveAll<IKitchenService>(); services.AddScoped<IKitchenService, KitchenService>();
            services.RemoveAll<IPaymentService>(); services.AddScoped<IPaymentService, PaymentService>();
            services.RemoveAll<IReservationRepository>(); services.AddSingleton(g.F.Reservations.Object);
            services.RemoveAll<IOrderRepository>(); services.AddSingleton(g.F.Orders.Object);
            services.RemoveAll<ITableRepository>(); services.AddSingleton(g.F.Tables.Object);
            services.RemoveAll<IProductRepository>(); services.AddSingleton(g.F.Products.Object);
            services.RemoveAll<IPaymentRepository>(); services.AddSingleton(g.F.Payments.Object);
            services.RemoveAll<IUnitOfWork>(); services.AddSingleton(g.F.Work.Object);
        };

        var jsonOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        // 1. Web guest creates reservation and preorder
        using var anonymous = f.Client();
        var createdResResponse = await anonymous.PostAsJsonAsync("/api/v1/public/reservations", g.Request);
        Assert.Equal(HttpStatusCode.Created, createdResResponse.StatusCode);
        var createdRes = (await createdResResponse.Content.ReadFromJsonAsync<SIGER.Application.DTOs.Reservations.GuestReservationCreatedDto>(jsonOptions))!;
        g.F.Reservations.Setup(x => x.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(g.SavedReservation);
        anonymous.DefaultRequestHeaders.Add("X-Reservation-Token", createdRes.AccessToken);

        var preResponse = await anonymous.PostAsJsonAsync("/api/v1/public/reservations/10/preorder", g.Items);
        Assert.Equal(HttpStatusCode.Created, preResponse.StatusCode);
        Assert.Equal(OrderStatus.PreOrdered, g.SavedOrder!.Status);
        Assert.Equal(TableStatus.Available, g.F.Table.Status);

        // 2. Anonymous (even with guest token) and unauthorized roles cannot access internal reception/activation endpoints
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/reservations/10/preorder")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/reservations/10/activate-preorder", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/reservations/10/receive", null)).StatusCode);

        foreach (var forbiddenRole in new[] { "Cliente", "Cocinero", "Cajero" })
        {
            using var forbidden = f.Client(forbiddenRole);
            Assert.Equal(HttpStatusCode.Forbidden, (await forbidden.GetAsync("/api/v1/reservations/10/preorder")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await forbidden.PostAsync("/api/v1/reservations/10/activate-preorder", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await forbidden.PostAsync("/api/v1/reservations/10/receive", null)).StatusCode);
        }

        // 3. Before activation, preorder does not enter kitchen queue and rejects kitchen/payment actions
        using var cook = f.Client("Cocinero");
        Assert.Equal("[]", await cook.GetStringAsync("/api/v1/kitchen/orders"));
        Assert.Equal(HttpStatusCode.Conflict, (await cook.PatchAsJsonAsync("/api/v1/kitchen/orders/20/in-preparation", new { })).StatusCode);

        // 4. Waiter locates reservation and inspects preorder without guest token; catalog price changes do not alter preorder total
        g.F.Product.Price = 88m;
        using var waiter = f.Client("Mesero");
        var internalLookup = await waiter.GetAsync("/api/v1/reservations/10/preorder");
        Assert.Equal(HttpStatusCode.OK, internalLookup.StatusCode);
        var lookedUpOrder = (await internalLookup.Content.ReadFromJsonAsync<OrderDto>(jsonOptions))!;
        Assert.Equal(20, lookedUpOrder.Id);
        Assert.Equal(OrderStatus.PreOrdered, lookedUpOrder.Status);
        Assert.Equal(25m, lookedUpOrder.Total);
        Assert.Equal(HttpStatusCode.Conflict, (await waiter.PostAsync("/api/v1/reservations/10/activate-preorder", null)).StatusCode);
        g.SavedReservation!.ReservationDateTime = DateTimeOffset.UtcNow;

        // 5. Waiter activates preorder atomically; duplicate activation is rejected with 409 Conflict
        var activateResponse = await waiter.PostAsync("/api/v1/reservations/10/activate-preorder", null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activatedOrder = (await activateResponse.Content.ReadFromJsonAsync<OrderDto>(jsonOptions))!;
        Assert.Equal(20, activatedOrder.Id);
        Assert.Equal(10, activatedOrder.ReservationId);
        Assert.Equal(42, activatedOrder.UserId);
        Assert.Equal(OrderStatus.Pending, activatedOrder.Status);
        Assert.Equal(25m, activatedOrder.Total);
        Assert.Equal(12.50m, Assert.Single(activatedOrder.Details).UnitPrice);
        Assert.Equal("Sin sal", activatedOrder.Details.Single().Note);
        Assert.Equal(TableStatus.Occupied, g.F.Table.Status);
        Assert.Equal(ReservationStatus.Completed, g.SavedReservation!.Status);

        Assert.Equal(HttpStatusCode.Conflict, (await waiter.PostAsync("/api/v1/reservations/10/activate-preorder", null)).StatusCode);

        // 6. Activated order now appears in Kitchen and advances Pending -> InPreparation -> Ready
        f.LocalUser.Role.Name = "Cocinero";
        var kitchenQueue = (await cook.GetFromJsonAsync<IReadOnlyList<OrderDto>>("/api/v1/kitchen/orders", jsonOptions))!;
        Assert.Equal(20, Assert.Single(kitchenQueue).Id);
        Assert.Equal(HttpStatusCode.OK, (await cook.PatchAsJsonAsync("/api/v1/kitchen/orders/20/in-preparation", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cook.PatchAsJsonAsync("/api/v1/kitchen/orders/20/ready", new { })).StatusCode);
        Assert.Equal(OrderStatus.Ready, g.SavedOrder.Status);

        // 7. Waiter marks Served and requests account
        f.LocalUser.Role.Name = "Mesero";
        Assert.Equal(HttpStatusCode.OK, (await waiter.PatchAsJsonAsync("/api/v1/orders/20/status", new { status = "Served", version = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await waiter.PostAsJsonAsync("/api/v1/orders/20/request-account", new { accountRequested = true, version = 0 })).StatusCode);

        // 8. Cashier pays the preserved $25.00 total and releases the table
        using var cashier = f.Client("Cajero");
        var paidResponse = await cashier.PostAsJsonAsync("/api/v1/payments", new { orderId = 20, userId = 999, amount = 25m, method = "Card" });
        Assert.Equal(HttpStatusCode.Created, paidResponse.StatusCode);
        Assert.Equal(OrderStatus.Paid, g.SavedOrder.Status);
        Assert.Equal(TableStatus.Available, g.F.Table.Status);
        Assert.Equal(25m, payment!.Amount);
    }
}
