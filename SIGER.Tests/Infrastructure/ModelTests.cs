using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIGER.Domain.Entities;
using SIGER.Infrastructure.DependencyInjection;
using SIGER.Infrastructure.Persistence;
using SIGER.Infrastructure.Repositories;

namespace SIGER.Tests.Infrastructure;

// Uses the real Npgsql model. Save interception below prevents ALL database I/O.
internal sealed class ModelFixture : IDisposable
{
    private static readonly ServiceProvider Provider = CreateProvider();
    private readonly IServiceScope scope;
    public SIGERDbContext Context { get; }
    public ModelFixture()
    {
        scope = Provider.CreateScope();
        Context = scope.ServiceProvider.GetRequiredService<SIGERDbContext>();
    }
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SIGERDatabase"] = "Host=127.0.0.1;Port=1;Database=never_connect;Username=test",
            ["Supabase:Url"] = "https://test.invalid", ["Supabase:ServiceRoleKey"] = "synthetic-test-only"
        }).Build();
        services.AddDbContext<SIGERDbContext>(options => options.AddInterceptors(new NoDatabaseSave())
            .ReplaceService<IDbContextTransactionManager, NoDatabaseTransactions>());
        services.AddInfrastructure(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    public void Dispose() => scope.Dispose();
    private sealed class NoDatabaseSave : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result) => InterceptionResult<int>.SuppressWithResult(0);
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default) =>
            ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
    }

    // Model-only tests suppress writes, so their new enclosing audit transaction must also avoid IO.
    private sealed class NoDatabaseTransactions : IDbContextTransactionManager
    {
        public IDbContextTransaction? CurrentTransaction { get; private set; }
        public IDbContextTransaction BeginTransaction()
        {
            var tx = new Mock<IDbContextTransaction>();
            tx.SetupGet(x => x.SupportsSavepoints).Returns(true);
            tx.Setup(x => x.Dispose()).Callback(() => CurrentTransaction = null);
            tx.Setup(x => x.DisposeAsync()).Callback(() => CurrentTransaction = null).Returns(ValueTask.CompletedTask);
            return CurrentTransaction = tx.Object;
        }
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken token = default) => Task.FromResult(BeginTransaction());
        public void CommitTransaction() { }
        public Task CommitTransactionAsync(CancellationToken token = default) => Task.CompletedTask;
        public void RollbackTransaction() { }
        public Task RollbackTransactionAsync(CancellationToken token = default) => Task.CompletedTask;
        public void ResetState() => CurrentTransaction = null;
        public Task ResetStateAsync(CancellationToken token = default) { ResetState(); return Task.CompletedTask; }
    }
}

public class ModelTests
{

    [Fact]
    public void Monetary_precision_is_explicit()
    {
        using var f = new ModelFixture();
        var model = f.Context.Model;
        foreach (var (type, field) in new[] { (typeof(Product), nameof(Product.Price)), (typeof(OrderDetail), nameof(OrderDetail.UnitPrice)),
            (typeof(OrderDetail), nameof(OrderDetail.Subtotal)), (typeof(Order), nameof(Order.Total)), (typeof(Payment), nameof(Payment.Amount)) })
        {
            var property = model.FindEntityType(type)!.FindProperty(field)!;
            Assert.Equal(12, property.GetPrecision()); Assert.Equal(2, property.GetScale());
        }
    }

    [Theory]
    [InlineData(typeof(Product))] [InlineData(typeof(Table))] [InlineData(typeof(Order))]
    public async Task Version_initializes_and_increments_without_discarding_original(Type type)
    {
        using var f = new ModelFixture(); var context = f.Context;
        var entity = Activator.CreateInstance(type)!;
        type.GetProperty("Id")!.SetValue(entity, 100L);
        context.Add(entity);
        await context.SaveChangesAsync();
        var version = context.Entry(entity).Property("Version");
        Assert.Equal(1L, version.CurrentValue); Assert.True(version.Metadata.IsConcurrencyToken);
        context.Entry(entity).State = EntityState.Unchanged;
        version.OriginalValue = 5L; version.CurrentValue = 5L;
        context.Entry(entity).State = EntityState.Modified;
        await context.SaveChangesAsync();
        Assert.Equal(6L, version.CurrentValue); Assert.Equal(5L, version.OriginalValue);
        Assert.Equal("bigint", version.Metadata.GetColumnType());
    }

    [Fact]
    public void Updating_tracked_order_keeps_catalog_and_user_unchanged_and_tracks_item_changes()
    {
        using var f = new ModelFixture(); var c = f.Context;
        var user = new User { Id = 1, Role = new Role { Id = 1 } };
        var product = new Product { Id = 2, Category = new Category { Id = 3 }, Version = 5 };
        var order = new Order { Id = 4, User = user, Table = new Table { Id = 5, Version = 2 }, Version = 3 };
        var detail = new OrderDetail { Id = 6, Order = order, Product = product, Quantity = 1 };
        order.Details.Add(detail); c.Attach(order);
        detail.Quantity = 2;
        new OrderRepository(c).Update(order); c.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Modified, c.Entry(order).State);
        Assert.Equal(EntityState.Modified, c.Entry(detail).State);
        Assert.Equal(EntityState.Unchanged, c.Entry(product).State);
        Assert.Equal(EntityState.Unchanged, c.Entry(user).State);
        Assert.Equal(EntityState.Unchanged, c.Entry(order.Table).State);
        order.Details.Remove(detail); c.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Deleted, c.Entry(detail).State);
        order.Details.Add(new OrderDetail { Product = product, Quantity = 1 });
        c.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Added, c.Entry(Assert.Single(order.Details)).State);
    }
}
