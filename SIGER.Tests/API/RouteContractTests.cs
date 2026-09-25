using System.Net;
using System.Text;
using Moq;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Interfaces.Services;
namespace SIGER.Tests.API;

public class RouteContractTests
{
    [Theory]
    [InlineData("GET", "users/roles", "", 200)]
    [InlineData("GET", "categories/1", "", 200)]
    [InlineData("GET", "products/1", "", 200)]
    [InlineData("GET", "tables/1", "", 200)]
    [InlineData("GET", "tables/available", "", 200)]
    [InlineData("GET", "orders/1", "", 200)]
    [InlineData("GET", "reservations/1", "", 200)]
    [InlineData("GET", "promotions/1", "", 200)]
    [InlineData("GET", "promotions/active", "", 200)]
    [InlineData("GET", "audits/1", "", 200)]
    [InlineData("GET", "reports/top-products?startDate=2026-01-01&endDate=2026-01-02", "", 200)]
    [InlineData("POST", "categories", "{ \"name\": \"Food\" }", 201)]
    [InlineData("PUT", "categories/1", "{ \"name\": \"Food\" }", 200)]
    [InlineData("PATCH", "categories/1/status", "{ \"isActive\": true }", 204)]
    [InlineData("POST", "products", "{ \"categoryId\": 1, \"name\": \"Soup\", \"price\": 5 }", 201)]
    [InlineData("PUT", "products/1", "{ \"categoryId\": 1, \"name\": \"Soup\", \"price\": 5, \"version\": 1 }", 200)]
    [InlineData("PATCH", "products/1/availability", "{ \"isAvailable\": false, \"version\": 1 }", 204)]
    [InlineData("POST", "tables", "{ \"number\": 1, \"capacity\": 4 }", 201)]
    [InlineData("PUT", "tables/1", "{ \"number\": 1, \"capacity\": 4, \"version\": 1 }", 200)]
    [InlineData("PATCH", "tables/1/status", "{ \"status\": \"Reserved\", \"version\": 1 }", 204)]
    [InlineData("PUT", "users/1", "{ \"roleId\": 1, \"firstName\": \"Ana\", \"lastName\": \"Diaz\", \"email\": \"a@example.test\" }", 200)]
    [InlineData("PATCH", "users/1/status", "{ \"isActive\": false }", 204)]
    [InlineData("POST", "orders/1/items", "{ \"productId\": 1, \"quantity\": 2 }", 200)]
    [InlineData("DELETE", "orders/1/items/2", "", 200)]
    [InlineData("POST", "orders/1/request-account", "{ \"version\": 1 }", 200)]
    [InlineData("PATCH", "kitchen/orders/1/in-preparation", "", 200)]
    [InlineData("PATCH", "kitchen/orders/1/ready", "", 200)]
    [InlineData("PUT", "reservations/1", "{ \"userId\": 42, \"tableId\": 1, \"numberOfPeople\": 2, \"reservationDateTime\": \"2027-01-01T12:00:00Z\" }", 200)]
    [InlineData("POST", "promotions", "{ \"name\": \"Lunch\", \"discountPercentage\": 10, \"startDate\": \"2027-01-01T00:00:00Z\", \"endDate\": \"2027-01-02T00:00:00Z\" }", 201)]
    [InlineData("PUT", "promotions/1", "{ \"name\": \"Lunch\", \"discountPercentage\": 10, \"startDate\": \"2027-01-01T00:00:00Z\", \"endDate\": \"2027-01-02T00:00:00Z\" }", 200)]
    [InlineData("PATCH", "promotions/1/status", "{ \"isActive\": true }", 204)]
    [InlineData("POST", "promotions/1/products/2", "", 204)]
    [InlineData("DELETE", "promotions/1/products/2", "", 204)]
    public async Task Administrator_routes_bind_and_return_expected_success_codes(string method, string path, string body, int status)
    {
        using var f = new ApiFactory(); using var client = f.Client("Administrador");
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/" + path);
        if (body.Length > 0) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        Assert.Equal(status, (int)(await client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("PUT")] [InlineData("PATCH")]
    public async Task Missing_reservation_is_not_updated(string method)
    {
        using var f = new ApiFactory(); using var client = f.Client("Cliente");
        f.Service<IReservationService>().Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReservationDto>.Failure("Reservation not found."));
        var path = "/api/v1/reservations/1" + (method == "PATCH" ? "/status" : "");
        var body = method == "PATCH" ? "{\"status\":\"Cancelled\"}" : "{\"userId\":42}";
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Production_does_not_expose_swagger_or_openapi()
    {
        using var f = new ApiFactory { EnvironmentName = "Production" }; using var client = f.Client("Administrador");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }
}
