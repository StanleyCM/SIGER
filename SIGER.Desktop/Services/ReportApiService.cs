using System.Globalization;
using SIGER.Application.DTOs.Reports;

namespace SIGER.Desktop.Services;

internal sealed class ReportApiService(ApiClient api)
{
    internal Task<SalesReportDto> GetSalesReportAsync(DateTimeOffset startDate, DateTimeOffset endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            throw new ApiException("La fecha final no puede ser anterior a la fecha inicial.");

        var start = Uri.EscapeDataString(startDate.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var end = Uri.EscapeDataString(endDate.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return api.SendAsync<SalesReportDto>(
            HttpMethod.Get,
            $"api/v1/reports/sales?startDate={start}&endDate={end}",
            cancellationToken: cancellationToken);
    }

    internal Task<IReadOnlyCollection<TopSellingProductDto>> GetTopProductsAsync(DateTimeOffset startDate,
        DateTimeOffset endDate, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            throw new ApiException("La fecha final no puede ser anterior a la fecha inicial.");
        if (limit is < 1 or > 200)
            throw new ApiException("El límite de productos debe estar entre 1 y 200.");

        var start = Uri.EscapeDataString(startDate.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var end = Uri.EscapeDataString(endDate.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return api.SendAsync<IReadOnlyCollection<TopSellingProductDto>>(
            HttpMethod.Get,
            $"api/v1/reports/top-products?startDate={start}&endDate={end}&limit={limit}",
            cancellationToken: cancellationToken);
    }
}
