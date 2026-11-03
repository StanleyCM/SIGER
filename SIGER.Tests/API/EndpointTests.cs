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

    [Theory]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/orders")]
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
    public async Task Roles_enforce_controller_policies(string role, string path, int status)
    {
        using var f = new ApiFactory(); using var client = f.Client(role);
        Assert.Equal(status, (int)(await client.GetAsync(path)).StatusCode);
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

}
