using SIGER.Application.Base;
using SIGER.Application.DTOs.Categories;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;

namespace SIGER.Application.Services;

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CategoryService(ICategoryRepository categoryRepository, IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return Result<CategoryDto>.Failure("Category name is required.");
        var now = DateTimeOffset.UtcNow;
        var category = new Category
        {
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<CategoryDto>.Success(Map(category));
    }

    public async Task<Result<CategoryDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var category = await _categoryRepository.GetByIdAsync(id, cancellationToken);
        return category is null ? Result<CategoryDto>.Failure("Category not found.") : Result<CategoryDto>.Success(Map(category));
    }

    public async Task<Result<PaginatedResult<CategoryDto>>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1) return Result<PaginatedResult<CategoryDto>>.Failure("Page number and page size must be greater than zero.");
        var page = await _categoryRepository.GetPagedAsync(pageNumber, pageSize, cancellationToken);
        return Result<PaginatedResult<CategoryDto>>.Success(new(page.Items.Select(Map), page.TotalCount, page.PageNumber, page.PageSize));
    }

    public async Task<Result<CategoryDto>> UpdateAsync(long id, UpdateCategoryRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return Result<CategoryDto>.Failure("Category name is required.");
        var category = await _categoryRepository.GetByIdAsync(id, cancellationToken);
        if (category is null) return Result<CategoryDto>.Failure("Category not found.");
        category.Name = request.Name.Trim();
        category.Description = Normalize(request.Description);
        category.UpdatedAt = DateTimeOffset.UtcNow;
        _categoryRepository.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<CategoryDto>.Success(Map(category));
    }

    public async Task<Result> SetActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default)
    {
        var category = await _categoryRepository.GetByIdAsync(id, cancellationToken);
        if (category is null) return Result.Failure("Category not found.");
        category.IsActive = isActive;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        _categoryRepository.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static CategoryDto Map(Category category) => new()
    {
        Id = category.Id, Name = category.Name, Description = category.Description, IsActive = category.IsActive,
        CreatedAt = category.CreatedAt, UpdatedAt = category.UpdatedAt
    };
}
