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

}
