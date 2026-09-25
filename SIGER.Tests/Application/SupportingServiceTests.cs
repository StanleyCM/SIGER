using Moq;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Promotions;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.DTOs.Reports;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Tests.Application;

public class SupportingServiceTests
{
    private readonly ServiceFixture f = new();
    private static readonly DateTimeOffset Future = DateTimeOffset.UtcNow.AddDays(10);
    private static CreateReservationRequestDto ReservationRequest() => new() { UserId = 1, TableId = 4, NumberOfPeople = 2, ReservationDateTime = Future, Notes = " window " };
    private static CreatePromotionRequestDto PromotionRequest() => new() { Name = " Lunch ", DiscountPercentage = 15, StartDate = Future, EndDate = Future.AddDays(1), ProductIds = [3, 3] };

    [Fact]
    public async Task Reservation_create_update_cancel_and_lookup()
    {
        var created = await f.ReservationService.CreateAsync(ReservationRequest(), f.Token);
        Assert.Equal(ReservationStatus.Pending, created.Value!.Status);
        Assert.Equal(7, created.Value.TableNumber); Assert.Equal("window", created.Value.Notes);
        var entity = new Reservation { Id = 8, UserId = 1, User = f.User, TableId = 4, Table = f.Table };
        f.Reservations.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(entity);
        var updated = await f.ReservationService.UpdateAsync(8, new() { UserId = 1, TableId = 4, NumberOfPeople = 3, ReservationDateTime = Future }, f.Token);
        Assert.Equal(3, updated.Value!.NumberOfPeople);
        Assert.True((await f.ReservationService.ChangeStatusAsync(8, new() { Status = ReservationStatus.Cancelled }, f.Token)).IsSuccess);
        Assert.Equal(ReservationStatus.Cancelled, (await f.ReservationService.GetByIdAsync(8, f.Token)).Value!.Status);
    }

    [Theory]
    [InlineData("user")] [InlineData("table")] [InlineData("people")] [InlineData("date")]
    public async Task Reservation_rejects_invalid_input(string scenario)
    {
        var request = ReservationRequest();
        if (scenario == "user") request.UserId = 99;
        if (scenario == "table") request.TableId = 99;
        if (scenario == "people") request.NumberOfPeople = 0;
        if (scenario == "date") request.ReservationDateTime = DateTimeOffset.UtcNow.AddDays(-1);
        Assert.True((await f.ReservationService.CreateAsync(request, f.Token)).IsFailure);
        f.Reservations.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(new Reservation { Id = 8 });
        Assert.True((await f.ReservationService.UpdateAsync(8, new() { UserId = request.UserId, TableId = request.TableId,
            NumberOfPeople = request.NumberOfPeople, ReservationDateTime = request.ReservationDateTime }, f.Token)).IsFailure);
        f.NoSave();
    }

    [Fact]
    public async Task Missing_reservations_are_failures()
    {
        Assert.True((await f.ReservationService.UpdateAsync(99, new() { NumberOfPeople = 2, ReservationDateTime = Future })).IsFailure);
        Assert.True((await f.ReservationService.ChangeStatusAsync(99, new())).IsFailure); f.NoSave();
    }

    [Fact]
    public async Task Promotion_create_deduplicates_products_and_uses_transaction()
    {
        f.Promotions.Setup(x => x.AddAsync(It.IsAny<Promotion>(), f.Token)).Callback<Promotion, CancellationToken>((p, _) =>
        { Assert.True(f.InTransaction); Assert.Single(p.PromotionProducts); }).Returns(Task.CompletedTask);
        var result = await f.PromotionService.CreateAsync(PromotionRequest(), f.Token);
        Assert.True(result.IsSuccess); Assert.Equal("Lunch", result.Value!.Name);
        Assert.Equal("Soup", Assert.Single(result.Value.Products).ProductName);
        f.Promotions.Verify(x => x.AddProductAsync(It.IsAny<PromotionProduct>(), f.Token), Times.Once);
    }

