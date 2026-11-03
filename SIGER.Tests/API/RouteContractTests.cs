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

}
