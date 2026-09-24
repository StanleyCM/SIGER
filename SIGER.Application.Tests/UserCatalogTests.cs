using System.Text.Json;
using Moq;
using SIGER.Application.DTOs.Auth;
using SIGER.Application.DTOs.Users;
using SIGER.Application.DTOs.Products;
using SIGER.Application.DTOs.Tables;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Domain.Exceptions;

namespace SIGER.Application.Tests;

public class UserCatalogTests
{
    private readonly ServiceFixture f = new();
    private static CreateUserRequestDto UserRequest() => new() { RoleId = 1, FirstName = " Ana ", LastName = " Diaz ",
        Email = " ana@example.test ", Password = "synthetic-test-input", Phone = " 123 " };

    [Fact]
    public async Task User_create_normalizes_and_never_exposes_password()
    {
        f.Users.Setup(x => x.AddAsync(It.IsAny<User>(), f.Token)).Callback<User, CancellationToken>((u, _) =>
        {
            Assert.Equal(f.User.AuthUserId, u.AuthUserId); Assert.Equal("ana@example.test", u.Email);
            Assert.True(u.IsActive); Assert.Equal("123", u.Phone);
        }).Returns(Task.CompletedTask);
        var result = await f.UserService.CreateAsync(UserRequest(), f.Token);
        Assert.True(result.IsSuccess); Assert.Equal("Ana", result.Value!.FirstName);
        Assert.Equal("Waiter", result.Value.RoleName);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("Password", json); Assert.DoesNotContain("synthetic-test-input", json);
        f.Provider.Verify(x => x.CreateUserAsync("ana@example.test", "synthetic-test-input", f.Token), Times.Once);
        f.Work.Verify(x => x.SaveChangesAsync(f.Token), Times.Once);
    }

    [Theory]
    [InlineData("role")] [InlineData("inactive")] [InlineData("duplicate")] [InlineData("first")]
    [InlineData("last")] [InlineData("email")] [InlineData("password")] [InlineData("null-password")]
    public async Task Invalid_user_is_not_created(string scenario)
    {
        var request = UserRequest();
        switch (scenario)
        {
            case "role": request.RoleId = 99; break;
            case "inactive": f.Role.IsActive = false; break;
            case "duplicate": f.Users.Setup(x => x.GetByEmailAsync("ana@example.test", f.Token)).ReturnsAsync(f.User); break;
            case "first": request.FirstName = ""; break;
            case "last": request.LastName = ""; break;
            case "email": request.Email = "invalid"; break;
            case "password": request.Password = " "; break;
            case "null-password": request.Password = null!; break;
        }
        Assert.True((await f.UserService.CreateAsync(request, f.Token)).IsFailure);
        f.NoSave();
        f.Provider.Verify(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remote_creation_failure_prevents_local_write()
    {
        f.Provider.Setup(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), f.Token)).ThrowsAsync(new HttpRequestException());
        await Assert.ThrowsAsync<HttpRequestException>(() => f.UserService.CreateAsync(UserRequest(), f.Token));
        f.Users.Verify(x => x.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never); f.NoSave();
    }

    [Fact]
    public async Task Update_and_status_delegate_and_map()
    {
        var result = await f.UserService.UpdateAsync(1, new() { FirstName = " Eva ", LastName = " Cruz ",
            Email = f.User.Email, RoleId = 1 }, f.Token);
        Assert.Equal("Eva", result.Value!.FirstName); Assert.Null(result.Value.Phone);
        Assert.True((await f.UserService.UpdateStatusAsync(1, new() { IsActive = false }, f.Token)).IsSuccess);
        Assert.False(f.User.IsActive);
        Assert.True((await f.UserService.UpdateStatusAsync(1, new() { IsActive = true }, f.Token)).IsSuccess);
        f.Provider.Verify(x => x.DisableUserAsync(f.User.AuthUserId, f.Token), Times.Once);
        f.Provider.Verify(x => x.EnableUserAsync(f.User.AuthUserId, f.Token), Times.Once);
        f.Roles.Setup(x => x.GetAllActiveAsync(f.Token)).ReturnsAsync([f.Role]);
        Assert.Equal("Waiter", Assert.Single((await f.UserService.GetAvailableRolesAsync(f.Token)).Value!).Name);
    }

    [Theory]
    [InlineData("invalid")] [InlineData("missing")] [InlineData("role")] [InlineData("duplicate")]
    public async Task Invalid_user_update_does_not_save(string scenario)
    {
        var request = new UpdateUserRequestDto { FirstName = "Ana", LastName = "Diaz", Email = f.User.Email, RoleId = 1 };
        long id = 1;
        if (scenario == "invalid") request.Email = "";
        if (scenario == "missing") id = 99;
        if (scenario == "role") request.RoleId = 99;
        if (scenario == "duplicate") f.Users.Setup(x => x.GetByEmailAsync(request.Email, f.Token)).ReturnsAsync(new User { Id = 99 });
        Assert.True((await f.UserService.UpdateAsync(id, request, f.Token)).IsFailure); f.NoSave();
        Assert.True((await f.UserService.UpdateStatusAsync(99, new())).IsFailure);
    }

