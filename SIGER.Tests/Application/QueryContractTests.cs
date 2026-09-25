using Moq;
using SIGER.Application.Base;
using SIGER.Domain.Entities;
namespace SIGER.Tests.Application;
public class QueryContractTests
{

    [Fact]
    public async Task User_queries_map_pagination_and_reject_invalid_pages()
    {
        var f = new ServiceFixture();
        var entity = f.User;
        f.Users.Setup(x => x.GetPagedAsync(2, 10, null, null, f.Token)).ReturnsAsync(new PaginatedResult<User>([entity], 21, 2, 10));
        var page = (await f.UserService.GetPagedAsync(2, 10, null, null, f.Token)).Value!;
        Assert.Equal(21, page.TotalCount); Assert.Equal(3, page.TotalPages); Assert.Equal(2, page.PageNumber);
        Assert.Equal(1, Assert.Single(page.Items).Id);
        f.Users.Verify(x => x.GetPagedAsync(2, 10, null, null, f.Token), Times.Once);
        Assert.True((await f.UserService.GetPagedAsync(0, 10)).IsFailure);
        Assert.True((await f.UserService.GetPagedAsync(1, 0)).IsFailure);
        f.Users.Setup(x => x.GetByIdAsync(1, f.Token)).ReturnsAsync(entity);
        Assert.Equal(1, (await f.UserService.GetByIdAsync(1, f.Token)).Value!.Id);
        Assert.True((await f.UserService.GetByIdAsync(999, f.Token)).IsFailure);
    }

    [Fact]
    public async Task Category_queries_map_pagination_and_reject_invalid_pages()
    {
        var f = new ServiceFixture();
        var entity = f.Category;
        f.Categories.Setup(x => x.GetPagedAsync(2, 10, f.Token)).ReturnsAsync(new PaginatedResult<Category>([entity], 21, 2, 10));
        var page = (await f.CategoryService.GetPagedAsync(2, 10, f.Token)).Value!;
        Assert.Equal(21, page.TotalCount); Assert.Equal(3, page.TotalPages); Assert.Equal(2, page.PageNumber);
        Assert.Equal(2, Assert.Single(page.Items).Id);
        f.Categories.Verify(x => x.GetPagedAsync(2, 10, f.Token), Times.Once);
        Assert.True((await f.CategoryService.GetPagedAsync(0, 10)).IsFailure);
        Assert.True((await f.CategoryService.GetPagedAsync(1, 0)).IsFailure);
        f.Categories.Setup(x => x.GetByIdAsync(2, f.Token)).ReturnsAsync(entity);
        Assert.Equal(2, (await f.CategoryService.GetByIdAsync(2, f.Token)).Value!.Id);
        Assert.True((await f.CategoryService.GetByIdAsync(999, f.Token)).IsFailure);
    }

    [Fact]
    public async Task Product_queries_map_pagination_and_reject_invalid_pages()
    {
        var f = new ServiceFixture();
        var entity = f.Product;
        f.Products.Setup(x => x.GetPagedAsync(2, 10, 2, f.Token)).ReturnsAsync(new PaginatedResult<Product>([entity], 21, 2, 10));
        var page = (await f.ProductService.GetPagedAsync(2, 10, 2, f.Token)).Value!;
        Assert.Equal(21, page.TotalCount); Assert.Equal(3, page.TotalPages); Assert.Equal(2, page.PageNumber);
        Assert.Equal(3, Assert.Single(page.Items).Id);
        f.Products.Verify(x => x.GetPagedAsync(2, 10, 2, f.Token), Times.Once);
        Assert.True((await f.ProductService.GetPagedAsync(0, 10)).IsFailure);
        Assert.True((await f.ProductService.GetPagedAsync(1, 0)).IsFailure);
        f.Products.Setup(x => x.GetByIdAsync(3, f.Token)).ReturnsAsync(entity);
        Assert.Equal(3, (await f.ProductService.GetByIdAsync(3, f.Token)).Value!.Id);
        Assert.True((await f.ProductService.GetByIdAsync(999, f.Token)).IsFailure);
    }

    [Fact]
    public async Task Table_queries_map_pagination_and_reject_invalid_pages()
    {
        var f = new ServiceFixture();
        var entity = f.Table;
        f.Tables.Setup(x => x.GetPagedAsync(2, 10, f.Token)).ReturnsAsync(new PaginatedResult<Table>([entity], 21, 2, 10));
        var page = (await f.TableService.GetPagedAsync(2, 10, f.Token)).Value!;
        Assert.Equal(21, page.TotalCount); Assert.Equal(3, page.TotalPages); Assert.Equal(2, page.PageNumber);
        Assert.Equal(4, Assert.Single(page.Items).Id);
        f.Tables.Verify(x => x.GetPagedAsync(2, 10, f.Token), Times.Once);
        Assert.True((await f.TableService.GetPagedAsync(0, 10)).IsFailure);
        Assert.True((await f.TableService.GetPagedAsync(1, 0)).IsFailure);
        f.Tables.Setup(x => x.GetByIdAsync(4, f.Token)).ReturnsAsync(entity);
        Assert.Equal(4, (await f.TableService.GetByIdAsync(4, f.Token)).Value!.Id);
        Assert.True((await f.TableService.GetByIdAsync(999, f.Token)).IsFailure);
    }

