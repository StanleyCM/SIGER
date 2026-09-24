using SIGER.Application.Base;
using SIGER.Application.DTOs.Products;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Exceptions;

namespace SIGER.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ProductService(IProductRepository productRepository, ICategoryRepository categoryRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProductDto>> CreateAsync(CreateProductRequestDto request, CancellationToken cancellationToken = default)
    {
        var error = Validate(request.Name, request.Price);
        if (error is not null) return Result<ProductDto>.Failure(error);
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category is null) return Result<ProductDto>.Failure("Category not found.");
        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            CategoryId = category.Id, Category = category, Name = request.Name.Trim(), Description = Normalize(request.Description),
            Price = request.Price, IsAvailable = request.IsAvailable, ImageUrl = Normalize(request.ImageUrl),
            CreatedAt = now, UpdatedAt = now, Version = 0
        };
        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ProductDto>.Success(Map(product));
    }

    public async Task<Result<ProductDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(id, cancellationToken);
        return product is null ? Result<ProductDto>.Failure("Product not found.") : Result<ProductDto>.Success(Map(product));
    }

    public async Task<Result<PaginatedResult<ProductDto>>> GetPagedAsync(int pageNumber, int pageSize, long? categoryId = null, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1) return Result<PaginatedResult<ProductDto>>.Failure("Page number and page size must be greater than zero.");
        var page = await _productRepository.GetPagedAsync(pageNumber, pageSize, categoryId, cancellationToken);
        return Result<PaginatedResult<ProductDto>>.Success(new(page.Items.Select(Map), page.TotalCount, page.PageNumber, page.PageSize));
    }

    public async Task<Result<IReadOnlyCollection<ProductDto>>> GetAvailableAsync(long? categoryId = null, CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetAvailableAsync(categoryId, cancellationToken);
        return Result<IReadOnlyCollection<ProductDto>>.Success(products.Select(Map).ToArray());
    }

    public async Task<Result<ProductDto>> UpdateAsync(long id, UpdateProductRequestDto request, CancellationToken cancellationToken = default)
    {
        var error = Validate(request.Name, request.Price);
        if (error is not null) return Result<ProductDto>.Failure(error);
        var product = await _productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null) return Result<ProductDto>.Failure("Product not found.");
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category is null) return Result<ProductDto>.Failure("Category not found.");
        if (product.Version != request.Version) throw new BusinessRuleException("The product changed. Reload it and try again.");
        product.CategoryId = category.Id;
        product.Category = category;
        product.Name = request.Name.Trim();
        product.Description = Normalize(request.Description);
        product.Price = request.Price;
        product.ImageUrl = Normalize(request.ImageUrl);
        product.UpdatedAt = DateTimeOffset.UtcNow;
        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ProductDto>.Success(Map(product));
    }

    public async Task<Result> ChangeAvailabilityAsync(long id, UpdateProductAvailabilityRequestDto request, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null) return Result.Failure("Product not found.");
        if (product.Version != request.Version) throw new BusinessRuleException("The product changed. Reload it and try again.");
        product.IsAvailable = request.IsAvailable;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static string? Validate(string name, decimal price)
        => string.IsNullOrWhiteSpace(name) ? "Product name is required." : price < 0 ? "Product price cannot be negative." : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ProductDto Map(Product product) => new()
    {
        Id = product.Id, CategoryId = product.CategoryId, CategoryName = product.Category?.Name ?? string.Empty,
        Name = product.Name, Description = product.Description, Price = product.Price, IsAvailable = product.IsAvailable,
        ImageUrl = product.ImageUrl, CreatedAt = product.CreatedAt, UpdatedAt = product.UpdatedAt, Version = product.Version
    };
}
