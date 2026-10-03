using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Interfaces.Services;
using SIGER.Tests.Application;

namespace SIGER.Tests.API;

public class PublicReservationSecurityTests
{
    private static ApiFactory RealGuestApi(GuestFixture guest) => new()
    {
        ConfigureBackend = services =>
        {
            services.RemoveAll<IGuestReservationService>(); services.AddSingleton<IGuestReservationService>(guest.Guests);
            services.RemoveAll<IPreOrderService>(); services.AddSingleton<IPreOrderService>(guest.Preorders);
        }
    };

    [Fact]
    public async Task Guest_flow_works_without_JWT_and_token_is_only_in_creation_response()
    {
        var guest = new GuestFixture(); using var f = RealGuestApi(guest); using var c = f.Client();
        var response = await c.PostAsJsonAsync("/api/v1/public/reservations", guest.Request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var created = (await response.Content.ReadFromJsonAsync<GuestReservationCreatedDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }))!;
        guest.F.Reservations.Setup(x => x.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(guest.SavedReservation);
        Assert.Equal("Reserva recibida: pendiente", created.Message);
        Assert.DoesNotContain(created.AccessToken, response.Headers.Location!.ToString());
        c.DefaultRequestHeaders.Add("X-Reservation-Token", created.AccessToken);
        var get = await c.GetAsync("/api/v1/public/reservations/10"); Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.DoesNotContain("token", await get.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var pre = await c.PostAsJsonAsync("/api/v1/public/reservations/10/preorder", guest.Items);
        Assert.Equal(HttpStatusCode.Created, pre.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/public/reservations/10/preorder")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/public/reservations/10/preorder", guest.Items)).StatusCode);
    }

    [Theory]
    [InlineData("GET", "10", false)] [InlineData("GET", "10/preorder", false)] [InlineData("POST", "10/preorder", false)]
    [InlineData("GET", "11", true)] [InlineData("GET", "11/preorder", true)] [InlineData("POST", "11/preorder", true)]
    public async Task Numeric_ID_or_another_guests_token_does_not_grant_access(string method, string suffix, bool header)
    {
        var guest = new GuestFixture(); using var f = RealGuestApi(guest); using var c = f.Client();
        if (header) c.DefaultRequestHeaders.Add("X-Reservation-Token", guest.Credential);
        var path = "/api/v1/public/reservations/" + suffix;
        var response = method == "GET" ? await c.GetAsync(path) : await c.PostAsJsonAsync(path, guest.Items);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); guest.F.NoSave();
    }

    [Fact]
    public async Task Query_string_token_is_not_accepted()
    {
        var g = new GuestFixture(); using var f = RealGuestApi(g); using var c = f.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/public/reservations/10?token=" + g.Credential)).StatusCode);
    }

    [Theory]
    [InlineData("categories")] [InlineData("products")] [InlineData("products/1")] [InlineData("promotions")]
    public async Task Public_catalog_does_not_require_JWT(string path)
    {
        using var f = new ApiFactory(); using var c = f.Client();
        f.Service<IPublicCatalogService>().Setup(s => s.GetProductAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SIGER.Application.Base.Result<SIGER.Application.DTOs.Catalog.PublicProductDto>.Success(new(1, 1, "Soup", null, 10, null)));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/public/" + path)).StatusCode);
    }

    [Theory]
    [InlineData("users")] [InlineData("users/roles")] [InlineData("categories")] [InlineData("products")]
    [InlineData("promotions")] [InlineData("tables")] [InlineData("orders")] [InlineData("kitchen/orders")]
    [InlineData("payments/order/1")] [InlineData("reports/sales")] [InlineData("audits")] [InlineData("reservations")]
    public async Task Internal_routes_still_require_authorization(string path)
    {
        using var f = new ApiFactory(); using var c = f.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/" + path)).StatusCode);
    }

    [Fact]
    public async Task Public_writes_are_rate_limited()
    {
        var guest = new GuestFixture(); using var f = RealGuestApi(guest); using var c = f.Client();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/v1/public/reservations", guest.Request)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/api/v1/public/reservations", guest.Request)).StatusCode);
    }

    [Fact]
    public async Task Preorder_ignores_client_total_and_operational_fields()
    {
        var g = new GuestFixture(); using var f = RealGuestApi(g); using var c = f.Client();
        c.DefaultRequestHeaders.Add("X-Reservation-Token", g.Credential);
        var response = await c.PostAsJsonAsync("/api/v1/public/reservations/10/preorder", new
        { total = 0.01m, userId = 1, tableId = 99, status = "Paid", items = new[] { new { productId = 3, quantity = 2, unitPrice = 0.01m } } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(25m, g.SavedOrder!.Total); Assert.Null(g.SavedOrder.UserId); Assert.Equal(4, g.SavedOrder.TableId);
        Assert.Equal(SIGER.Domain.Enums.OrderStatus.PreOrdered, g.SavedOrder.Status);
    }

    [Theory]
    [InlineData("invalid")] [InlineData("expired")]
    public async Task Invalid_and_expired_credentials_return_401(string kind)
    {
        var g = new GuestFixture(); using var f = RealGuestApi(g); using var c = f.Client();
        if (kind == "expired") g.Reservation.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        c.DefaultRequestHeaders.Add("X-Reservation-Token", kind == "invalid" ? g.Tokens.Generate() : g.Credential);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/public/reservations/10")).StatusCode);
    }

    [Fact]
    public async Task Vite_origin_can_send_reservation_header_but_unknown_origin_is_not_allowed()
    {
        using var f = new ApiFactory { AllowedOrigin = "http://localhost:49338" }; using var c = f.Client();
        foreach (var origin in new[] { "http://localhost:49338", "https://unknown.invalid" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/public/reservations/10");
            request.Headers.Add("Origin", origin); request.Headers.Add("Access-Control-Request-Method", "GET");
            request.Headers.Add("Access-Control-Request-Headers", "X-Reservation-Token");
            var response = await c.SendAsync(request);
            if (origin.Contains("localhost")) Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            else Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }
}