    [Theory]
    [InlineData("name")] [InlineData("zero")] [InlineData("large")] [InlineData("dates")] [InlineData("product")]
    public async Task Promotion_rejects_invalid_input(string scenario)
    {
        var request = PromotionRequest();
        if (scenario == "name") request.Name = "";
        if (scenario == "zero") request.DiscountPercentage = 0;
        if (scenario == "large") request.DiscountPercentage = 101;
        if (scenario == "dates") request.EndDate = request.StartDate.AddDays(-1);
        if (scenario == "product") request.ProductIds = [99];
        Assert.True((await f.PromotionService.CreateAsync(request, f.Token)).IsFailure); f.NoSave();
    }

    [Fact]
    public async Task Promotion_update_toggle_add_remove_and_duplicate_validation()
    {
        var promotion = new Promotion { Id = 8, Name = "Lunch" };
        f.Promotions.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(promotion);
        // Simulate the state returned by a subsequent repository read (EF relationship fixup).
        f.Promotions.Setup(x => x.AddProductAsync(It.IsAny<PromotionProduct>(), f.Token))
            .Callback<PromotionProduct, CancellationToken>((link, _) => promotion.PromotionProducts.Add(link)).Returns(Task.CompletedTask);
        f.Promotions.Setup(x => x.RemoveProductAsync(8, 3, f.Token))
            .Callback(() => promotion.PromotionProducts.Clear()).Returns(Task.CompletedTask);
        Assert.True((await f.PromotionService.UpdateAsync(8, new() { Name = "Dinner", DiscountPercentage = 25,
            StartDate = Future, EndDate = Future.AddDays(1) }, f.Token)).IsSuccess);
        Assert.True((await f.PromotionService.SetActiveAsync(8, false, f.Token)).IsSuccess); Assert.False(promotion.IsActive);
        Assert.True((await f.PromotionService.SetActiveAsync(8, true, f.Token)).IsSuccess);
        Assert.True((await f.PromotionService.AddProductAsync(8, 3, f.Token)).IsSuccess);
        Assert.True((await f.PromotionService.AddProductAsync(8, 3, f.Token)).IsFailure);
        Assert.True((await f.PromotionService.RemoveProductAsync(8, 3, f.Token)).IsSuccess);
        Assert.Empty(promotion.PromotionProducts);
        Assert.True((await f.PromotionService.RemoveProductAsync(8, 3, f.Token)).IsFailure);
        Assert.True((await f.PromotionService.AddProductAsync(8, 99, f.Token)).IsFailure);
        Assert.Equal("Dinner", (await f.PromotionService.GetByIdAsync(8, f.Token)).Value!.Name);
        f.Promotions.Setup(x => x.GetActiveAsync(Future, f.Token)).ReturnsAsync([promotion]);
        Assert.Single((await f.PromotionService.GetActiveAsync(Future, f.Token)).Value!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Promotion_equal_start_and_end_is_rejected_before_persistence(bool update)
    {
        var request = PromotionRequest();
        request.EndDate = request.StartDate;
        f.Promotions.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(new Promotion { Id = 8 });
        var result = update
            ? await f.PromotionService.UpdateAsync(8, new() { Name = request.Name, DiscountPercentage = request.DiscountPercentage,
                StartDate = request.StartDate, EndDate = request.EndDate }, f.Token)
            : await f.PromotionService.CreateAsync(request, f.Token);
        Assert.True(result.IsFailure);
        f.NoSave();
    }

    [Fact]
    public async Task Missing_promotions_and_invalid_updates_do_not_save()
    {
        Assert.True((await f.PromotionService.UpdateAsync(99, new() { Name = "N", DiscountPercentage = 1, StartDate = Future, EndDate = Future })).IsFailure);
        Assert.True((await f.PromotionService.UpdateAsync(99, new())).IsFailure);
        Assert.True((await f.PromotionService.SetActiveAsync(99, true)).IsFailure);
        Assert.True((await f.PromotionService.AddProductAsync(99, 3)).IsFailure);
        Assert.True((await f.PromotionService.RemoveProductAsync(99, 3)).IsFailure);
        f.NoSave();
    }

    [Fact]
    public async Task Category_lifecycle_and_validation()
    {
        var created = await f.CategoryService.CreateAsync(new() { Name = " Food ", Description = " warm " }, f.Token);
        Assert.Equal("Food", created.Value!.Name); Assert.Equal("warm", created.Value.Description); Assert.True(created.Value.IsActive);
        Assert.Equal("Drinks", (await f.CategoryService.UpdateAsync(2, new() { Name = " Drinks " }, f.Token)).Value!.Name);
        Assert.True((await f.CategoryService.SetActiveAsync(2, false, f.Token)).IsSuccess); Assert.False(f.Category.IsActive);
        Assert.True((await f.CategoryService.CreateAsync(new())).IsFailure);
        Assert.True((await f.CategoryService.UpdateAsync(2, new())).IsFailure);
        Assert.True((await f.CategoryService.UpdateAsync(99, new() { Name = "N" })).IsFailure);
        Assert.True((await f.CategoryService.SetActiveAsync(99, false)).IsFailure);
    }

    [Fact]
    public async Task Reports_delegate_filters_limit_and_cancellation()
    {
        var report = new SalesReportDto { TotalOrders = 3, TotalRevenue = 75m };
        IReadOnlyCollection<TopSellingProductDto> products = [new() { ProductId = 3, QuantitySold = 6, Revenue = 75m }];
        f.Reports.Setup(x => x.GetSalesReportAsync(Future, Future, f.Token)).ReturnsAsync(report);
        f.Reports.Setup(x => x.GetTopSellingProductsAsync(Future, Future, 7, f.Token)).ReturnsAsync(products);
        Assert.Same(report, (await f.ReportService.GetSalesReportAsync(Future, Future, f.Token)).Value);
        Assert.Same(products, (await f.ReportService.GetTopSellingProductsAsync(Future, Future, 7, f.Token)).Value);
        Assert.True((await f.ReportService.GetSalesReportAsync(Future, Future.AddDays(-1))).IsFailure);
        Assert.True((await f.ReportService.GetTopSellingProductsAsync(Future, Future.AddDays(-1))).IsFailure);
        Assert.True((await f.ReportService.GetTopSellingProductsAsync(Future, Future, 0)).IsFailure);
        f.Reports.VerifyAll();
    }

    [Fact]
    public async Task Audit_mapping_preserves_optional_actor_and_payload()
    {
        var audit = new Audit { Id = 8, User = f.User, UserId = 1, Entity = "Order", EntityId = 5, Action = "Updated", PreviousData = "{}", NewData = "{}", IpAddress = "127.0.0.1" };
        f.Audits.Setup(x => x.GetByIdAsync(8, f.Token)).ReturnsAsync(audit);
        var result = (await f.AuditService.GetByIdAsync(8, f.Token)).Value!;
        Assert.Equal("Ana Diaz", result.UserName); Assert.Equal("{}", result.PreviousData); Assert.Equal(5, result.EntityId);
        audit.User = null;
        Assert.Null((await f.AuditService.GetByIdAsync(8, f.Token)).Value!.UserName);
        Assert.True((await f.AuditService.GetPagedAsync(1, 10, startDate: Future, endDate: Future.AddDays(-1))).IsFailure);
        Assert.True((await f.OrderService.GetPagedAsync(1, 10, startDate: Future, endDate: Future.AddDays(-1))).IsFailure);
    }

    [Theory]
    [InlineData(-1, 1, 1)] [InlineData(0, 0, 1)] [InlineData(0, 1, 0)]
    public void Pagination_rejects_invalid_metadata(int total, int page, int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaginatedResult<int>([], total, page, size));

    [Fact]
    public void Result_and_pagination_contracts()
    {
        var result = Result<int>.Failure([" ", "first", "second"]);
        Assert.True(result.IsFailure); Assert.False(result.IsSuccess);
        Assert.Equal("first", result.Error); Assert.Equal(2, result.Errors.Count);
        Assert.True(Result.Success().IsSuccess); Assert.Equal(4, Result<int>.Success(4).Value);
        var page = new PaginatedResult<int>([1, 2], 5, 2, 2);
        Assert.Equal(3, page.TotalPages); Assert.Equal(2, page.PageNumber); Assert.Equal(5, page.TotalCount);
    }
}
