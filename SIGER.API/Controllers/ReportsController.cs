using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Reports;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = ApiPolicies.AdministratorOnly)]
public sealed class ReportsController(IReportService service) : ControllerBase
{
    [HttpGet("sales")]
    [ProducesResponseType(typeof(SalesReportDto), 200)]
    public async Task<IActionResult> GetSales([Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] DateTimeOffset startDate,
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] DateTimeOffset endDate, CancellationToken cancellationToken) =>
        (await service.GetSalesReportAsync(startDate.ToUniversalTime(), endDate.ToUniversalTime(), cancellationToken)).ToHttp(this);

    [HttpGet("top-products")]
    [ProducesResponseType(typeof(IReadOnlyCollection<TopSellingProductDto>), 200)]
    public async Task<IActionResult> GetTopProducts([Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] DateTimeOffset startDate,
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] DateTimeOffset endDate,
        [Range(1, 200)] int limit = 10, CancellationToken cancellationToken = default) =>
        (await service.GetTopSellingProductsAsync(startDate.ToUniversalTime(), endDate.ToUniversalTime(), limit, cancellationToken)).ToHttp(this);
}
