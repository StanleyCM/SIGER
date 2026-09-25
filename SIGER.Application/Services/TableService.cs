using SIGER.Application.Base;
using SIGER.Application.DTOs.Tables;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Exceptions;

namespace SIGER.Application.Services;

public class TableService : ITableService
{
    private readonly ITableRepository _tableRepository;
    private readonly IUnitOfWork _unitOfWork;

    public TableService(ITableRepository tableRepository, IUnitOfWork unitOfWork)
    {
        _tableRepository = tableRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TableDto>> CreateAsync(CreateTableRequestDto request, CancellationToken cancellationToken = default)
    {
        var error = Validate(request.Number, request.Capacity);
        if (error is not null) return Result<TableDto>.Failure(error);
        var now = DateTimeOffset.UtcNow;
        var table = new Table
        {
            Number = request.Number, Capacity = request.Capacity, Status = request.Status, Location = Normalize(request.Location),
            CreatedAt = now, UpdatedAt = now, Version = 0
        };
        await _tableRepository.AddAsync(table, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<TableDto>.Success(Map(table));
    }

    public async Task<Result<TableDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var table = await _tableRepository.GetByIdAsync(id, cancellationToken);
        return table is null ? Result<TableDto>.Failure("Table not found.") : Result<TableDto>.Success(Map(table));
    }

    public async Task<Result<PaginatedResult<TableDto>>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 200 || ((long)pageNumber - 1) * pageSize > int.MaxValue) return Result<PaginatedResult<TableDto>>.Failure("Page number and size must be positive, size at most 200, and offset within the supported range.");
        var page = await _tableRepository.GetPagedAsync(pageNumber, pageSize, cancellationToken);
        return Result<PaginatedResult<TableDto>>.Success(new(page.Items.Select(Map), page.TotalCount, page.PageNumber, page.PageSize));
    }

    public async Task<Result<IReadOnlyCollection<TableDto>>> GetAvailableAsync(CancellationToken cancellationToken = default)
    {
        var tables = await _tableRepository.GetAvailableAsync(cancellationToken);
        return Result<IReadOnlyCollection<TableDto>>.Success(tables.Select(Map).ToArray());
    }

    public async Task<Result<TableDto>> UpdateAsync(long id, UpdateTableRequestDto request, CancellationToken cancellationToken = default)
    {
        var error = Validate(request.Number, request.Capacity);
        if (error is not null) return Result<TableDto>.Failure(error);
        var table = await _tableRepository.GetByIdAsync(id, cancellationToken);
        if (table is null) return Result<TableDto>.Failure("Table not found.");
        if (table.Version != request.Version) throw new BusinessRuleException("The table changed. Reload it and try again.");
        table.Number = request.Number;
        table.Capacity = request.Capacity;
        table.Location = Normalize(request.Location);
        table.UpdatedAt = DateTimeOffset.UtcNow;
        _tableRepository.Update(table);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<TableDto>.Success(Map(table));
    }

    public async Task<Result> ChangeStatusAsync(long id, UpdateTableStatusRequestDto request, CancellationToken cancellationToken = default)
    {
        var table = await _tableRepository.GetByIdAsync(id, cancellationToken);
        if (table is null) return Result.Failure("Table not found.");
        if (table.Version != request.Version) throw new BusinessRuleException("The table changed. Reload it and try again.");
        table.Status = request.Status;
        table.UpdatedAt = DateTimeOffset.UtcNow;
        _tableRepository.Update(table);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static string? Validate(int number, int capacity)
        => number <= 0 ? "Table number must be greater than zero." : capacity <= 0 ? "Table capacity must be greater than zero." : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static TableDto Map(Table table) => new()
    {
        Id = table.Id, Number = table.Number, Capacity = table.Capacity, Status = table.Status, Location = table.Location,
        CreatedAt = table.CreatedAt, UpdatedAt = table.UpdatedAt, Version = table.Version
    };
}