    [Fact]
    public async Task Order_queries_map_pagination_and_reject_invalid_pages()
    {
        var f = new ServiceFixture();
        var entity = f.Order;
        f.Orders.Setup(x => x.GetPagedAsync(2, 10, null, null, null, null, null, f.Token)).ReturnsAsync(new PaginatedResult<Order>([entity], 21, 2, 10));
        var page = (await f.OrderService.GetPagedAsync(2, 10, null, null, null, null, null, f.Token)).Value!;
        Assert.Equal(21, page.TotalCount); Assert.Equal(3, page.TotalPages); Assert.Equal(2, page.PageNumber);
        Assert.Equal(5, Assert.Single(page.Items).Id);
        f.Orders.Verify(x => x.GetPagedAsync(2, 10, null, null, null, null, null, f.Token), Times.Once);
        Assert.True((await f.OrderService.GetPagedAsync(0, 10)).IsFailure);
        Assert.True((await f.OrderService.GetPagedAsync(1, 0)).IsFailure);
        f.Orders.Setup(x => x.GetWithDetailsAsync(5, f.Token)).ReturnsAsync(entity);
        Assert.Equal(5, (await f.OrderService.GetByIdAsync(5, f.Token)).Value!.Id);
        Assert.True((await f.OrderService.GetByIdAsync(999, f.Token)).IsFailure);
    }

    [Fact]
    public async Task Reservation_queries_map_pagination_and_reject_invalid_pages()
    {
        var f = new ServiceFixture();
        var entity = new Reservation { Id = 8, Table = f.Table };
        f.Reservations.Setup(x => x.GetPagedAsync(2, 10, null, null, null, f.Token)).ReturnsAsync(new PaginatedResult<Reservation>([entity], 21, 2, 10));
        var page = (await f.ReservationService.GetPagedAsync(2, 10, null, null, null, f.Token)).Value!;
        Assert.Equal(21, page.TotalCount); Assert.Equal(3, page.TotalPages); Assert.Equal(2, page.PageNumber);
        Assert.Equal(8, Assert.Single(page.Items).Id);
        f.Reservations.Verify(x => x.GetPagedAsync(2, 10, null, null, null, f.Token), Times.Once);
        Assert.True((await f.ReservationService.GetPagedAsync(0, 10)).IsFailure);
        Assert.True((await f.ReservationService.GetPagedAsync(1, 0)).IsFailure);
        f.Reservations.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(entity);
        Assert.Equal(8, (await f.ReservationService.GetByIdAsync(8, f.Token)).Value!.Id);
        Assert.True((await f.ReservationService.GetByIdAsync(999, f.Token)).IsFailure);
    }

    [Fact]
    public async Task Promotion_queries_map_pagination_and_reject_invalid_pages()
    {
        var f = new ServiceFixture();
        var entity = new Promotion { Id = 8 };
        f.Promotions.Setup(x => x.GetPagedAsync(2, 10, f.Token)).ReturnsAsync(new PaginatedResult<Promotion>([entity], 21, 2, 10));
        var page = (await f.PromotionService.GetPagedAsync(2, 10, f.Token)).Value!;
        Assert.Equal(21, page.TotalCount); Assert.Equal(3, page.TotalPages); Assert.Equal(2, page.PageNumber);
        Assert.Equal(8, Assert.Single(page.Items).Id);
        f.Promotions.Verify(x => x.GetPagedAsync(2, 10, f.Token), Times.Once);
        Assert.True((await f.PromotionService.GetPagedAsync(0, 10)).IsFailure);
        Assert.True((await f.PromotionService.GetPagedAsync(1, 0)).IsFailure);
        f.Promotions.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(entity);
        Assert.Equal(8, (await f.PromotionService.GetByIdAsync(8, f.Token)).Value!.Id);
        Assert.True((await f.PromotionService.GetByIdAsync(999, f.Token)).IsFailure);
    }

    [Fact]
    public async Task Audit_queries_map_pagination_and_reject_invalid_pages()
    {
        var f = new ServiceFixture();
        var entity = new Audit { Id = 8 };
        f.Audits.Setup(x => x.GetPagedAsync(2, 10, null, null, null, null, f.Token)).ReturnsAsync(new PaginatedResult<Audit>([entity], 21, 2, 10));
        var page = (await f.AuditService.GetPagedAsync(2, 10, null, null, null, null, f.Token)).Value!;
        Assert.Equal(21, page.TotalCount); Assert.Equal(3, page.TotalPages); Assert.Equal(2, page.PageNumber);
        Assert.Equal(8, Assert.Single(page.Items).Id);
        f.Audits.Verify(x => x.GetPagedAsync(2, 10, null, null, null, null, f.Token), Times.Once);
        Assert.True((await f.AuditService.GetPagedAsync(0, 10)).IsFailure);
        Assert.True((await f.AuditService.GetPagedAsync(1, 0)).IsFailure);
        f.Audits.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(entity);
        Assert.Equal(8, (await f.AuditService.GetByIdAsync(8, f.Token)).Value!.Id);
        Assert.True((await f.AuditService.GetByIdAsync(999, f.Token)).IsFailure);
    }
}
