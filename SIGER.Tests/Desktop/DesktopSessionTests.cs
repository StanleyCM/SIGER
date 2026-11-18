using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SIGER.Application.DTOs.Audits;
using SIGER.Application.DTOs.Categories;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Payments;
using SIGER.Application.DTOs.Products;
using SIGER.Application.DTOs.Promotions;
using SIGER.Application.DTOs.Reports;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.DTOs.Roles;
using SIGER.Application.DTOs.Tables;
using SIGER.Application.DTOs.Users;
using SIGER.Desktop.Models;
using SIGER.Desktop.Services;
using SIGER.Domain.Enums;

namespace SIGER.Tests.Desktop;

public sealed class DesktopSessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T16:00:00Z");
    private const string SyntheticToken = "synthetic.desktop.token";

    [Fact]
    public async Task Login_uses_API_contract_and_authenticated_requests_send_Bearer()
    {
        using var fixture = new Fixture(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Null(request.Headers.Authorization);
                using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("demo@example.test", input.RootElement.GetProperty("email").GetString());
                Assert.Equal("synthetic password", input.RootElement.GetProperty("password").GetString());
                return LoginResponse();
            }

            Assert.Equal("/api/v1/tables", request.RequestUri.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(SyntheticToken, request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { "01" }) };
        });

        await fixture.Auth.SignInAsync(" demo@example.test ", "synthetic password", default);
        Assert.Equal(new SessionIdentity(42, "Ana Pérez", "Mesero", Now.AddMinutes(5)), fixture.Session.Current);
        Assert.Equal(["01"], await fixture.Api.SendAsync<string[]>(HttpMethod.Get, "api/v1/tables"));
    }

    [Fact]
    public async Task Login_401_is_a_credentials_error_and_ignores_provider_response_body()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("internal provider detail") }));
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Auth.SignInAsync("demo@example.test", "wrong", default));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Contains("contraseña", error.Message);
        Assert.DoesNotContain("internal provider detail", error.Message);
        Assert.Null(fixture.Session.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_API_and_timeout_have_safe_recoverable_errors(bool timeout)
    {
        using var fixture = new Fixture((_, _) => throw (timeout
            ? new OperationCanceledException("internal timeout")
            : new HttpRequestException("internal network error")));
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Auth.SignInAsync("demo@example.test", "unused", default));
        Assert.Null(error.StatusCode);
        Assert.Contains(timeout ? "tardó demasiado" : "No se pudo conectar", error.Message);
        Assert.DoesNotContain("internal", error.Message);
        Assert.Null(fixture.Session.Current);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Protected_HTTP_errors_keep_the_session_only_when_access_is_denied(HttpStatusCode status)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        fixture.StartSession();
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Api.SendAsync(HttpMethod.Get, "api/v1/users"));
        Assert.Equal(status, error.StatusCode);
        if (status == HttpStatusCode.Forbidden)
        {
            Assert.Contains("Acceso denegado", error.Message);
            Assert.NotNull(fixture.Session.Current);
        }
        else Assert.Null(fixture.Session.Current);
    }

    [Fact]
    public async Task Expiration_clears_the_token_and_stops_protected_requests_locally()
    {
        using var fixture = new Fixture((_, _) => throw new InvalidOperationException("No request expected"));
        fixture.StartSession();
        fixture.Clock.UtcNow = Now.AddMinutes(5);
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Api.SendAsync(HttpMethod.Get, "api/v1/tables"));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Null(fixture.Session.Current);
        Assert.Null(fixture.Session.GetCredential());
    }

    [Fact]
    public async Task Sign_out_clears_the_session_and_a_late_401_cannot_close_a_new_session()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture((_, _) => response.Task);
        fixture.StartSession();
        var oldRequest = fixture.Api.SendAsync(HttpMethod.Get, "api/v1/tables");
        fixture.Session.SignOut();
        Assert.Null(fixture.Session.GetCredential());
        fixture.StartSession();
        response.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiException>(() => oldRequest);
        Assert.NotNull(fixture.Session.Current);
    }

    [Fact]
    public async Task Customer_role_cannot_start_a_desktop_session()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(LoginResponse("Cliente")));
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Auth.SignInAsync("demo@example.test", "unused", default));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Null(fixture.Session.Current);
        Assert.Null(RoleNavigation.For("Cocina")); // SIGER.API returns Cocinero.
    }

    [Theory]
    [InlineData("Administrador", "Administrador", "Dashboard|Usuarios|Categorías|Productos|Mesas|Reservaciones|Órdenes|Pagos|Promociones|Reportes|Auditoría")]
    [InlineData("Mesero", "Mesero", "Mesas|Órdenes|Reservaciones")]
    [InlineData("Cocinero", "Cocina", "Órdenes de cocina")]
    [InlineData("Cajero", "Cajero", "Cobros|Pagos")]
    public void Role_menu_contains_only_its_allowed_modules(string role, string label, string titles)
    {
        var profile = Assert.IsType<RoleNavigation>(RoleNavigation.For(role));
        Assert.Equal(label, profile.RoleLabel);
        Assert.Equal(titles.Split('|'), profile.Modules.Select(module => module.Title));
        Assert.True(profile.Allows(profile.Modules[0]));
        var otherProfile = RoleNavigation.For(role == "Cocinero" ? "Administrador" : "Cocinero")!;
        Assert.False(profile.Allows(otherProfile.Modules[0]));
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_credentials_cannot_be_sent_to_another_origin()
    {
        using var fixture = new Fixture((_, _) => throw new InvalidOperationException("No request expected"));
        fixture.StartSession();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Api.SendAsync(HttpMethod.Get, "api/v1/tables", cancellationToken: cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Api.SendAsync(HttpMethod.Get, "https://other.example.test/api/v1/tables"));
        Assert.Throws<ApiException>(() => ApiClient.ReadBaseAddress("http://other.example.test"));
    }

    [Fact]
    public async Task ApiClient_serializes_and_deserializes_string_enums_and_rejects_integer_enums()
    {
        var call = 0;
        using var fixture = new Fixture(async (request, token) =>
        {
            call++;
            if (call == 1)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("Served", payload.RootElement.GetProperty("status").GetString());
                Assert.Equal(3L, payload.RootElement.GetProperty("version").GetInt64());
                return JsonStringResponse("""
                    {"id":7,"number":4,"capacity":4,"status":"Occupied","location":"Terraza","version":2}
                    """);
            }

            return JsonStringResponse("""
                {"id":7,"number":4,"capacity":4,"status":1,"location":"Terraza","version":2}
                """);
        });
        fixture.StartSession();

        var table = await fixture.Api.SendAsync<TableDto>(
            HttpMethod.Patch,
            "api/v1/tables/7/status",
            new UpdateOrderStatusRequestDto { Status = OrderStatus.Served, Version = 3 });
        Assert.Equal(TableStatus.Occupied, table.Status);
        Assert.Equal("Ocupada", TableApiService.FormatStatus(table.Status));

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Api.SendAsync<TableDto>(HttpMethod.Get, "api/v1/tables/7"));
        Assert.Contains("respuesta inesperada", error.Message);
    }

    [Fact]
    public async Task Waiter_services_list_tables_load_available_products_and_create_table_order()
    {
        using var fixture = new Fixture(async (request, token) =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            switch (pathAndQuery)
            {
                case "/api/v1/tables?pageNumber=1&pageSize=100":
                    return JsonStringResponse("""
                        {"items":[{"id":4,"number":4,"capacity":4,"status":"Available","location":"Salón","version":0}],"totalCount":1,"pageNumber":1,"pageSize":100,"totalPages":1}
                        """);
                case "/api/v1/tables/available":
                    return JsonStringResponse("""
                        [{"id":4,"number":4,"capacity":4,"status":"Available","location":"Salón","version":0}]
                        """);
                case "/api/v1/tables/4":
                    return JsonStringResponse("""
                        {"id":4,"number":4,"capacity":4,"status":"Occupied","location":"Salón","version":1}
                        """);
                case "/api/v1/products/available":
                    return JsonStringResponse("""
                        [{"id":11,"categoryId":2,"categoryName":"Entradas","name":"Sopa del día","price":12.50,"isAvailable":true,"version":0}]
                        """);
                case "/api/v1/orders":
                    Assert.Equal(HttpMethod.Post, request.Method);
                    using (var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)))
                    {
                        var root = body.RootElement;
                        Assert.Equal(4L, root.GetProperty("tableId").GetInt64());
                        Assert.Equal("Desktop", root.GetProperty("origin").GetString());
                        Assert.Equal("Table", root.GetProperty("type").GetString());
                        Assert.Equal("Sin cebolla", root.GetProperty("notes").GetString());
                        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
                        Assert.Equal(11L, item.GetProperty("productId").GetInt64());
                        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
                        Assert.Equal("Caliente", item.GetProperty("note").GetString());
                    }
                    return JsonStringResponse("""
                        {
                          "id":101,"tableId":4,"tableNumber":4,"userId":42,"status":"Pending","origin":"Desktop","type":"Table",
                          "total":25.00,"notes":"Sin cebolla","accountRequested":false,"version":0,
                          "details":[{"id":501,"productId":11,"productName":"Sopa del día","quantity":2,"unitPrice":12.50,"subtotal":25.00,"note":"Caliente"}]
                        }
                        """, HttpStatusCode.Created);
                default:
                    throw new InvalidOperationException("Unexpected route: " + pathAndQuery);
            }
        });
        fixture.StartSession();

        var pagedTables = await fixture.Tables.GetPagedAsync();
        Assert.Single(pagedTables.Items);
        Assert.Equal(1, pagedTables.TotalCount);
        Assert.Equal(1, pagedTables.PageNumber);
        Assert.Equal(100, pagedTables.PageSize);
        Assert.Equal(1, pagedTables.TotalPages);
        var availableTables = await fixture.Tables.GetAvailableAsync();
        Assert.Equal(TableStatus.Available, Assert.Single(availableTables).Status);

        var catalog = await fixture.Products.GetAvailableAsync();
        var product = Assert.Single(catalog);
        Assert.Equal(12.50m, product.Price);

        var created = await fixture.Orders.CreateTableOrderAsync(
            4,
            [new CreateOrderDetailRequestDto { ProductId = product.Id, Quantity = 2, Note = " Caliente " }],
            " Sin cebolla ");
        Assert.Equal(101L, created.Id);
        Assert.Equal(OrderStatus.Pending, created.Status);
        Assert.Equal(25.00m, created.Total);
        var detail = Assert.Single(created.Details);
        Assert.Equal(501L, detail.Id);
        Assert.Equal(11L, detail.ProductId);

        var tableAfterOrder = await fixture.Tables.GetByIdAsync(4);
        Assert.Equal(TableStatus.Occupied, tableAfterOrder.Status);
    }

    [Fact]
    public async Task Waiter_order_workflow_supports_filtering_line_mutations_status_transitions_account_request_and_conflicts()
    {
        var simulateConflict = false;
        using var fixture = new Fixture(async (request, token) =>
        {
            if (simulateConflict)
                return new HttpResponseMessage(HttpStatusCode.Conflict);

            var pathAndQuery = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && pathAndQuery.StartsWith("/api/v1/orders?", StringComparison.Ordinal))
            {
                Assert.Contains("tableId=4", pathAndQuery);
                Assert.Contains("status=Ready", pathAndQuery);
                Assert.Contains("orderId=101", pathAndQuery);
                return JsonStringResponse("""
                    {
                      "items":[{
                        "id":101,"tableId":4,"tableNumber":4,"status":"Ready","origin":"Desktop","type":"Table",
                        "total":25.00,"accountRequested":false,"version":2,
                        "details":[{"id":501,"productId":11,"productName":"Sopa","quantity":2,"unitPrice":12.50,"subtotal":25.00}]
                      }],
                      "totalCount":1,"pageNumber":1,"pageSize":20,"totalPages":1
                    }
                    """);
            }
            if (request.Method == HttpMethod.Get && pathAndQuery == "/api/v1/orders/101")
            {
                return JsonStringResponse("""
                    {
                      "id":101,"tableId":4,"tableNumber":4,"status":"Ready","origin":"Desktop","type":"Table",
                      "total":25.00,"accountRequested":false,"version":2,
                      "details":[{"id":501,"productId":11,"productName":"Sopa","quantity":2,"unitPrice":12.50,"subtotal":25.00}]
                    }
                    """);
            }
            if (request.Method == HttpMethod.Post && pathAndQuery == "/api/v1/orders/101/items")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal(15L, doc.RootElement.GetProperty("productId").GetInt64());
                Assert.Equal(1, doc.RootElement.GetProperty("quantity").GetInt32());
                return JsonStringResponse("""
                    {
                      "id":101,"tableId":4,"tableNumber":4,"status":"Ready","origin":"Desktop","type":"Table",
                      "total":33.00,"accountRequested":false,"version":3,
                      "details":[
                        {"id":501,"productId":11,"productName":"Sopa","quantity":2,"unitPrice":12.50,"subtotal":25.00},
                        {"id":502,"productId":15,"productName":"Café","quantity":1,"unitPrice":8.00,"subtotal":8.00,"note":"Sin azúcar"}
                      ]
                    }
                    """);
            }
            if (request.Method == HttpMethod.Put && pathAndQuery == "/api/v1/orders/101/items/502")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal(502L, doc.RootElement.GetProperty("orderDetailId").GetInt64());
                Assert.Equal(3, doc.RootElement.GetProperty("quantity").GetInt32());
                return JsonStringResponse("""
                    {
                      "id":101,"tableId":4,"tableNumber":4,"status":"Ready","origin":"Desktop","type":"Table",
                      "total":49.00,"accountRequested":false,"version":4,
                      "details":[
                        {"id":501,"productId":11,"productName":"Sopa","quantity":2,"unitPrice":12.50,"subtotal":25.00},
                        {"id":502,"productId":15,"productName":"Café","quantity":3,"unitPrice":8.00,"subtotal":24.00,"note":"Doble"}
                      ]
                    }
                    """);
            }
            if (request.Method == HttpMethod.Delete && pathAndQuery == "/api/v1/orders/101/items/501")
            {
                return JsonStringResponse("""
                    {
                      "id":101,"tableId":4,"tableNumber":4,"status":"Ready","origin":"Desktop","type":"Table",
                      "total":24.00,"accountRequested":false,"version":5,
                      "details":[
                        {"id":502,"productId":15,"productName":"Café","quantity":3,"unitPrice":8.00,"subtotal":24.00,"note":"Doble"}
                      ]
                    }
                    """);
            }
            if (request.Method == HttpMethod.Patch && pathAndQuery == "/api/v1/orders/101/status")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("Served", doc.RootElement.GetProperty("status").GetString());
                Assert.Equal(5L, doc.RootElement.GetProperty("version").GetInt64());
                return JsonStringResponse("""
                    {
                      "id":101,"tableId":4,"tableNumber":4,"status":"Served","origin":"Desktop","type":"Table",
                      "total":24.00,"accountRequested":false,"version":6,
                      "details":[{"id":502,"productId":15,"productName":"Café","quantity":3,"unitPrice":8.00,"subtotal":24.00}]
                    }
                    """);
            }
            if (request.Method == HttpMethod.Post && pathAndQuery == "/api/v1/orders/101/request-account")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.True(doc.RootElement.GetProperty("accountRequested").GetBoolean());
                Assert.Equal(6L, doc.RootElement.GetProperty("version").GetInt64());
                return JsonStringResponse("""
                    {
                      "id":101,"tableId":4,"tableNumber":4,"status":"Served","origin":"Desktop","type":"Table",
                      "total":24.00,"accountRequested":true,"version":7,
                      "details":[{"id":502,"productId":15,"productName":"Café","quantity":3,"unitPrice":8.00,"subtotal":24.00}]
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected {request.Method} {pathAndQuery}");
        });
        fixture.StartSession();

        var page = await fixture.Orders.GetPagedAsync(new OrderQueryFilter(1, 20, TableId: 4, Status: OrderStatus.Ready, OrderId: 101));
        Assert.Equal(101L, Assert.Single(page.Items).Id);

        var fetched = await fixture.Orders.GetByIdAsync(101);
        Assert.True(OrderApiService.CanMarkServed(fetched.Status));
        Assert.True(OrderApiService.CanEditItems(fetched.Status));

        var afterAdd = await fixture.Orders.AddItemAsync(101, productId: 15, quantity: 1, note: " Sin azúcar ");
        Assert.Equal(33.00m, afterAdd.Total);
        Assert.Equal(2, afterAdd.Details.Count);

        var afterUpdate = await fixture.Orders.UpdateItemAsync(101, orderDetailId: 502, quantity: 3, note: " Doble ");
        Assert.Equal(49.00m, afterUpdate.Total);

        var afterRemove = await fixture.Orders.RemoveItemAsync(101, orderDetailId: 501);
        Assert.Equal(24.00m, afterRemove.Total);
        Assert.Equal(502L, Assert.Single(afterRemove.Details).Id);

        var afterServed = await fixture.Orders.ChangeStatusAsync(101, OrderStatus.Served, version: afterRemove.Version);
        Assert.Equal(OrderStatus.Served, afterServed.Status);
        Assert.False(OrderApiService.CanMarkServed(afterServed.Status));
        Assert.True(OrderApiService.CanRequestAccount(afterServed));

        var afterAccount = await fixture.Orders.RequestAccountAsync(101, version: afterServed.Version);
        Assert.True(afterAccount.AccountRequested);
        Assert.False(OrderApiService.CanRequestAccount(afterAccount));

        simulateConflict = true;
        var conflict = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Orders.ChangeStatusAsync(101, OrderStatus.Cancelled, version: afterAccount.Version));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("Actualiza los datos", conflict.Message);
    }

    [Fact]
    public async Task Waiter_order_rules_enforce_allowed_transitions_and_local_input_validation()
    {
        using var fixture = new Fixture((_, _) => throw new InvalidOperationException("No network call expected"));
        fixture.StartSession();

        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Orders.ChangeStatusAsync(10, OrderStatus.Paid, version: 1));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Orders.ChangeStatusAsync(10, OrderStatus.InPreparation, version: 1));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Orders.CreateTableOrderAsync(4, []));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Orders.GetPagedAsync(new OrderQueryFilter(StartDate: Now, EndDate: Now.AddHours(-1))));

        Assert.False(OrderApiService.CanEditItems(OrderStatus.Paid));
        Assert.False(OrderApiService.CanEditItems(OrderStatus.Cancelled));
        Assert.False(OrderApiService.CanEditItems(OrderStatus.PreOrdered));
        Assert.False(OrderApiService.CanMarkServed(OrderStatus.Pending));
        Assert.True(OrderApiService.CanMarkServed(OrderStatus.Ready));
    }

    [Fact]
    public async Task Kitchen_queue_loads_orders_and_details_excludes_preorders_and_completes_preparation_transitions()
    {
        using var fixture = new Fixture((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path == "/api/v1/kitchen/orders")
            {
                return Task.FromResult(JsonStringResponse("""
                    [
                      {
                        "id":201,"tableId":4,"tableNumber":4,"orderDateTime":"2026-11-17T15:30:00Z",
                        "status":"Pending","origin":"Desktop","type":"Table","total":37.50,"notes":"Alergia a nueces","version":0,
                        "details":[
                          {"id":701,"productId":11,"productName":"Sopa del día","quantity":2,"unitPrice":12.50,"subtotal":25.00,"note":"Bien caliente"},
                          {"id":702,"productId":14,"productName":"Ensalada fresca","quantity":1,"unitPrice":12.50,"subtotal":12.50,"note":"Aderezo aparte"}
                        ]
                      },
                      {
                        "id":202,"tableId":5,"tableNumber":5,"orderDateTime":"2026-11-17T15:35:00Z",
                        "status":"PreOrdered","origin":"Web","type":"Table","total":20.00,"version":0,
                        "details":[{"id":703,"productId":12,"productName":"Postre","quantity":1,"unitPrice":20.00,"subtotal":20.00}]
                      }
                    ]
                    """));
            }
            if (request.Method == HttpMethod.Patch && path == "/api/v1/kitchen/orders/201/in-preparation")
            {
                return Task.FromResult(JsonStringResponse("""
                    {
                      "id":201,"tableId":4,"tableNumber":4,"orderDateTime":"2026-11-17T15:30:00Z",
                      "status":"InPreparation","origin":"Desktop","type":"Table","total":37.50,"notes":"Alergia a nueces","version":1,
                      "details":[
                        {"id":701,"productId":11,"productName":"Sopa del día","quantity":2,"unitPrice":12.50,"subtotal":25.00,"note":"Bien caliente"},
                        {"id":702,"productId":14,"productName":"Ensalada fresca","quantity":1,"unitPrice":12.50,"subtotal":12.50,"note":"Aderezo aparte"}
                      ]
                    }
                    """));
            }
            if (request.Method == HttpMethod.Patch && path == "/api/v1/kitchen/orders/201/ready")
            {
                return Task.FromResult(JsonStringResponse("""
                    {
                      "id":201,"tableId":4,"tableNumber":4,"orderDateTime":"2026-11-17T15:30:00Z",
                      "status":"Ready","origin":"Desktop","type":"Table","total":37.50,"notes":"Alergia a nueces","version":2,
                      "details":[
                        {"id":701,"productId":11,"productName":"Sopa del día","quantity":2,"unitPrice":12.50,"subtotal":25.00,"note":"Bien caliente"},
                        {"id":702,"productId":14,"productName":"Ensalada fresca","quantity":1,"unitPrice":12.50,"subtotal":12.50,"note":"Aderezo aparte"}
                      ]
                    }
                    """));
            }

            throw new InvalidOperationException($"Unexpected {request.Method} {path}");
        });
        fixture.StartSession();

        var queue = await fixture.Kitchen.GetOrdersAsync();
        var pendingOrder = Assert.Single(queue);
        Assert.Equal(201L, pendingOrder.Id);
        Assert.Equal(4, pendingOrder.TableNumber);
        Assert.Equal(OrderStatus.Pending, pendingOrder.Status);
        Assert.Equal("Alergia a nueces", pendingOrder.Notes);
        Assert.Equal(2, pendingOrder.Details.Count);
        Assert.Contains(pendingOrder.Details, d => d.ProductName == "Sopa del día" && d.Quantity == 2 && d.Note == "Bien caliente");
        Assert.True(KitchenApiService.CanStartPreparation(pendingOrder.Status));
        Assert.False(KitchenApiService.CanMarkReady(pendingOrder.Status));

        var inPrep = await fixture.Kitchen.MarkInPreparationAsync(pendingOrder.Id, pendingOrder.Status);
        Assert.Equal(OrderStatus.InPreparation, inPrep.Status);
        Assert.False(KitchenApiService.CanStartPreparation(inPrep.Status));
        Assert.True(KitchenApiService.CanMarkReady(inPrep.Status));

        var ready = await fixture.Kitchen.MarkReadyAsync(inPrep.Id, inPrep.Status);
        Assert.Equal(OrderStatus.Ready, ready.Status);
        Assert.False(KitchenApiService.CanStartPreparation(ready.Status));
        Assert.False(KitchenApiService.CanMarkReady(ready.Status));
    }

    [Fact]
    public async Task Kitchen_rules_reject_invalid_transitions_and_handle_401_403_409_and_network_recovery()
    {
        var step = "conflict";
        using var fixture = new Fixture((request, _) =>
        {
            return step switch
            {
                "conflict" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)),
                "forbidden" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)),
                "unauthorized" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
                "network" => throw new HttpRequestException("simulated network failure"),
                "recover" when request.Method == HttpMethod.Get && request.RequestUri!.PathAndQuery == "/api/v1/kitchen/orders" =>
                    Task.FromResult(JsonStringResponse("""
                        [{"id":201,"tableId":4,"tableNumber":4,"status":"InPreparation","origin":"Desktop","type":"Table","total":25.00,"version":1,"details":[]}]
                        """)),
                _ => throw new InvalidOperationException("Unexpected step: " + step)
            };
        });
        fixture.StartSession();

        // Local transition guards prevent skipping, repeating, backward transitions, or operating on PreOrdered/Served/Paid/Cancelled.
        Assert.False(KitchenApiService.IsKitchenQueueStatus(OrderStatus.PreOrdered));
        Assert.False(KitchenApiService.IsKitchenQueueStatus(OrderStatus.Served));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Kitchen.MarkInPreparationAsync(201, OrderStatus.InPreparation));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Kitchen.MarkInPreparationAsync(201, OrderStatus.Ready));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Kitchen.MarkInPreparationAsync(201, OrderStatus.PreOrdered));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Kitchen.MarkReadyAsync(201, OrderStatus.Pending));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Kitchen.MarkReadyAsync(201, OrderStatus.Ready));

        // 409 Conflict
        var conflict = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Kitchen.MarkInPreparationAsync(201, OrderStatus.Pending));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        // 403 Forbidden keeps session active
        step = "forbidden";
        var forbidden = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Kitchen.GetOrdersAsync());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains("Acceso denegado", forbidden.Message);
        Assert.NotNull(fixture.Session.Current);

        // Network error leaves status uncertain (StatusCode is null), then consulting queue recovers actual server state
        step = "network";
        var networkError = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Kitchen.MarkInPreparationAsync(201, OrderStatus.Pending));
        Assert.Null(networkError.StatusCode);

        step = "recover";
        var recovered = Assert.Single(await fixture.Kitchen.GetOrdersAsync());
        Assert.Equal(OrderStatus.InPreparation, recovered.Status);

        // 401 Unauthorized invalidates session
        step = "unauthorized";
        var unauthorized = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Kitchen.GetOrdersAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Null(fixture.Session.Current);
    }

    [Fact]
    public async Task Cashier_queries_orders_verifies_existing_payments_and_processes_valid_payment()
    {
        var paymentCreated = false;
        using var fixture = new Fixture(async (request, token) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path == "/api/v1/orders?pageNumber=1&pageSize=20&status=Served")
            {
                return JsonStringResponse("""
                    {
                      "items":[{
                        "id":301,"tableId":4,"tableNumber":4,"status":"Served","origin":"Desktop","type":"Table",
                        "total":45.00,"accountRequested":true,"version":3,
                        "details":[{"id":801,"productId":11,"productName":"Filete","quantity":1,"unitPrice":45.00,"subtotal":45.00}]
                      }],
                      "totalCount":1,"pageNumber":1,"pageSize":20,"totalPages":1
                    }
                    """);
            }
            if (request.Method == HttpMethod.Get && path == "/api/v1/orders/301")
            {
                var status = paymentCreated ? "Paid" : "Served";
                return JsonStringResponse($$"""
                    {
                      "id":301,"tableId":4,"tableNumber":4,"status":"{{status}}","origin":"Desktop","type":"Table",
                      "total":45.00,"accountRequested":true,"version":{{(paymentCreated ? 4 : 3)}},
                      "details":[{"id":801,"productId":11,"productName":"Filete","quantity":1,"unitPrice":45.00,"subtotal":45.00}]
                    }
                    """);
            }
            if (request.Method == HttpMethod.Get && path == "/api/v1/payments/order/301")
            {
                if (!paymentCreated)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                return JsonStringResponse("""
                    {
                      "id":901,"orderId":301,"userId":42,"amount":45.00,"method":"Card",
                      "status":"Completed","reference":"VOUCHER-7788","paymentDate":"2026-11-17T16:10:00Z","updatedAt":"2026-11-17T16:10:00Z"
                    }
                    """);
            }
            if (request.Method == HttpMethod.Post && path == "/api/v1/payments")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var root = doc.RootElement;
                Assert.Equal(301L, root.GetProperty("orderId").GetInt64());
                Assert.Equal(45.00m, root.GetProperty("amount").GetDecimal());
                Assert.Equal("Card", root.GetProperty("method").GetString());
                Assert.Equal("VOUCHER-7788", root.GetProperty("reference").GetString());
                paymentCreated = true;
                return JsonStringResponse("""
                    {
                      "id":901,"orderId":301,"userId":42,"amount":45.00,"method":"Card",
                      "status":"Completed","reference":"VOUCHER-7788","paymentDate":"2026-11-17T16:10:00Z","updatedAt":"2026-11-17T16:10:00Z"
                    }
                    """, HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected {request.Method} {path}");
        });
        fixture.StartSession();

        var page = await fixture.Orders.GetPagedAsync(new OrderQueryFilter(1, 20, Status: OrderStatus.Served));
        Assert.Equal(301L, Assert.Single(page.Items).Id);

        var order = await fixture.Orders.GetByIdAsync(301);
        var beforePayment = await fixture.Payments.TryGetByOrderIdAsync(301);
        Assert.Null(beforePayment);
        Assert.True(PaymentApiService.CanProcessPayment(order, beforePayment));

        var created = await fixture.Payments.ProcessPaymentAsync(
            order.Id,
            order.Total,
            PaymentMethod.Card,
            " VOUCHER-7788 ",
            order);
        Assert.Equal(901L, created.Id);
        Assert.Equal(45.00m, created.Amount);
        Assert.Equal(PaymentMethod.Card, created.Method);
        Assert.Equal(PaymentStatus.Completed, created.Status);
        Assert.Equal("Tarjeta", PaymentApiService.FormatMethod(created.Method));
        Assert.Equal("Completado", PaymentApiService.FormatStatus(created.Status));

        var existing = await fixture.Payments.GetByOrderIdAsync(301);
        Assert.Equal(901L, existing.Id);
        var refreshedOrder = await fixture.Orders.GetByIdAsync(301);
        Assert.Equal(OrderStatus.Paid, refreshedOrder.Status);
        Assert.False(PaymentApiService.CanProcessPayment(refreshedOrder, existing));
    }

    [Fact]
    public async Task Cashier_payment_validations_reject_invalid_inputs_and_handle_401_403_409_and_uncertain_timeout_reconciliation()
    {
        var step = "duplicate";
        using var fixture = new Fixture((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            return step switch
            {
                "duplicate" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { detail = "A payment already exists for this order." })
                }),
                "account-changed" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { detail = "Request the account before paying a table order." })
                }),
                "forbidden" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)),
                "timeout" => throw new OperationCanceledException("simulated timeout"),
                "reconcile" when request.Method == HttpMethod.Get && path == "/api/v1/payments/order/301" =>
                    Task.FromResult(JsonStringResponse("""
                        {"id":902,"orderId":301,"userId":42,"amount":45.00,"method":"Cash","status":"Completed","paymentDate":"2026-11-17T16:15:00Z","updatedAt":"2026-11-17T16:15:00Z"}
                        """)),
                "unauthorized" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
                _ => throw new InvalidOperationException("Unexpected step: " + step)
            };
        });
        fixture.StartSession();

        var payableOrder = new OrderDto { Id = 301, Status = OrderStatus.Served, AccountRequested = true, Total = 45.00m };
        var paidOrder = new OrderDto { Id = 301, Status = OrderStatus.Paid, Total = 45.00m };
        var preOrder = new OrderDto { Id = 302, Status = OrderStatus.PreOrdered, Total = 45.00m };

        // Local validations: wrong amount, invalid method, already paid or preordered
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.ProcessPaymentAsync(301, 40.00m, PaymentMethod.Cash, null, payableOrder));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.ProcessPaymentAsync(301, 0m, PaymentMethod.Cash, null, payableOrder));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.ProcessPaymentAsync(301, 45.00m, (PaymentMethod)99, null, payableOrder));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.ProcessPaymentAsync(301, 45.00m, PaymentMethod.Cash, null, paidOrder));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.ProcessPaymentAsync(302, 45.00m, PaymentMethod.Cash, null, preOrder));

        // 409 Conflict (duplicate payment or already paid on server)
        var duplicate = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.ProcessPaymentAsync(301, 45.00m, PaymentMethod.Cash, null, payableOrder));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("ya tiene un pago", duplicate.Message);
        step = "account-changed";
        var accountError = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.ProcessPaymentAsync(301, 45.00m, PaymentMethod.Cash, null, payableOrder));
        Assert.Equal(HttpStatusCode.Conflict, accountError.StatusCode);
        Assert.Contains("solicitar la cuenta", accountError.Message);

        // 403 Forbidden preserves session
        step = "forbidden";
        var forbidden = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.ProcessPaymentAsync(301, 45.00m, PaymentMethod.Cash, null, payableOrder));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.NotNull(fixture.Session.Current);

        // Timeout during POST /payments results in uncertain state (StatusCode is null), reconciled via GET /payments/order/{orderId}
        step = "timeout";
        var timeoutError = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.ProcessPaymentAsync(301, 45.00m, PaymentMethod.Cash, null, payableOrder));
        Assert.Null(timeoutError.StatusCode);
        Assert.Contains("tardó demasiado", timeoutError.Message);

        step = "reconcile";
        var reconciled = await fixture.Payments.TryGetByOrderIdAsync(301);
        Assert.NotNull(reconciled);
        Assert.Equal(902L, reconciled!.Id);
        Assert.Equal(PaymentStatus.Completed, reconciled.Status);
        Assert.False(PaymentApiService.CanProcessPayment(payableOrder, reconciled));

        // 401 Unauthorized invalidates session
        step = "unauthorized";
        var unauthorized = await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Payments.TryGetByOrderIdAsync(301));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Null(fixture.Session.Current);
    }

    [Fact]
    public async Task Admin_catalog_and_staff_services_manage_users_roles_categories_products_and_tables()
    {
        using var fixture = new Fixture(async (request, token) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            var method = request.Method;

            if (method == HttpMethod.Get && path == "/api/v1/users/roles")
                return JsonStringResponse("""[{"id":1,"name":"Administrador","isActive":true},{"id":2,"name":"Mesero","isActive":true}]""");
            if (method == HttpMethod.Get && path == "/api/v1/users?pageNumber=1&pageSize=20&search=ana&isActive=true")
                return JsonStringResponse("""{"items":[{"id":10,"roleId":2,"roleName":"Mesero","firstName":"Ana","lastName":"Pérez","email":"ana@example.test","isActive":true}],"totalCount":1,"pageNumber":1,"pageSize":20,"totalPages":1}""");
            if (method == HttpMethod.Post && path == "/api/v1/users")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal(2L, doc.RootElement.GetProperty("roleId").GetInt64());
                Assert.Equal("luis@example.test", doc.RootElement.GetProperty("email").GetString());
                return JsonStringResponse("""{"id":11,"roleId":2,"roleName":"Mesero","firstName":"Luis","lastName":"Gómez","email":"luis@example.test","isActive":true}""", HttpStatusCode.Created);
            }
            if (method == HttpMethod.Put && path == "/api/v1/users/11")
                return JsonStringResponse("""{"id":11,"roleId":1,"roleName":"Administrador","firstName":"Luis","lastName":"Gómez","email":"luis@example.test","isActive":true}""");
            if (method == HttpMethod.Patch && path == "/api/v1/users/11/status")
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            if (method == HttpMethod.Get && path == "/api/v1/categories?pageNumber=1&pageSize=50")
                return JsonStringResponse("""{"items":[{"id":3,"name":"Postres","isActive":true}],"totalCount":1,"pageNumber":1,"pageSize":50,"totalPages":1}""");
            if (method == HttpMethod.Post && path == "/api/v1/categories")
                return JsonStringResponse("""{"id":4,"name":"Bebidas","description":"Frías","isActive":true}""", HttpStatusCode.Created);
            if (method == HttpMethod.Put && path == "/api/v1/categories/4")
                return JsonStringResponse("""{"id":4,"name":"Bebidas Naturales","description":"Frías","isActive":true}""");
            if (method == HttpMethod.Patch && path == "/api/v1/categories/4/status")
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            if (method == HttpMethod.Post && path == "/api/v1/products")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal(18.75m, doc.RootElement.GetProperty("price").GetDecimal());
                return JsonStringResponse("""{"id":25,"categoryId":4,"categoryName":"Bebidas","name":"Limonada","price":18.75,"isAvailable":true,"version":0}""", HttpStatusCode.Created);
            }
            if (method == HttpMethod.Put && path == "/api/v1/products/25")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal(0L, doc.RootElement.GetProperty("version").GetInt64());
                return JsonStringResponse("""{"id":25,"categoryId":4,"categoryName":"Bebidas","name":"Limonada Menta","price":19.50,"isAvailable":true,"version":1}""");
            }
            if (method == HttpMethod.Patch && path == "/api/v1/products/25/availability")
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            if (method == HttpMethod.Post && path == "/api/v1/tables")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("Available", doc.RootElement.GetProperty("status").GetString());
                return JsonStringResponse("""{"id":8,"number":8,"capacity":6,"status":"Available","location":"Terraza","version":0}""", HttpStatusCode.Created);
            }
            if (method == HttpMethod.Put && path == "/api/v1/tables/8")
                return JsonStringResponse("""{"id":8,"number":8,"capacity":8,"status":"Available","location":"VIP","version":1}""");
            if (method == HttpMethod.Patch && path == "/api/v1/tables/8/status")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("OutOfService", doc.RootElement.GetProperty("status").GetString());
                Assert.Equal(1L, doc.RootElement.GetProperty("version").GetInt64());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected {method} {path}");
        });
        fixture.StartSession();

        var roles = await fixture.Users.GetRolesAsync();
        Assert.Equal(2, roles.Count);
        var usersPage = await fixture.Users.GetPagedAsync(1, 20, "ana", true);
        Assert.Single(usersPage.Items);
        var createdUser = await fixture.Users.CreateAsync(new CreateUserRequestDto
        {
            RoleId = 2, FirstName = "Luis", LastName = "Gómez", Email = "luis@example.test", Password = "SyntheticPassword123!"
        });
        Assert.Equal(11L, createdUser.Id);
        var updatedUser = await fixture.Users.UpdateAsync(11, new UpdateUserRequestDto
        {
            RoleId = 1, FirstName = "Luis", LastName = "Gómez", Email = "luis@example.test"
        });
        Assert.Equal("Administrador", updatedUser.RoleName);
        await fixture.Users.UpdateStatusAsync(11, false);

        var categories = await fixture.Categories.GetPagedAsync();
        Assert.Single(categories.Items);
        var createdCategory = await fixture.Categories.CreateAsync(" Bebidas ", " Frías ");
        var updatedCategory = await fixture.Categories.UpdateAsync(createdCategory.Id, "Bebidas Naturales", "Frías");
        Assert.Equal("Bebidas Naturales", updatedCategory.Name);
        await fixture.Categories.SetActiveAsync(createdCategory.Id, false);

        var createdProduct = await fixture.Products.CreateAsync(new CreateProductRequestDto
        {
            CategoryId = 4, Name = "Limonada", Price = 18.75m, IsAvailable = true
        });
        var updatedProduct = await fixture.Products.UpdateAsync(createdProduct.Id, new UpdateProductRequestDto
        {
            CategoryId = 4, Name = "Limonada Menta", Price = 19.50m, Version = createdProduct.Version
        });
        Assert.Equal(19.50m, updatedProduct.Price);
        Assert.Equal(1L, updatedProduct.Version);
        await fixture.Products.ChangeAvailabilityAsync(updatedProduct.Id, false, updatedProduct.Version);

        var createdTable = await fixture.Tables.CreateAsync(new CreateTableRequestDto
        {
            Number = 8, Capacity = 6, Status = TableStatus.Available, Location = "Terraza"
        });
        var updatedTable = await fixture.Tables.UpdateAsync(createdTable.Id, new UpdateTableRequestDto
        {
            Number = 8, Capacity = 8, Location = "VIP", Version = createdTable.Version
        });
        Assert.Equal(8, updatedTable.Capacity);
        await fixture.Tables.ChangeStatusAsync(updatedTable.Id, TableStatus.OutOfService, updatedTable.Version);
    }

    [Fact]
    public async Task Admin_promotions_reservations_reports_and_audits_services_follow_api_contracts()
    {
        var futureStart = Now.AddDays(2);
        var futureEnd = Now.AddDays(10);

        using var fixture = new Fixture(async (request, token) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            var method = request.Method;

            if (method == HttpMethod.Get && path == "/api/v1/promotions?pageNumber=1&pageSize=20")
                return JsonStringResponse("""{"items":[{"id":5,"name":"2x1 Entradas","discountPercentage":15.00,"isActive":true,"products":[{"productId":11,"productName":"Sopa"}]}],"totalCount":1,"pageNumber":1,"pageSize":20,"totalPages":1}""");
            if (method == HttpMethod.Post && path == "/api/v1/promotions")
                return JsonStringResponse("""{"id":6,"name":"Postre Feliz","discountPercentage":20.00,"isActive":true,"products":[]}""", HttpStatusCode.Created);
            if (method == HttpMethod.Put && path == "/api/v1/promotions/6")
                return JsonStringResponse("""{"id":6,"name":"Postre Feliz VIP","discountPercentage":25.00,"isActive":true,"products":[]}""");
            if (method == HttpMethod.Patch && path == "/api/v1/promotions/6/status")
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (method == HttpMethod.Post && path == "/api/v1/promotions/6/products/11")
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (method == HttpMethod.Delete && path == "/api/v1/promotions/6/products/11")
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            if (method == HttpMethod.Get && path == "/api/v1/reservations?pageNumber=1&pageSize=20&tableId=4&status=Pending")
                return JsonStringResponse("""{"items":[{"id":70,"tableId":4,"tableNumber":4,"contactName":"Invitado Web","numberOfPeople":4,"status":"Pending"}],"totalCount":1,"pageNumber":1,"pageSize":20,"totalPages":1}""");
            if (method == HttpMethod.Post && path == "/api/v1/reservations")
                return JsonStringResponse("""{"id":71,"userId":42,"tableId":4,"tableNumber":4,"numberOfPeople":2,"status":"Pending"}""", HttpStatusCode.Created);
            if (method == HttpMethod.Put && path == "/api/v1/reservations/71")
                return JsonStringResponse("""{"id":71,"userId":42,"tableId":4,"tableNumber":4,"numberOfPeople":4,"status":"Pending"}""");
            if (method == HttpMethod.Patch && path == "/api/v1/reservations/71/status")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("Confirmed", doc.RootElement.GetProperty("status").GetString());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (method == HttpMethod.Get && path.StartsWith("/api/v1/reports/sales?", StringComparison.Ordinal))
                return JsonStringResponse("""{"totalOrders":12,"totalRevenue":540.50,"topSellingProducts":[{"productId":11,"productName":"Sopa","quantitySold":8,"revenue":100.00}]}""");
            if (method == HttpMethod.Get && path.StartsWith("/api/v1/reports/top-products?", StringComparison.Ordinal))
                return JsonStringResponse("""[{"productId":11,"productName":"Sopa","quantitySold":8,"revenue":100.00}]""");

            if (method == HttpMethod.Get && path == "/api/v1/audits?pageNumber=1&pageSize=20&userId=42&entity=Order")
                return JsonStringResponse("""{"items":[{"id":900,"userId":42,"userName":"Ana Pérez","action":"Update","entity":"Order","entityId":101,"newData":"{\"Status\":\"Served\"}"}],"totalCount":1,"pageNumber":1,"pageSize":20,"totalPages":1}""");
            if (method == HttpMethod.Get && path == "/api/v1/audits/900")
                return JsonStringResponse("""{"id":900,"userId":42,"userName":"Ana Pérez","action":"Update","entity":"Order","entityId":101,"newData":"{\"Status\":\"Served\"}"}""");

            throw new InvalidOperationException($"Unexpected {method} {path}");
        });
        fixture.StartSession();

        // Promotions
        var promos = await fixture.Promotions.GetPagedAsync();
        Assert.Single(promos.Items);
        var createdPromo = await fixture.Promotions.CreateAsync(new CreatePromotionRequestDto
        {
            Name = "Postre Feliz", DiscountPercentage = 20m, StartDate = futureStart, EndDate = futureEnd, IsActive = true
        });
        var updatedPromo = await fixture.Promotions.UpdateAsync(createdPromo.Id, new UpdatePromotionRequestDto
        {
            Name = "Postre Feliz VIP", DiscountPercentage = 25m, StartDate = futureStart, EndDate = futureEnd
        });
        Assert.Equal(25m, updatedPromo.DiscountPercentage);
        await fixture.Promotions.SetActiveAsync(createdPromo.Id, false);
        await fixture.Promotions.AddProductAsync(createdPromo.Id, 11);
        await fixture.Promotions.RemoveProductAsync(createdPromo.Id, 11);

        // Internal Reservations
        var reservations = await fixture.Reservations.GetPagedAsync(1, 20, tableId: 4, status: ReservationStatus.Pending);
        Assert.Equal("Invitado Web", Assert.Single(reservations.Items).ContactName);
        var createdRes = await fixture.Reservations.CreateAsync(new CreateReservationRequestDto
        {
            UserId = 42, TableId = 4, ReservationDateTime = futureStart, NumberOfPeople = 2
        });
        var updatedRes = await fixture.Reservations.UpdateAsync(createdRes.Id, new UpdateReservationRequestDto
        {
            UserId = 42, TableId = 4, ReservationDateTime = futureStart, NumberOfPeople = 4
        });
        Assert.Equal(4, updatedRes.NumberOfPeople);
        await fixture.Reservations.ChangeStatusAsync(createdRes.Id, ReservationStatus.Confirmed);

        // Reports
        var sales = await fixture.Reports.GetSalesReportAsync(Now.AddDays(-7), Now);
        Assert.Equal(12, sales.TotalOrders);
        Assert.Equal(540.50m, sales.TotalRevenue);
        var topProducts = await fixture.Reports.GetTopProductsAsync(Now.AddDays(-7), Now, 10);
        Assert.Single(topProducts);

        // Audits
        var audits = await fixture.Audits.GetPagedAsync(1, 20, userId: 42, entity: "Order");
        Assert.Single(audits.Items);
        var auditDetail = await fixture.Audits.GetByIdAsync(900);
        Assert.Equal("Order", auditDetail.Entity);

        // Local validations
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Products.CreateAsync(new CreateProductRequestDto { CategoryId = 1, Name = "X", Price = -1m }));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Promotions.CreateAsync(new CreatePromotionRequestDto { Name = "X", DiscountPercentage = 150m, StartDate = futureStart, EndDate = futureEnd }));
        await Assert.ThrowsAsync<ApiException>(() =>
            fixture.Reports.GetSalesReportAsync(Now, Now.AddDays(-1)));
    }

    [Fact]
    public async Task Daily_dashboard_and_report_use_the_same_Dominican_midnights_and_one_sales_request()
    {
        var queries = new List<string>();
        using var fixture = new Fixture((request, _) =>
        {
            Assert.Equal("/api/v1/reports/sales", request.RequestUri!.AbsolutePath);
            queries.Add(Uri.UnescapeDataString(request.RequestUri.Query));
            return Task.FromResult(JsonStringResponse("""{"totalOrders":1,"totalRevenue":25,"topSellingProducts":[]}"""));
        });
        fixture.StartSession();
        var day = ReportApiService.RestaurantDate(DateTimeOffset.Parse("2026-11-19T02:00:00Z"));
        Assert.Equal(new DateOnly(2026, 11, 18), day);
        var (start, end) = ReportApiService.RestaurantDateRange(day, day);
        var dashboard = await fixture.Reports.GetSalesReportAsync(start, end);
        var report = await fixture.Reports.GetSalesReportAsync(start, end, limit: 3);
        Assert.Equal(dashboard.TotalRevenue, report.TotalRevenue);
        Assert.Equal(new[]
        {
            "?startDate=2026-11-18T04:00:00.0000000+00:00&endDate=2026-11-19T04:00:00.0000000+00:00&limit=10",
            "?startDate=2026-11-18T04:00:00.0000000+00:00&endDate=2026-11-19T04:00:00.0000000+00:00&limit=3"
        }, queries);
    }

    [Fact]
    public async Task Desktop_reservation_reception_preorder_activation_and_network_recovery_workflow()
    {
        var reservationWithPreorderStatus = "Pending";
        var preorderStatus = "PreOrdered";
        var dropActivationResponse = true;

        using var fixture = new Fixture(async (request, token) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            var method = request.Method;

            // Reservation #80: Web guest reservation WITH preorder #300
            if (method == HttpMethod.Get && path == "/api/v1/reservations/80")
                return JsonStringResponse($$"""
                    {"id":80,"tableId":4,"tableNumber":4,"contactName":"Laura Gómez","contactPhone":"8095550199","reservationDateTime":"{{Now.AddHours(1):O}}","numberOfPeople":2,"status":"{{reservationWithPreorderStatus}}"}
                    """);
            if (method == HttpMethod.Get && path == "/api/v1/reservations/80/preorder")
                return JsonStringResponse($$"""
                    {"id":300,"reservationId":80,"tableId":4,"tableNumber":4,"userId":{{(preorderStatus == "PreOrdered" ? "null" : "42")}},"status":"{{preorderStatus}}","origin":"Web","type":"Table","total":37.50,"accountRequested":false,"version":1,"details":[{"id":701,"productId":11,"productName":"Risotto","quantity":3,"unitPrice":12.50,"subtotal":37.50,"note":"Al dente"}]}
                    """);
            if (method == HttpMethod.Post && path == "/api/v1/reservations/80/activate-preorder")
            {
                if (dropActivationResponse)
                {
                    dropActivationResponse = false;
                    reservationWithPreorderStatus = "Completed";
                    preorderStatus = "Pending";
                    throw new HttpRequestException("Simulated connection reset after server commit.");
                }
                return JsonStringResponse(
                    """{"title":"Request conflicts with the current state.","status":409,"detail":"The preorder has already been activated."}""",
                    HttpStatusCode.Conflict);
            }

            // Reservation #81: Web guest reservation WITHOUT preorder
            if (method == HttpMethod.Get && path == "/api/v1/reservations/81/preorder")
                return JsonStringResponse("""{"title":"Resource not found.","status":404,"detail":"Order not found."}""", HttpStatusCode.NotFound);
            if (method == HttpMethod.Post && path == "/api/v1/reservations/81/receive")
                return JsonStringResponse($$"""
                    {"id":81,"tableId":5,"tableNumber":5,"contactName":"Carlos Ruiz","contactPhone":"8095550188","reservationDateTime":"{{Now.AddMinutes(30):O}}","numberOfPeople":4,"status":"Completed"}
                    """);
            if (method == HttpMethod.Post && path == "/api/v1/orders")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal(81L, doc.RootElement.GetProperty("reservationId").GetInt64());
                Assert.Equal(5L, doc.RootElement.GetProperty("tableId").GetInt64());
                return JsonStringResponse("""
                    {"id":301,"reservationId":81,"tableId":5,"tableNumber":5,"userId":42,"status":"Pending","origin":"Desktop","type":"Table","total":25.00,"accountRequested":false,"version":1,"details":[{"id":702,"productId":11,"productName":"Sopa","quantity":2,"unitPrice":12.50,"subtotal":25.00,"note":null}]}
                    """, HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected {method} {path}");
        });
        fixture.StartSession();

        // 1. Arrival window evaluation mirrors the server boundaries.
        Assert.Equal(new(true, false, "En horario de recepción"),
            ReservationApiService.EvaluateArrivalWindow(Now.AddMinutes(30), Now));
        Assert.Equal(new(true, true, "Llegada tardía (dentro del turno de 2 horas)"),
            ReservationApiService.EvaluateArrivalWindow(Now.AddMinutes(-45), Now));
        Assert.False(ReservationApiService.EvaluateArrivalWindow(Now.AddMinutes(31), Now).CanReceive);
        Assert.False(ReservationApiService.EvaluateArrivalWindow(Now.AddHours(-2), Now).CanReceive);

        // 2. Staff inspects reservation #80 and its preorder #300 without guest token
        var res80 = await fixture.Reservations.GetByIdAsync(80);
        var preorder80 = await fixture.Reservations.TryGetPreOrderAsync(80);
        Assert.NotNull(preorder80);
        Assert.Equal(300L, preorder80.Id);
        Assert.Equal(OrderStatus.PreOrdered, preorder80.Status);
        Assert.Equal(37.50m, preorder80.Total);
        Assert.Contains("Preorden Web #300", ReservationApiService.FormatOperationalState(res80, preorder80));

        // 3. Activation experiences a network drop after server commit; Desktop surfaces null StatusCode and reconciles via GET
        var networkError = await Assert.ThrowsAsync<ApiException>(() => fixture.Reservations.ActivatePreOrderAsync(80));
        Assert.Null(networkError.StatusCode);

        var reconciledRes80 = await fixture.Reservations.GetByIdAsync(80);
        var reconciledOrder80 = await fixture.Reservations.TryGetPreOrderAsync(80);
        Assert.Equal(ReservationStatus.Completed, reconciledRes80.Status);
        Assert.Equal(OrderStatus.Pending, reconciledOrder80!.Status);
        Assert.Equal(37.50m, reconciledOrder80.Total);
        Assert.Contains("Cliente recibido · Orden #300 activa (Pendiente)",
            ReservationApiService.FormatOperationalState(reconciledRes80, reconciledOrder80));

        // 4. Duplicate activation returns 409 Conflict
        var conflict = await Assert.ThrowsAsync<ApiException>(() => fixture.Reservations.ActivatePreOrderAsync(80));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        // 5. Reservation #81 without preorder: TryGetPreOrderAsync returns null (404), ReceiveAsync marks Completed, and waiter creates linked order
        Assert.Null(await fixture.Reservations.TryGetPreOrderAsync(81));
        var received81 = await fixture.Reservations.ReceiveAsync(81);
        Assert.Equal(ReservationStatus.Completed, received81.Status);
        Assert.Contains("sin preorden", ReservationApiService.FormatOperationalState(received81, null));

        var linkedOrder81 = await fixture.Orders.CreateTableOrderAsync(
            5,
            [new CreateOrderDetailRequestDto { ProductId = 11, Quantity = 2 }],
            notes: null,
            reservationId: 81);
        Assert.Equal(301L, linkedOrder81.Id);
        Assert.Equal(81L, linkedOrder81.ReservationId);
        Assert.Equal(OrderStatus.Pending, linkedOrder81.Status);
    }

    private static HttpResponseMessage JsonStringResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage LoginResponse(string role = "Mesero") => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            accessToken = SyntheticToken,
            expiresAt = Now.AddMinutes(5),
            user = new { id = 42L, roleId = 2L, roleName = role, firstName = "Ana", lastName = "Pérez", isActive = true }
        })
    };

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        internal Clock Clock { get; } = new();
        internal SessionService Session { get; }
        internal ApiClient Api { get; }
        internal AuthApiService Auth { get; }
        internal TableApiService Tables { get; }
        internal ProductApiService Products { get; }
        internal OrderApiService Orders { get; }
        internal KitchenApiService Kitchen { get; }
        internal PaymentApiService Payments { get; }
        internal UserApiService Users { get; }
        internal CategoryApiService Categories { get; }
        internal PromotionApiService Promotions { get; }
        internal ReservationApiService Reservations { get; }
        internal ReportApiService Reports { get; }
        internal AuditApiService Audits { get; }

        internal Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            http = new HttpClient(new Handler(send)) { BaseAddress = ApiClient.ReadBaseAddress("https://api.example.test") };
            Session = new SessionService(Clock);
            Api = new ApiClient(http, Session);
            Auth = new AuthApiService(Api, Session);
            Tables = new TableApiService(Api);
            Products = new ProductApiService(Api);
            Orders = new OrderApiService(Api);
            Kitchen = new KitchenApiService(Api);
            Payments = new PaymentApiService(Api);
            Users = new UserApiService(Api);
            Categories = new CategoryApiService(Api);
            Promotions = new PromotionApiService(Api);
            Reservations = new ReservationApiService(Api);
            Reports = new ReportApiService(Api);
            Audits = new AuditApiService(Api);
        }

        internal void StartSession() => Assert.True(Session.TryStart(new(42, "Ana Pérez", "Mesero", Now.AddMinutes(5)), SyntheticToken));
        public void Dispose() => http.Dispose();
    }
}
