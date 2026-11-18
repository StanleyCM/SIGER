using System.Globalization;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Audits;

namespace SIGER.Desktop.Services;

internal sealed class AuditApiService(ApiClient api)
{
    internal Task<PaginatedResult<AuditDto>> GetPagedAsync(
        int pageNumber = 1,
        int pageSize = 20,
        long? userId = null,
        string? entity = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "La paginación de auditoría debe estar entre 1 y 200.");
        if (userId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId), "El identificador de usuario debe ser positivo.");
        if (startDate.HasValue && endDate.HasValue && endDate < startDate)
            throw new ApiException("La fecha final no puede ser anterior a la fecha inicial.");

        var query = new List<string>(6)
        {
            $"pageNumber={pageNumber}",
            $"pageSize={pageSize}"
        };
        if (userId.HasValue) query.Add($"userId={userId.Value}");
        if (!string.IsNullOrWhiteSpace(entity)) query.Add($"entity={Uri.EscapeDataString(entity.Trim())}");
        if (startDate.HasValue)
            query.Add($"startDate={Uri.EscapeDataString(startDate.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
        if (endDate.HasValue)
            query.Add($"endDate={Uri.EscapeDataString(endDate.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");

        return api.SendAsync<PaginatedResult<AuditDto>>(
            HttpMethod.Get,
            "api/v1/audits?" + string.Join('&', query),
            cancellationToken: cancellationToken);
    }

    internal Task<AuditDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync<AuditDto>(HttpMethod.Get, $"api/v1/audits/{id}", cancellationToken: cancellationToken);
    }
}