    [Theory]
    [InlineData("valid")] [InlineData("credentials")] [InlineData("missing")] [InlineData("inactive")] [InlineData("inactive-role")] [InlineData("empty")]
    public async Task Auth_validates_local_identity_and_preserves_provider_token(string scenario)
    {
        var session = new AuthSessionDto { AuthUserId = f.User.AuthUserId, AccessToken = "synthetic-opaque-token", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        f.Provider.Setup(x => x.SignInAsync("ana@example.test", "input", f.Token)).ReturnsAsync(session);
        if (scenario == "credentials") f.Provider.Setup(x => x.SignInAsync("ana@example.test", "input", f.Token)).ReturnsAsync((AuthSessionDto?)null);
        if (scenario == "missing") session.AuthUserId = Guid.NewGuid();
        if (scenario == "inactive") f.User.IsActive = false;
        if (scenario == "inactive-role") f.Role.IsActive = false;
        var result = await f.AuthService.LoginAsync(new() { Email = scenario == "empty" ? "" : " ana@example.test ", Password = "input" }, f.Token);
        if (scenario == "valid")
        {
            Assert.True(result.IsSuccess); Assert.Equal(session.AccessToken, result.Value!.AccessToken);
            Assert.Equal(session.ExpiresAt, result.Value.ExpiresAt);
            Assert.DoesNotContain("Password", JsonSerializer.Serialize(result.Value));
        }
        else Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Product_create_update_and_availability()
    {
        var created = await f.ProductService.CreateAsync(new() { CategoryId = 2, Name = " Soup ", Price = 12.50m,
            Description = " warm ", ImageUrl = " image " }, f.Token);
        Assert.True(created.IsSuccess); Assert.Equal("Food", created.Value!.CategoryName);
        Assert.Equal("warm", created.Value.Description); Assert.Equal("image", created.Value.ImageUrl);
        var updated = await f.ProductService.UpdateAsync(3, new() { CategoryId = 2, Name = "Other", Price = 20m, Version = 5 }, f.Token);
        Assert.Equal(20m, updated.Value!.Price);
        Assert.True((await f.ProductService.ChangeAvailabilityAsync(3, new() { Version = 5, IsAvailable = false }, f.Token)).IsSuccess);
        Assert.False(f.Product.IsAvailable);
        f.Products.Setup(x => x.GetAvailableAsync(2, f.Token)).ReturnsAsync([f.Product]);
        Assert.Equal("Other", Assert.Single((await f.ProductService.GetAvailableAsync(2, f.Token)).Value!).Name);
    }

    [Theory]
    [InlineData("", 1, 2)] [InlineData("Soup", -1, 2)] [InlineData("Soup", 1, 99)]
    public async Task Product_rejects_bad_name_price_or_category(string name, int price, long category)
    {
        Assert.True((await f.ProductService.CreateAsync(new() { Name = name, Price = price, CategoryId = category })).IsFailure);
        Assert.True((await f.ProductService.UpdateAsync(3, new() { Name = name, Price = price, CategoryId = category, Version = 5 })).IsFailure);
        f.NoSave();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stale_product_version_cannot_overwrite_current_record(bool availability)
    {
        if (availability)
            await Assert.ThrowsAsync<BusinessRuleException>(() => f.ProductService.ChangeAvailabilityAsync(3, new() { Version = 4 }));
        else
            await Assert.ThrowsAsync<BusinessRuleException>(() => f.ProductService.UpdateAsync(3,
                new() { CategoryId = 2, Name = "Changed", Price = 1m, Version = 4 }));
        Assert.Equal("Soup", f.Product.Name); Assert.True(f.Product.IsAvailable); Assert.Equal(5, f.Product.Version);
        f.NoSave();
    }

    [Fact]
    public async Task Table_create_update_and_status()
    {
        var created = await f.TableService.CreateAsync(new() { Number = 2, Capacity = 4, Location = " patio " }, f.Token);
        Assert.Equal("patio", created.Value!.Location);
        var updated = await f.TableService.UpdateAsync(4, new() { Number = 8, Capacity = 6, Version = 5 }, f.Token);
        Assert.Equal(6, updated.Value!.Capacity);
        Assert.True((await f.TableService.ChangeStatusAsync(4, new() { Status = TableStatus.Reserved, Version = 5 }, f.Token)).IsSuccess);
        Assert.Equal(TableStatus.Reserved, f.Table.Status);
        f.Tables.Setup(x => x.GetAvailableAsync(f.Token)).ReturnsAsync([f.Table]);
        Assert.Equal(8, Assert.Single((await f.TableService.GetAvailableAsync(f.Token)).Value!).Number);
    }

    [Theory]
    [InlineData(0, 2)] [InlineData(1, 0)] [InlineData(1, -1)]
    public async Task Table_rejects_invalid_number_or_capacity(int number, int capacity)
    {
        Assert.True((await f.TableService.CreateAsync(new() { Number = number, Capacity = capacity })).IsFailure);
        Assert.True((await f.TableService.UpdateAsync(4, new() { Number = number, Capacity = capacity, Version = 5 })).IsFailure);
        f.NoSave();
    }

    [Fact]
    public async Task Missing_catalog_records_return_failure()
    {
        Assert.True((await f.ProductService.UpdateAsync(99, new() { Name = "Soup", CategoryId = 2, Price = 1 })).IsFailure);
        Assert.True((await f.ProductService.ChangeAvailabilityAsync(99, new())).IsFailure);
        Assert.True((await f.TableService.UpdateAsync(99, new() { Number = 1, Capacity = 2 })).IsFailure);
        Assert.True((await f.TableService.ChangeStatusAsync(99, new())).IsFailure);
        f.NoSave();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stale_table_version_is_rejected_before_mutation(bool status)
    {
        if (status)
            await Assert.ThrowsAsync<BusinessRuleException>(() => f.TableService.ChangeStatusAsync(4, new() { Version = 4, Status = TableStatus.Reserved }));
        else
            await Assert.ThrowsAsync<BusinessRuleException>(() => f.TableService.UpdateAsync(4, new() { Number = 99, Capacity = 2, Version = 4 }));
        Assert.Equal(7, f.Table.Number); Assert.Equal(TableStatus.Available, f.Table.Status); f.NoSave();
    }
}

