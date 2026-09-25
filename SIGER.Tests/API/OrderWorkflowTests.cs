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
        f.LocalUser.Role.Name = "Mesero";
        Assert.Equal(HttpStatusCode.OK, (await waiter.PatchAsJsonAsync("/api/v1/orders/1/status", new { status = "Served", version = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await waiter.PostAsJsonAsync("/api/v1/orders/1/request-account", new { accountRequested = true, version = 0 })).StatusCode);
        Assert.True(order.AccountRequested);
        using var cashier = f.Client("Cajero");
        var paid = await cashier.PostAsJsonAsync("/api/v1/payments", new { orderId = 1, userId = 999, amount = 25m, method = "Cash" });
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        Assert.Equal(PaymentStatus.Completed, payment!.Status); Assert.Equal(42, payment.UserId);
        Assert.Equal(OrderStatus.Paid, order.Status); Assert.Equal(TableStatus.Available, table.Status);
        var savedAtPayment = saves;
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsJsonAsync("/api/v1/payments", new { orderId = 1, amount = 25m, method = "Cash" })).StatusCode);
        f.LocalUser.Role.Name = "Mesero";
        Assert.Equal(HttpStatusCode.Conflict, (await waiter.PostAsJsonAsync("/api/v1/orders/1/items", new { productId = 3, quantity = 1 })).StatusCode);
        Assert.Equal(savedAtPayment, saves);
        Assert.Equal(4, transactionCount); // Create, served transition, payment and rejected duplicate payment.
        Assert.False(inTransaction);
        // Real services and HTTP authorization; repositories/transactions are fakes, not a physical DB commit.
    }
}
