using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIGER.Domain.Entities;
using SIGER.Infrastructure.DependencyInjection;
using SIGER.Infrastructure.Persistence;
using SIGER.Infrastructure.Repositories;

namespace SIGER.Infrastructure.Tests;

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
        services.AddDbContext<SIGERDbContext>(options => options.AddInterceptors(new NoDatabaseSave()));
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
}

public class ModelTests
{
    [Fact]
    public void Available_products_join_uses_existing_category_estado_column()
    {
        using var f = new ModelFixture();
        var category = f.Context.Model.FindEntityType(typeof(Category))!;
        Assert.Equal("categoria", category.GetTableName());
        var isActive = category.FindProperty(nameof(Category.IsActive))!;
        Assert.Equal("estado", isActive.GetColumnName());
        Assert.Equal("boolean", isActive.GetColumnType());
        Assert.False(isActive.IsNullable);

        var sql = f.Context.Products.AsNoTracking().Include(product => product.Category)
            .Where(product => product.IsAvailable).OrderBy(product => product.Name).ToQueryString();
        Assert.Contains("JOIN categoria AS c", sql);
        Assert.Contains("c.estado", sql);
        Assert.DoesNotContain("c.activo", sql);
        Assert.Equal(System.Data.ConnectionState.Closed, f.Context.Database.GetDbConnection().State);
    }

    [Fact]
    public void Repeated_scopes_reuse_the_same_EF_model()
    {
        using var first = new ModelFixture();
        using var second = new ModelFixture();
        Assert.Same(first.Context.Model, second.Context.Model);
    }

    [Theory]
    [InlineData(typeof(Role), "rol", "id_rol")]
    [InlineData(typeof(User), "usuario", "id_usuario")]
    [InlineData(typeof(Category), "categoria", "id_categoria")]
    [InlineData(typeof(Product), "producto", "id_producto")]
    [InlineData(typeof(Table), "mesa", "id_mesa")]
    [InlineData(typeof(Order), "orden", "id_orden")]
    [InlineData(typeof(OrderDetail), "detalle_orden", "id_detalle")]
    [InlineData(typeof(Payment), "pago", "id_pago")]
    [InlineData(typeof(Reservation), "reserva", "id_reserva")]
    [InlineData(typeof(Promotion), "promocion", "id_promocion")]
    [InlineData(typeof(Audit), "auditoria", "id_auditoria")]
    public void Tables_identity_keys_and_physical_columns(Type type, string table, string key)
    {
        using var f = new ModelFixture();
        Assert.Equal(12, f.Context.Model.GetEntityTypes().Count());
        var entity = f.Context.Model.FindEntityType(type)!;
        Assert.Equal(table, entity.GetTableName());
        var property = Assert.Single(entity.FindPrimaryKey()!.Properties);
        Assert.Equal("Id", property.Name); Assert.Equal(typeof(long), property.ClrType);
        Assert.Equal(key, property.GetColumnName()); Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
        Assert.All(entity.GetProperties(), p => Assert.NotEqual(p.Name, p.GetColumnName()));
        Assert.Equal(System.Data.ConnectionState.Closed, f.Context.Database.GetDbConnection().State);
    }

    [Fact]
    public void Composite_key_and_delete_rules_match_relational_model()
    {
        using var f = new ModelFixture(); var model = f.Context.Model;
        var join = model.FindEntityType(typeof(PromotionProduct))!;
        Assert.Equal("promocion_producto", join.GetTableName());
        Assert.Equal(["PromotionId", "ProductId"], join.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Null(join.FindProperty("Id"));
        Assert.All(join.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior));
        Assert.Equal(DeleteBehavior.SetNull, Assert.Single(model.FindEntityType(typeof(Audit))!.GetForeignKeys()).DeleteBehavior);
        foreach (var fk in model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            var expected = fk.DeclaringEntityType.ClrType == typeof(PromotionProduct) ||
                           (fk.DeclaringEntityType.ClrType == typeof(OrderDetail) && fk.PrincipalEntityType.ClrType == typeof(Order))
                ? DeleteBehavior.Cascade : fk.DeclaringEntityType.ClrType == typeof(Audit) ? DeleteBehavior.SetNull : DeleteBehavior.Restrict;
            Assert.Equal(expected, fk.DeleteBehavior);
            Assert.StartsWith("fk_", fk.GetConstraintName());
        }
        var order = model.FindEntityType(typeof(Order))!;
        Assert.True(order.FindProperty("TableId")!.IsNullable); Assert.True(order.FindProperty("ClientId")!.IsNullable);
        Assert.False(order.FindProperty("UserId")!.IsNullable);
        Assert.Equal("CreatedOrders", order.FindNavigation("User")!.Inverse!.Name);
        Assert.Equal("ClientOrders", order.FindNavigation("Client")!.Inverse!.Name);
    }

