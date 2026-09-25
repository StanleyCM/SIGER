using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Moq;
using SIGER.Application.DTOs.Auth;
using SIGER.Application.Exceptions;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Application.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Infrastructure.Persistence;
using SIGER.Infrastructure.Repositories;
using Work = SIGER.Infrastructure.UnitOfWork.UnitOfWork;

namespace SIGER.Tests.Infrastructure;

public class BusinessAuditTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Create_commits_audit_with_generated_ID_and_actor(bool sync)
    {
        using var f = new Fixture(); f.Actor.UserId = 1; f.Actor.IpAddress = "127.0.0.1";
        var category = new Category { Name = "SECRET-TEXT", IsActive = true }; f.Db.Add(category);
        if (sync) f.Db.SaveChanges(); else await f.Db.SaveChangesAsync();
        var audit = Assert.Single(await f.Db.Audits.ToArrayAsync());
        Assert.True(category.Id > 0); Assert.Equal(category.Id, audit.EntityId); Assert.Equal("Create", audit.Action);
        Assert.Equal("Category", audit.Entity); Assert.Equal(1, audit.UserId); Assert.Equal("127.0.0.1", audit.IpAddress);
        Assert.Null(audit.PreviousData); Assert.True(audit.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
        using var json = JsonDocument.Parse(audit.NewData!);
        Assert.Equal(category.Id, json.RootElement.GetProperty("Values").GetProperty("Id").GetInt64());
        Assert.DoesNotContain("SECRET-TEXT", audit.NewData);
    }

    [Fact]
    public async Task Update_captures_original_and_new_price_without_navigation_cycles()
    {
        using var f = new Fixture(); var p = new Product { CategoryId = 1, Name = "Product", Price = 10, Version = 1 };
        f.Db.Add(p); await f.Db.SaveChangesAsync(); p.Price = 15; await f.Db.SaveChangesAsync();
        var a = await f.Db.Audits.SingleAsync(a => a.Action == "Update");
        Assert.Contains("\"Price\":10", a.PreviousData); Assert.Contains("\"Price\":15", a.NewData);
        Assert.Contains("\"ChangedFields\":[\"Price\"]", a.NewData); Assert.DoesNotContain("Category\":", a.NewData);
    }

    [Theory]
    [InlineData(typeof(User), "IsActive", true, "Activate")]
    [InlineData(typeof(User), "IsActive", false, "Deactivate")]
    [InlineData(typeof(Category), "IsActive", true, "Activate")]
    [InlineData(typeof(Product), "IsAvailable", true, "AvailabilityChange")]
    [InlineData(typeof(Promotion), "IsActive", true, "Activate")]
    public async Task Status_mutations_have_consistent_actions(Type type, string property, bool value, string action)
    {
        using var f = new Fixture(); var entity = Activator.CreateInstance(type)!;
        type.GetProperty(property)!.SetValue(entity, !value); f.Db.Add(entity); await f.Db.SaveChangesAsync();
        type.GetProperty(property)!.SetValue(entity, value); await f.Db.SaveChangesAsync();
        var a = await f.Db.Audits.SingleAsync(a => a.Action == action);
        Assert.Equal(type.Name, a.Entity); Assert.Null(a.UserId); Assert.Null(a.IpAddress);
    }

    [Theory]
    [InlineData(OrderStatus.InPreparation, "StatusChange")]
    [InlineData(OrderStatus.Ready, "StatusChange")]
    [InlineData(OrderStatus.Served, "StatusChange")]
    [InlineData(OrderStatus.Cancelled, "Cancel")]
    public async Task Kitchen_waiter_and_cancellation_keep_transition_values(OrderStatus status, string action)
    {
        using var f = new Fixture(); var order = new Order { Status = OrderStatus.Pending }; f.Db.Add(order); await f.Db.SaveChangesAsync();
        order.Status = status; await f.Db.SaveChangesAsync();
        var a = await f.Db.Audits.SingleAsync(a => a.Action == action);
        Assert.Contains("Pending", a.PreviousData); Assert.Contains(status.ToString(), a.NewData);
    }

    [Fact]
    public async Task Payment_completion_and_detail_change_are_audited()
    {
        using var f = new Fixture(); f.Db.Add(new Payment { OrderId = 7, UserId = 1, Amount = 20, Status = PaymentStatus.Completed });
        var d = new OrderDetail { OrderId = 7, ProductId = 8, UnitPrice = 10, Quantity = 2, Subtotal = 20 };
        f.Db.Add(d); await f.Db.SaveChangesAsync(); d.Quantity = 3; d.Subtotal = 30; await f.Db.SaveChangesAsync();
        var payment = await f.Db.Audits.SingleAsync(a => a.Action == "Pay"); Assert.Contains("\"Amount\":20", payment.NewData);
        var detail = await f.Db.Audits.SingleAsync(a => a.Entity == "OrderDetail" && a.Action == "Update");
        Assert.Contains("\"Quantity\":2", detail.PreviousData); Assert.Contains("\"Quantity\":3", detail.NewData);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed, "StatusChange")]
    [InlineData(ReservationStatus.Cancelled, "Cancel")]
    [InlineData(ReservationStatus.Completed, "StatusChange")]
    public async Task Reservation_transitions_are_audited(ReservationStatus status, string action)
    {
        using var f = new Fixture(); var r = new Reservation { Status = ReservationStatus.Pending, NumberOfPeople = 2 };
        f.Db.Add(r); await f.Db.SaveChangesAsync(); r.Status = status; await f.Db.SaveChangesAsync();
        Assert.Contains(await f.Db.Audits.ToArrayAsync(), a => a.Action == action && a.NewData!.Contains(status.ToString()));
    }

    [Fact]
    public async Task Composite_association_and_physical_delete_preserve_keys()
    {
        using var f = new Fixture(); var join = new PromotionProduct { ProductId = 12, PromotionId = 13 };
        f.Db.Add(join); await f.Db.SaveChangesAsync(); f.Db.Remove(join); await f.Db.SaveChangesAsync();
        var a = await f.Db.Audits.SingleAsync(a => a.Action == "Delete"); Assert.Null(a.EntityId); Assert.Null(a.NewData);
        Assert.Contains("\"ProductId\":12", a.PreviousData); Assert.Contains("\"PromotionId\":13", a.PreviousData);
    }

    [Fact]
    public async Task Profile_free_text_and_secrets_are_never_serialized()
    {
        using var f = new Fixture();
        const string secret = "password JWT refresh_token Authorization sb_secret_ ConnectionString PRIVATE";
        var user = new User { Email = secret, FirstName = secret, LastName = secret, Phone = secret, AuthUserId = Guid.NewGuid() };
        f.Db.Add(user); f.Db.Add(new Product { Name = secret, Description = secret, ImageUrl = secret });
        f.Db.Add(new Payment { Reference = secret }); f.Db.Add(new Order { Notes = secret });
        await f.Db.SaveChangesAsync(); user.Email = "changed@example.test"; await f.Db.SaveChangesAsync();
        foreach (var a in await f.Db.Audits.ToArrayAsync())
        {
            var text = a.PreviousData + a.NewData; Assert.DoesNotContain(secret, text);
            Assert.DoesNotContain(user.AuthUserId.ToString(), text); Assert.DoesNotContain("changed@example.test", text);
        }
        Assert.Contains(await f.Db.Audits.ToArrayAsync(), a => a.Action == "EmailChange" && a.NewData!.Contains("Email"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Audit_failure_rolls_back_business_even_if_outer_transaction_catches(bool outer)
    {
        using var f = new Fixture(); await using var tx = outer ? await f.Db.Database.BeginTransactionAsync() : null;
        f.Commands.FailAudit = true;
        f.Db.Add(new Category { Name = "temporary" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        if (tx is not null) await tx.CommitAsync();
        Assert.Empty(await f.Db.Categories.ToArrayAsync());
        Assert.Empty(await f.Db.Audits.ToArrayAsync());
    }

    [Fact]
    public async Task Business_failure_leaves_no_success_audit()
    {
        using var f = new Fixture(); f.Db.Add(new Category { Name = null! });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.Empty(await f.Db.Audits.ToArrayAsync()); Assert.Empty(await f.Db.Categories.ToArrayAsync());
    }

    [Fact]
    public async Task Outer_rollback_removes_business_and_audit()
    {
        using var f = new Fixture(); await using var tx = await f.Db.Database.BeginTransactionAsync();
        f.Db.Add(new Category { Name = "temporary" }); await f.Db.SaveChangesAsync();
        Assert.Single(await f.Db.Audits.AsNoTracking().ToArrayAsync()); await tx.RollbackAsync();
        Assert.Empty(await f.Db.Categories.AsNoTracking().ToArrayAsync()); Assert.Empty(await f.Db.Audits.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Queries_and_noop_saves_do_not_generate_audit_and_false_preserves_tracking()
    {
        using var f = new Fixture(); var c = new Category { Name = "safe" }; f.Db.Add(c);
        await f.Db.SaveChangesAsync(false); Assert.Equal(EntityState.Added, f.Db.Entry(c).State);
        f.Db.ChangeTracker.AcceptAllChanges(); await f.Db.Categories.ToArrayAsync(); await f.Db.SaveChangesAsync();
        Assert.Single(await f.Db.Audits.ToArrayAsync());
    }

    [Fact]
    public async Task Many_changes_are_batched_not_one_audit_command_per_entity()
    {
        using var f = new Fixture(); f.Db.AddRange(Enumerable.Range(1, 130).Select(i => new Category { Name = "safe" + i }));
        await f.Db.SaveChangesAsync(); Assert.Equal(130, await f.Db.Audits.CountAsync()); Assert.Equal(3, f.Commands.AuditInserts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Auth_creation_and_audit_commit_together_or_compensate(bool failAudit)
    {
        using var f = new Fixture();
        f.Commands.FailAudit = failAudit;
        var role = new Role { Id = 1, IsActive = true, Name = "Mesero" }; var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).Callback<User, CancellationToken>((u, _) => f.Db.Add(u)).Returns(Task.CompletedTask);
        AuthUserStateDto? remote = null; var provider = new Mock<IAuthProvider>();
        provider.Setup(p => p.CreateUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, Guid op, string email, string _, CancellationToken _) => remote = new(id, email, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, op));
        provider.Setup(p => p.GetUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => remote);
        provider.Setup(p => p.DeleteCreatedUserAsync(It.IsAny<AuthUserStateDto>(), It.IsAny<CancellationToken>())).Callback(() => remote = null).Returns(Task.CompletedTask);
        var service = new UserService(users.Object, roles.Object, provider.Object, new Work(f.Db), Mock.Of<IUserOperationReporter>());
        var request = new SIGER.Application.DTOs.Users.CreateUserRequestDto { RoleId = 1, FirstName = "Test", LastName = "Only", Email = "audit@example.test", Password = "PRIVATE" };
        if (failAudit)
        {
            var error = await Assert.ThrowsAsync<UserOperationException>(() => service.CreateAsync(request));
            Assert.False(error.CompensationFailed); Assert.Null(remote); Assert.Empty(await f.Db.Users.ToArrayAsync());
            Assert.Empty(await f.Db.Audits.ToArrayAsync());
        }
        else
        {
            Assert.True((await service.CreateAsync(request)).IsSuccess); Assert.NotNull(remote);
            Assert.Single(await f.Db.Users.ToArrayAsync()); var a = Assert.Single(await f.Db.Audits.ToArrayAsync());
            Assert.Equal("User", a.Entity); Assert.Equal("Create", a.Action); Assert.DoesNotContain("PRIVATE", a.NewData);
        }
    }

    private sealed class Actor : IAuditActor { public long? UserId { get; set; } public string? IpAddress { get; set; } }

    [Fact]
    public async Task Table_administrative_status_has_previous_and_current_state()
    {
        using var f = new Fixture(); var table = new Table { Number = 1, Capacity = 4, Status = TableStatus.Available };
        f.Db.Add(table); await f.Db.SaveChangesAsync(); table.Status = TableStatus.OutOfService; await f.Db.SaveChangesAsync();
        var a = await f.Db.Audits.SingleAsync(a => a.Action == "StatusChange");
        Assert.Contains("Available", a.PreviousData); Assert.Contains("OutOfService", a.NewData);
    }

    [Theory]
    [InlineData(null)] [InlineData("not-an-ip")]
    public async Task Missing_or_invalid_address_is_nullable(string? address)
    {
        using var f = new Fixture(); f.Actor.IpAddress = address; f.Db.Add(new Category { Name = "safe" });
        await f.Db.SaveChangesAsync(); Assert.Null((await f.Db.Audits.SingleAsync()).IpAddress);
    }

    [Fact]
    public async Task Invalid_actor_is_rejected_before_any_write()
    {
        using var f = new Fixture(); f.Actor.UserId = -1; f.Db.Add(new Category { Name = "safe" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        Assert.Empty(await f.Db.Audits.ToArrayAsync()); Assert.Empty(await f.Db.Categories.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public void Synchronous_audit_failure_rolls_back_business()
    {
        using var f = new Fixture(); f.Commands.FailAudit = true; f.Db.Add(new Category { Name = "safe" });
        Assert.Throws<InvalidOperationException>(() => f.Db.SaveChanges());
        Assert.Empty(f.Db.Audits.ToArray()); Assert.Empty(f.Db.Categories.ToArray());
    }

    private sealed class Counter : DbCommandInterceptor
    {
        public int AuditInserts;
        public bool FailAudit;
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"auditoria\"") && FailAudit) throw new InvalidOperationException("Injected audit failure.");
            return result;
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"auditoria\""))
            {
                AuditInserts++;
                if (FailAudit) throw new InvalidOperationException("Injected audit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=False");
        public Actor Actor { get; } = new(); public Counter Commands { get; } = new();
        public TestContext Db { get; }
        public Fixture()
        {
            connection.Open();
            Db = new TestContext(new DbContextOptionsBuilder<SIGERDbContext>().UseSqlite(connection).AddInterceptors(Commands).Options, Actor);
            Db.Database.EnsureCreated(); // Isolated SQLite only. Never calls Supabase or uses secrets.
        }
        public void Dispose() { Db.Dispose(); connection.Dispose(); }
    }
    private sealed class TestContext(DbContextOptions<SIGERDbContext> options, IAuditActor actor) : SIGERDbContext(options, actor)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            // Adapt provider-specific storage for transactional tests; production Npgsql model remains tested separately.
            foreach (var e in b.Model.GetEntityTypes())
            foreach (var p in e.GetProperties())
            {
                if (p.ClrType == typeof(long)) p.SetColumnType("INTEGER");
                if (p.ClrType == typeof(DateTimeOffset)) p.SetColumnType("TEXT");
            }
            b.Entity<Audit>().Property(a => a.IpAddress).HasConversion(new ValueConverter<string?, string?>(v => v, v => v)).HasColumnType("TEXT");
        }
    }
}
