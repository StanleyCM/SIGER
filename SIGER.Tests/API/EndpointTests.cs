using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Auth;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Payments;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.DTOs.Users;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Enums;
using SIGER.Domain.Exceptions;

namespace SIGER.Tests.API;

public class EndpointTests
{
    [Fact]
    public async Task User_compensation_failure_returns_safe_operation_reference()
    {
        using var f = new ApiFactory(); using var client = f.Client("Administrador"); var operation = Guid.NewGuid();
        f.Service<IUserService>().Setup(x => x.UpdateStatusAsync(1, It.IsAny<UpdateUserStatusRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SIGER.Application.Exceptions.UserOperationException(operation, true));
        var response = await client.PatchAsJsonAsync("/api/v1/users/1/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(operation.ToString(), text); Assert.DoesNotContain("stack", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Overlapping_reservation_is_a_safe_conflict()
    {
        using var f = new ApiFactory(); using var client = f.Client("Mesero");
        f.Service<IReservationService>().Setup(x => x.CreateAsync(It.IsAny<CreateReservationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReservationDto>.Failure("The table already has an overlapping reservation."));
        var response = await client.PostAsJsonAsync("/api/v1/reservations", new
        { userId = 42, tableId = 4, numberOfPeople = 2, reservationDateTime = DateTimeOffset.UtcNow.AddDays(1) });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }
    [Theory]
    [InlineData("/api/v1/users")] [InlineData("/api/v1/orders")] [InlineData("/api/v1/kitchen/orders")]
    [InlineData("/api/v1/payments/order/1")] [InlineData("/api/v1/reservations")]
    public async Task Protected_routes_require_authentication(string path)
    {
        using var f = new ApiFactory(); using var client = f.Client();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Theory]
    [InlineData("Administrador", "/api/v1/users", 200)]
    [InlineData("Mesero", "/api/v1/users", 403)]
    [InlineData("Mesero", "/api/v1/orders", 200)]
    [InlineData("Cocinero", "/api/v1/kitchen/orders", 200)]
    [InlineData("Cocinero", "/api/v1/users", 403)]
    [InlineData("Cocinero", "/api/v1/payments/order/1", 403)]
    [InlineData("Cajero", "/api/v1/payments/order/1", 200)]
    [InlineData("Cajero", "/api/v1/users", 403)]
    [InlineData("Cliente", "/api/v1/orders", 403)]
    [InlineData("Cliente", "/api/v1/reservations", 200)]
    [InlineData("Cliente", "/api/v1/users", 403)]
    [InlineData("Unknown", "/api/v1/users", 403)]
    [InlineData("Administrador", "/api/v1/categories", 200)]
    [InlineData("Administrador", "/api/v1/tables", 200)]
    [InlineData("Administrador", "/api/v1/promotions", 200)]
    [InlineData("Administrador", "/api/v1/reports/sales?startDate=2026-01-01&endDate=2026-01-02", 200)]
    [InlineData("Administrador", "/api/v1/audits", 200)]
    public async Task Roles_enforce_controller_policies(string role, string path, int status)
    {
        using var f = new ApiFactory(); using var client = f.Client(role);
        Assert.Equal(status, (int)(await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("issuer")] [InlineData("audience")] [InlineData("expired")] [InlineData("signature")]
    [InlineData("subject")] [InlineData("missing")] [InlineData("inactive")] [InlineData("inactive-role")] [InlineData("malformed")]
    public async Task Invalid_identity_is_rejected(string scenario)
    {
        using var f = new ApiFactory(); using var client = f.Client("Administrador");
        if (scenario == "missing") f.UserExists = false;
        if (scenario == "inactive") f.LocalUser.IsActive = false;
        if (scenario == "inactive-role") f.LocalUser.Role.IsActive = false;
        client.DefaultRequestHeaders.Authorization = new("Bearer", scenario == "malformed" ? "not-a-jwt" : f.Token(
            issuer: scenario == "issuer" ? "https://wrong.invalid" : null,
            audience: scenario == "audience" ? "wrong" : "authenticated",
            expired: scenario == "expired", wrongSignature: scenario == "signature",
            subject: scenario == "subject" ? "invalid-guid" : null));
        var response = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("IDX", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Es256_is_accepted_and_token_roles_are_not_trusted()
    {
        using var f = new ApiFactory(); using var client = f.Client("Mesero");
        client.DefaultRequestHeaders.Authorization = new("Bearer", f.Token(ecdsa: true, spoofRole: true));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/orders")).StatusCode);
    }

    [Fact]
    public async Task User_creation_returns_created_location_and_safe_dto()
    {
        using var f = new ApiFactory(); using var client = f.Client("Administrador");
        f.Service<IUserService>().Setup(x => x.CreateAsync(It.IsAny<CreateUserRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Success(new() { Id = 7, FirstName = "Ana" }));
        var response = await client.PostAsJsonAsync("/api/v1/users", new { roleId = 1, firstName = "Ana", lastName = "Diaz", email = "a@example.test", password = "synthetic-input" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith("/api/v1/users/7", response.Headers.Location!.ToString());
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Order_and_payment_use_trusted_local_user_and_item_route_id()
    {
        using var f = new ApiFactory(); using var client = f.Client("Mesero");
        var created = await client.PostAsJsonAsync("/api/v1/orders", new { userId = 999, type = "TakeAway", origin = "Desktop", items = new[] { new { productId = 3, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        f.Service<IOrderService>().Verify(x => x.CreateOrderAsync(It.Is<CreateOrderRequestDto>(r => r.UserId == 42), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/orders/5/items/6", new { orderDetailId = 999, quantity = 2 })).StatusCode);
        f.Service<IOrderService>().Verify(x => x.UpdateItemAsync(5, It.Is<UpdateOrderItemRequestDto>(r => r.OrderDetailId == 6), It.IsAny<CancellationToken>()), Times.Once);
        f.LocalUser.Role.Name = "Cajero";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/payments", new { orderId = 5, userId = 999, amount = 25, method = "Cash" })).StatusCode);
        f.Service<IPaymentService>().Verify(x => x.ProcessPaymentAsync(It.Is<ProcessPaymentRequestDto>(r => r.UserId == 42), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Waiter_cannot_bypass_payment_or_kitchen_workflows()
    {
        using var f = new ApiFactory(); using var client = f.Client("Mesero");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync("/api/v1/orders/5/status", new { status = "Paid", version = 1 })).StatusCode);
        f.Service<IOrderService>().Verify(x => x.UpdateStatusAsync(It.IsAny<long>(), It.IsAny<UpdateOrderStatusRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/api/v1/orders/5/status", new { status = "Served", version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsync("/api/v1/kitchen/orders/5/ready", null)).StatusCode);
    }

    [Fact]
    public async Task Client_reservations_cannot_access_or_assign_other_users()
    {
        using var f = new ApiFactory(); using var client = f.Client("Cliente");
        f.Service<IReservationService>().Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReservationDto>.Success(new() { Id = 1, UserId = 99 }));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reservations/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/v1/reservations/1", new { userId = 42, numberOfPeople = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync("/api/v1/reservations/1/status", new { status = "Cancelled" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/reservations?userId=99")).StatusCode);
        f.Service<IReservationService>().Verify(x => x.GetPagedAsync(1, 20, 42, null, null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/reservations", new { userId = 99, tableId = 4, numberOfPeople = 2, reservationDateTime = "2027-01-01T12:00:00-04:00" })).StatusCode);
        f.Service<IReservationService>().Verify(x => x.CreateAsync(It.Is<CreateReservationRequestDto>(r => r.UserId == 42 && r.ReservationDateTime.Offset == TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
        f.Service<IReservationService>().Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Result<ReservationDto>.Success(new() { Id = 1, UserId = 42 }));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync("/api/v1/reservations/1/status", new { status = "Confirmed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync("/api/v1/reservations/1/status", new { status = "Cancelled" })).StatusCode);
    }

    [Theory]
    [InlineData("User not found.", 404)]
    [InlineData("A user with this email already exists.", 409)]
    [InlineData("First name is required.", 400)]
    public async Task Result_failures_are_not_200(string error, int status)
    {
        using var f = new ApiFactory(); using var client = f.Client("Administrador");
        f.Service<IUserService>().Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Result<UserDto>.Failure(error));
        Assert.Equal(status, (int)(await client.GetAsync("/api/v1/users/1")).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/users?pageNumber=0")] [InlineData("/api/v1/products?pageSize=201")]
    [InlineData("/api/v1/orders?status=999")] [InlineData("/api/v1/reports/sales")]
    public async Task Model_binding_rejects_bad_filters(string path)
    {
        using var f = new ApiFactory(); using var client = f.Client("Administrador");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Invalid_json_and_integer_enums_are_rejected()
    {
        using var f = new ApiFactory(); using var client = f.Client("Mesero");
        var response = await client.PatchAsJsonAsync("/api/v1/orders/5/status", new { status = 4, version = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        response = await client.PostAsync("/api/v1/orders", new StringContent("{ invalid", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("BytePosition", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_success_failure_and_rate_limit()
    {
        using var f = new ApiFactory { LoginLimit = 2 }; using var client = f.Client();
        f.Service<IAuthService>().SetupSequence(x => x.LoginAsync(It.IsAny<LoginRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(new() { AccessToken = "synthetic-token", User = new() }))
            .ReturnsAsync(Result<LoginResponseDto>.Failure("Invalid credentials."));
        var first = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "a@example.test", password = "synthetic-input" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl!.NoStore);
        Assert.Contains("synthetic-token", await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "a", password = "b" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "a", password = "b" })).StatusCode);
    }

    [Fact]
    public async Task Public_catalog_is_anonymous_and_rate_limited()
    {
        using var f = new ApiFactory { PublicLimit = 1 }; using var client = f.Client();
        Assert.Equal("[]", await client.GetStringAsync("/api/v1/products/available"));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/v1/products/available")).StatusCode);
    }

    [Theory]
    [InlineData("https://allowed.example.test", true)] [InlineData("https://evil.example.test", false)]
    public async Task Cors_preflight_only_allows_configured_origin(string origin, bool allowed)
    {
        using var f = new ApiFactory(); using var client = f.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/products/available");
        request.Headers.Add("Origin", origin); request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Development_openapi_describes_security_and_swagger_is_available()
    {
        using var f = new ApiFactory(); using var client = f.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        Assert.Equal("bearer", json.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        var paths = json.RootElement.GetProperty("paths");
        Assert.Equal(0, paths.GetProperty("/api/v1/auth/login").GetProperty("post").GetProperty("security").GetArrayLength());
        Assert.Equal(1, paths.GetProperty("/api/v1/users").GetProperty("get").GetProperty("security").GetArrayLength());
    }

    [Theory]
    [InlineData("notfound", 404)] [InlineData("validation", 400)] [InlineData("unauthorized", 401)]
    [InlineData("business", 409)] [InlineData("concurrency", 409)] [InlineData("domain", 400)] [InlineData("unexpected", 500)]
    public async Task Exception_mapping_never_discloses_internal_details(string kind, int status)
    {
        using var f = new ApiFactory(); using var client = f.Client("Administrador");
        const string sensitive = "PRIVATE_SQL_CONNECTION_TOKEN";
        Exception error = kind switch
        {
            "notfound" => new SIGER.Application.Exceptions.NotFoundException(sensitive),
            "validation" => new SIGER.Application.Exceptions.ValidationException(sensitive),
            "unauthorized" => new SIGER.Application.Exceptions.UnauthorizedException(sensitive),
            "business" => new BusinessRuleException(sensitive),
            "concurrency" => new DbUpdateConcurrencyException(sensitive),
            "domain" => new DomainException(sensitive),
            _ => new InvalidOperationException(sensitive, new Exception(sensitive))
        };
        f.Service<IUserService>().Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ThrowsAsync(error);
        var response = await client.GetAsync("/api/v1/users/1");
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(sensitive, body); Assert.DoesNotContain("StackTrace", body); Assert.Contains("traceId", body);
    }
}