    [Theory]
    [InlineData(typeof(Product), "Price", 10, 2)]
    [InlineData(typeof(OrderDetail), "UnitPrice", 10, 2)]
    [InlineData(typeof(OrderDetail), "Subtotal", 12, 2)]
    [InlineData(typeof(Order), "Total", 12, 2)]
    [InlineData(typeof(Payment), "Amount", 12, 2)]
    [InlineData(typeof(Promotion), "DiscountPercentage", 5, 2)]
    public void Monetary_precision_is_explicit(Type type, string name, int precision, int scale)
    {
        using var f = new ModelFixture(); var p = f.Context.Model.FindEntityType(type)!.FindProperty(name)!;
        Assert.Equal(precision, p.GetPrecision()); Assert.Equal(scale, p.GetScale());
    }

    [Fact]
    public void PostgreSql_specific_types_lengths_and_indexes()
    {
        using var f = new ModelFixture(); var model = f.Context.Model;
        var audit = model.FindEntityType(typeof(Audit))!;
        Assert.Equal("jsonb", audit.FindProperty("PreviousData")!.GetColumnType());
        Assert.Equal("jsonb", audit.FindProperty("NewData")!.GetColumnType());
        var ip = audit.FindProperty("IpAddress")!;
        Assert.Equal("inet", ip.GetColumnType());
        Assert.Equal(System.Net.IPAddress.Loopback, ip.GetValueConverter()!.ConvertToProvider("127.0.0.1"));
        Assert.Null(ip.GetValueConverter()!.ConvertToProvider(null));
        Assert.All(model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => p.ClrType == typeof(DateTimeOffset)),
            p => Assert.Equal("timestamp with time zone", p.GetColumnType()));
        Assert.Equal(150, model.FindEntityType(typeof(User))!.FindProperty("Email")!.GetMaxLength());
        Assert.Equal(500, model.FindEntityType(typeof(Product))!.FindProperty("ImageUrl")!.GetMaxLength());
        Assert.Equal(255, model.FindEntityType(typeof(OrderDetail))!.FindProperty("Note")!.GetMaxLength());
        Assert.Equal(5, model.GetEntityTypes().SelectMany(e => e.GetIndexes()).Count(i => i.IsUnique));
        Assert.All(model.GetEntityTypes().SelectMany(e => e.GetIndexes()), i => Assert.False(string.IsNullOrWhiteSpace(i.GetDatabaseName())));
    }

    [Fact]
    public void Native_enums_use_documented_spanish_labels()
    {
        using var f = new ModelFixture();
        var model = f.Context.GetService<IDesignTimeModel>().Model;
        var enums = model.GetPostgresEnums().ToDictionary(e => e.Name, e => e.Labels);
        Assert.Equal(7, enums.Count);
        Assert.Equal(new[] { "Disponible", "Ocupada", "Reservada", "FueraServicio" }.Order(), enums["estado_mesa"].Order());
        Assert.Equal(new[] { "Pendiente", "EnPreparacion", "Lista", "Servida", "Pagada", "Cancelada" }.Order(), enums["estado_orden"].Order());
        Assert.Equal(new[] { "Efectivo", "Tarjeta", "Transferencia", "Otro" }.Order(), enums["metodo_pago"].Order());
        Assert.Equal(new[] { "Pendiente", "Completado", "Fallido", "Reembolsado" }.Order(), enums["estado_pago"].Order());
        Assert.Equal(new[] { "Desktop", "Web" }.Order(), enums["origen_orden"].Order());
        Assert.Equal(new[] { "Mesa", "ParaLlevar" }.Order(), enums["tipo_orden"].Order());
        Assert.Equal(new[] { "Pendiente", "Confirmada", "Cancelada", "Completada" }.Order(), enums["estado_reserva"].Order());
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
