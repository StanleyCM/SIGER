using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Payments;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController]
[Route("api/v1/payments")]
[Authorize(Policy = ApiPolicies.CashierOrAdministrator)]
public sealed class PaymentsController(IPaymentService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(PaymentDto), 201)]
    public async Task<IActionResult> Process(ProcessPaymentRequestDto request, CancellationToken cancellationToken)
    {
        request.UserId = User.GetLocalUserId();
        return (await service.ProcessPaymentAsync(request, cancellationToken)).ToHttp(this, nameof(GetByOrderId), value => new { orderId = value.OrderId });
    }

    [HttpGet("order/{orderId:long}")]
    [ProducesResponseType(typeof(PaymentDto), 200)]
    public async Task<IActionResult> GetByOrderId(long orderId, CancellationToken cancellationToken) =>
        (await service.GetByOrderIdAsync(orderId, cancellationToken)).ToHttp(this);
}
