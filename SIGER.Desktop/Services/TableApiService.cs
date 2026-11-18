using SIGER.Application.Base;
using SIGER.Application.DTOs.Tables;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Services;

internal sealed class TableApiService(ApiClient api)
{
    internal Task<PaginatedResult<TableDto>> GetPagedAsync(int pageNumber = 1, int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "La paginación de mesas debe estar entre 1 y 200.");

        return api.SendAsync<PaginatedResult<TableDto>>(
            HttpMethod.Get,
            $"api/v1/tables?pageNumber={pageNumber}&pageSize={pageSize}",
            cancellationToken: cancellationToken);
    }

    internal Task<IReadOnlyCollection<TableDto>> GetAvailableAsync(CancellationToken cancellationToken = default) =>
        api.SendAsync<IReadOnlyCollection<TableDto>>(
            HttpMethod.Get,
            "api/v1/tables/available",
            cancellationToken: cancellationToken);

    internal Task<TableDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync<TableDto>(
            HttpMethod.Get,
            $"api/v1/tables/{id}",
            cancellationToken: cancellationToken);
    }

    internal Task<TableDto> CreateAsync(CreateTableRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTable(request.Number, request.Capacity);
        if (!Enum.IsDefined(request.Status))
            throw new ApiException("Selecciona un estado de mesa válido.");

        var body = new CreateTableRequestDto
        {
            Number = request.Number,
            Capacity = request.Capacity,
            Status = request.Status,
            Location = Normalize(request.Location)
        };
        return api.SendAsync<TableDto>(HttpMethod.Post, "api/v1/tables", body, cancellationToken: cancellationToken);
    }

    internal Task<TableDto> UpdateAsync(long id, UpdateTableRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.Version);
        ValidateTable(request.Number, request.Capacity);

        var body = new UpdateTableRequestDto
        {
            Number = request.Number,
            Capacity = request.Capacity,
            Location = Normalize(request.Location),
            Version = request.Version
        };
        return api.SendAsync<TableDto>(HttpMethod.Put, $"api/v1/tables/{id}", body, cancellationToken: cancellationToken);
    }

    internal Task ChangeStatusAsync(long id, TableStatus status, long version, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        if (!Enum.IsDefined(status))
            throw new ApiException("Selecciona un estado de mesa válido.");

        return api.SendAsync(
            HttpMethod.Patch,
            $"api/v1/tables/{id}/status",
            new UpdateTableStatusRequestDto { Status = status, Version = version },
            cancellationToken: cancellationToken);
    }

    private static void ValidateTable(int number, int capacity)
    {
        if (number <= 0)
            throw new ApiException("El número de mesa debe ser mayor que cero.");
        if (capacity <= 0)
            throw new ApiException("La capacidad de la mesa debe ser mayor que cero.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string FormatStatus(TableStatus status) => status switch
    {
        TableStatus.Available => "Disponible",
        TableStatus.Occupied => "Ocupada",
        TableStatus.Reserved => "Reservada",
        TableStatus.OutOfService => "Fuera de servicio",
        _ => status.ToString()
    };
}
