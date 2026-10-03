using Moq;
using SIGER.Application.Services;
using SIGER.Domain.Entities;

namespace SIGER.Tests.Application;

public class PublicCatalogTests
{
    [Fact]
    public async Task Only_active_categories_are_exposed()
    {
        var f = new ServiceFixture();
        f.Categories.Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([f.Category, new Category { Id = 99, IsActive = false }]);
        var service = new PublicCatalogService(f.Categories.Object, f.Products.Object, f.Promotions.Object, TimeProvider.System);
        Assert.Equal(2, Assert.Single((await service.GetCategoriesAsync()).Value!).Id);
    }

    [Theory]
    [InlineData(true, true, true)] [InlineData(false, true, false)] [InlineData(true, false, false)]
    public async Task Product_lookup_requires_available_product_and_active_category(bool available, bool active, bool expected)
    {
        var f = new ServiceFixture(); f.Product.IsAvailable = available; f.Category.IsActive = active;
        var service = new PublicCatalogService(f.Categories.Object, f.Products.Object, f.Promotions.Object, TimeProvider.System);
        Assert.Equal(expected, (await service.GetProductAsync(3)).IsSuccess);
        Assert.True((await service.GetProductAsync(99)).IsFailure);
    }

    [Fact]
    public async Task Promotions_are_current_informational_and_only_reference_public_products()
    {
        var f = new ServiceFixture(); var now = DateTimeOffset.UtcNow;
        var current = new Promotion { Id = 1, IsActive = true, StartDate = now.AddHours(-1), EndDate = now.AddHours(1), DiscountPercentage = 10 };
        current.PromotionProducts.Add(new() { ProductId = 3, Product = f.Product });
        current.PromotionProducts.Add(new() { ProductId = 9, Product = new Product { IsAvailable = false } });
        var expired = new Promotion { Id = 2, IsActive = true, StartDate = now.AddDays(-2), EndDate = now.AddDays(-1) };
        f.Promotions.Setup(x => x.GetActiveAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([current, expired]);
        var service = new PublicCatalogService(f.Categories.Object, f.Products.Object, f.Promotions.Object, TimeProvider.System);
        var promo = Assert.Single((await service.GetPromotionsAsync()).Value!);
        Assert.Equal(1, promo.Id); Assert.Equal(new long[] { 3 }, promo.ProductIds);
        Assert.Equal(12.50m, f.Product.Price);
    }
}
