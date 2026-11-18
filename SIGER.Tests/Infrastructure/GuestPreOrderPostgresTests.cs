using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Payments;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Application.Services;
using SIGER.Domain.Enums;
using SIGER.Domain.Exceptions;
using SIGER.Infrastructure.DependencyInjection;
using SIGER.Infrastructure.Persistence;
using SIGER.Tests.Application;

namespace SIGER.Tests.Infrastructure;

// Opt-in real PostgreSQL tests. Explicit loopback-only connection, never reads API secrets.
public sealed class LocalPostgresFactAttribute : FactAttribute
{
    public LocalPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIGER_TEST_POSTGRES")))
            Skip = "Set SIGER_TEST_POSTGRES to a disposable local PostgreSQL server to run SQL and concurrency tests.";
    }
}

public sealed class GuestPreOrderPostgresTests : IAsyncLifetime
{
    private string adminConnection = "";
    private string connection = "";
    private readonly string database = "siger_test_" + Guid.NewGuid().ToString("N");
    private ServiceProvider? provider;
    private readonly TestClock clock = new();
    private bool created;
    private static string Root => FindRoot();
    private static string Script => File.ReadAllText(Path.Combine(Root, "scripts/database/003_guest_reservations_preorders.sql"));
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset? Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now ?? DateTimeOffset.UtcNow;
    }
    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "SIGER.slnx"))) return d.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("SIGER_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(source)) return;
        var builder = new NpgsqlConnectionStringBuilder(source);
        if (builder.Host is not ("127.0.0.1" or "localhost" or "::1")) throw new InvalidOperationException("Tests require a local disposable server.");
        builder.Pooling = false; builder.Database = "postgres"; adminConnection = builder.ConnectionString;
        await using (var c = new NpgsqlConnection(adminConnection))
        {
            await c.OpenAsync(); await new NpgsqlCommand($"CREATE DATABASE {database}", c).ExecuteNonQueryAsync(); created = true;
        }
        builder.Database = database; connection = builder.ConnectionString;
        await Sql(File.ReadAllText(Path.Combine(Root, "SIGER.Tests/Fixtures/guest-preorder-baseline.sql")));
        await Sql(Script);
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SIGERDatabase"] = connection,
            ["Supabase:Url"] = "https://test.invalid", ["Supabase:ServiceRoleKey"] = "synthetic-test-only"
        }).Build());
        services.AddScoped<IGuestReservationService, GuestReservationService>();
        services.AddScoped<IPreOrderService, PreOrderService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IPublicCatalogService, PublicCatalogService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IKitchenService, KitchenService>();
        services.AddSingleton<TimeProvider>(clock);
        provider = services.BuildServiceProvider();
    }

    private async Task<object?> Sql(string sql)
    {
        await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
        return await new NpgsqlCommand(sql, c).ExecuteScalarAsync();
    }
    private IServiceScope Scope() => provider!.CreateScope();
    private static CreateGuestReservationRequestDto Request(DateTimeOffset? date = null) => new()
    {
        Name = "PRIVATE-NAME", Phone = "584121234567", Email = "private@example.invalid", Notes = "PRIVATE-NOTES",
        NumberOfPeople = 2, ReservationDateTime = date ?? GuestFixture.NoonInSantoDomingo(1)
    };
    private async Task<GuestReservationCreatedDto> Reserve(DateTimeOffset? date = null)
    {
        using var scope = Scope(); var result = await scope.ServiceProvider.GetRequiredService<IGuestReservationService>().CreateAsync(Request(date));
        Assert.True(result.IsSuccess, result.Error); return result.Value!;
    }
    private static CreatePreOrderRequestDto Items => new() { Items = [new() { ProductId = 1, Quantity = 2, Notes = "PRIVATE-ITEM" }] };

    [LocalPostgresFact]
    public async Task Script_is_atomic_repeatable_and_preserves_legacy_data_indexes_and_RLS()
    {
        var before = await Sql("SELECT count(*) FROM pg_indexes WHERE schemaname='public'");
        await Sql(Script);
        Assert.Equal(before, await Sql("SELECT count(*) FROM pg_indexes WHERE schemaname='public'"));
        Assert.Equal(1L, await Sql("SELECT count(*) FROM reserva WHERE id_usuario=1 AND nombre_contacto IS NULL"));
        Assert.Equal(1L, await Sql("SELECT count(*) FROM orden WHERE id_usuario=1 AND id_reserva IS NULL AND estado='Pagada'"));
        Assert.Equal(2L, await Sql("SELECT count(*) FROM pg_class WHERE relname IN ('reserva','orden') AND relrowsecurity"));
        Assert.Equal(2L, await Sql("SELECT count(*) FROM pg_indexes WHERE indexname IN ('idx_reserva_fecha','idx_reserva_mesa')"));
        Assert.Equal("YES", await Sql("SELECT is_nullable FROM information_schema.columns WHERE table_name='reserva' AND column_name='id_usuario'"));
        Assert.Equal("NO", await Sql("SELECT is_nullable FROM information_schema.columns WHERE table_name='reserva' AND column_name='id_mesa'"));
    }

    [LocalPostgresFact]
    public async Task Incompatible_existing_column_aborts_and_rolls_back_nullability_changes()
    {
        await Sql("ALTER TABLE reserva ALTER COLUMN id_usuario SET NOT NULL, ALTER COLUMN nombre_contacto TYPE varchar(149); ALTER TABLE orden ALTER COLUMN id_usuario SET NOT NULL;");
        Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => Sql(Script))).SqlState);
        Assert.Equal(2L, await Sql("SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name IN ('reserva','orden') AND column_name='id_usuario' AND is_nullable='NO'"));
        Assert.Equal(149, await Sql("SELECT character_maximum_length FROM information_schema.columns WHERE table_schema='public' AND table_name='reserva' AND column_name='nombre_contacto'"));
    }

    [LocalPostgresFact]
    public async Task Concurrent_guests_competing_for_one_table_create_only_one_reservation()
    {
        var when = GuestFixture.NoonInSantoDomingo(2);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = Scope(); await start.Task;
            return await scope.ServiceProvider.GetRequiredService<IGuestReservationService>().CreateAsync(Request(when));
        }).ToArray();
        start.SetResult(); var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, r => r.IsSuccess); Assert.Equal(7, results.Count(r => r.IsFailure));
        Assert.Equal(1L, await Sql("SELECT count(*) FROM reserva WHERE id_usuario IS NULL"));
        Assert.Equal("Disponible", await Sql("SELECT estado::text FROM mesa WHERE id_mesa=1"));
    }

    [LocalPostgresFact]
    public async Task Concurrent_preorders_are_unique_persist_after_reload_and_do_not_enter_kitchen()
    {
        var r = await Reserve(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = Scope(); await start.Task;
            return await scope.ServiceProvider.GetRequiredService<IPreOrderService>().CreateAsync(r.Reservation.Id, r.AccessToken, Items);
        }).ToArray();
        start.SetResult(); var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, x => x.IsSuccess); Assert.All(results.Where(x => x.IsFailure), x => Assert.Equal("A preorder already exists for this reservation.", x.Error));
        using var read = Scope();
        var saved = await read.ServiceProvider.GetRequiredService<IPreOrderService>().GetAsync(r.Reservation.Id, r.AccessToken);
        Assert.Equal(25m, saved.Value!.Total); Assert.Equal(OrderStatus.PreOrdered, saved.Value.Status);
        var orders = read.ServiceProvider.GetRequiredService<IOrderRepository>();
        Assert.Empty(await orders.GetKitchenOrdersAsync()); Assert.False(await orders.HasActiveOrdersAsync(1, 0));
        Assert.Equal("Disponible", await Sql("SELECT estado::text FROM mesa WHERE id_mesa=1"));
    }

    [LocalPostgresFact]
    public async Task Cancellation_is_transactional_and_preserves_occupied_table_and_history()
    {
        var r = await Reserve();
        using (var scope = Scope()) Assert.True((await scope.ServiceProvider.GetRequiredService<IPreOrderService>().CreateAsync(r.Reservation.Id, r.AccessToken, Items)).IsSuccess);
        await Sql("UPDATE mesa SET estado='Ocupada' WHERE id_mesa=1");
        using (var scope = Scope()) Assert.True((await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(r.Reservation.Id, new() { Status = ReservationStatus.Cancelled })).IsSuccess);
        Assert.Equal("Cancelada", await Sql("SELECT estado::text FROM orden WHERE id_reserva IS NOT NULL"));
        Assert.Equal("Ocupada", await Sql("SELECT estado::text FROM mesa WHERE id_mesa=1"));
        Assert.Equal(1L, await Sql("SELECT count(*) FROM detalle_orden"));
    }

    [LocalPostgresFact]
    public async Task Database_enforces_uniqueness_ownership_contact_and_credential_pairs()
    {
        var r = await Reserve();
        using (var scope = Scope()) Assert.True((await scope.ServiceProvider.GetRequiredService<IPreOrderService>().CreateAsync(r.Reservation.Id, r.AccessToken, Items)).IsSuccess);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Sql($"INSERT INTO orden(id_mesa,id_reserva,estado,origen,tipo) VALUES (1,{r.Reservation.Id},'Preordenada','Web','Mesa')"));
        Assert.Equal("23505", duplicate.SqlState);
        foreach (var sql in new[]
        {
            "INSERT INTO orden(id_mesa) VALUES (1)",
            "UPDATE orden SET estado='Pendiente' WHERE id_reserva IS NOT NULL",
            "UPDATE orden SET cuenta_solicitada=true WHERE id_reserva IS NOT NULL",
            "UPDATE reserva SET nombre_contacto=' ' WHERE id_usuario IS NULL",
            "UPDATE reserva SET telefono_contacto=NULL WHERE id_usuario IS NULL",
            "UPDATE reserva SET token_acceso_expira=NULL WHERE id_usuario IS NULL",
            "UPDATE reserva SET token_acceso_hash=NULL,token_acceso_expira=NULL WHERE id_usuario IS NULL"
        }) Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => Sql(sql))).SqlState);
        var restrictedDelete = await Assert.ThrowsAsync<PostgresException>(() => Sql("DELETE FROM reserva WHERE id_usuario IS NULL"));
        Assert.Contains(restrictedDelete.SqlState, new[] { "23503", "23001" });
        Assert.Equal("fk_orden_reserva", restrictedDelete.ConstraintName);
    }

    [LocalPostgresFact]
    public async Task Failed_edit_save_rolls_back_both_reservation_and_preorder()
    {
        var r = await Reserve();
        using (var s = Scope()) Assert.True((await s.ServiceProvider.GetRequiredService<IPreOrderService>().CreateAsync(r.Reservation.Id, r.AccessToken, Items)).IsSuccess);
        await Sql("INSERT INTO mesa(numero,capacidad) VALUES (2,12)");
        var before = await Sql("SELECT jsonb_build_object('r',(SELECT jsonb_agg(to_jsonb(r)) FROM reserva r),'o',(SELECT jsonb_agg(to_jsonb(o)) FROM orden o),'a',(SELECT jsonb_agg(to_jsonb(a)) FROM auditoria a))::text");
        // Failure injection only in this disposable local database, deferred until COMMIT.
        await Sql("CREATE FUNCTION reject_test_edit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test edit rollback'; END $$; CREATE CONSTRAINT TRIGGER reject_test_edit AFTER UPDATE ON reserva DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_test_edit();");
        using (var s = Scope()) await Assert.ThrowsAsync<PostgresException>(() => s.ServiceProvider.GetRequiredService<IGuestReservationService>().UpdateAsync(r.Reservation.Id, r.AccessToken,
            new() { NumberOfPeople = 8, Notes = "Must roll back" }));
        Assert.Equal(before, await Sql("SELECT jsonb_build_object('r',(SELECT jsonb_agg(to_jsonb(r)) FROM reserva r),'o',(SELECT jsonb_agg(to_jsonb(o)) FROM orden o),'a',(SELECT jsonb_agg(to_jsonb(a)) FROM auditoria a))::text"));
    }

    [LocalPostgresFact]
    public async Task Concurrent_edits_competing_for_one_slot_have_one_winner()
    {
        var date = GuestFixture.NoonInSantoDomingo(6);
        var first = await Reserve(date); var second = await Reserve(date.AddHours(2));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[] { first, second }.Select(async r =>
        {
            using var s = Scope(); await start.Task;
            return await s.ServiceProvider.GetRequiredService<IGuestReservationService>().UpdateAsync(r.Reservation.Id, r.AccessToken, new() { ReservationDateTime = date.AddDays(1) });
        }).ToArray();
        start.SetResult(); var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, x => x.IsSuccess); Assert.Single(results, x => x.IsFailure);
    }

    [LocalPostgresFact]
    public async Task Availability_uses_real_Postgres_overlap_status_capacity_and_contiguous_intervals()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
        var start = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-4)).ToUniversalTime();
        var r = await Reserve(start);
        async Task<IReadOnlyList<ReservationAvailabilityDto>> Read(DateOnly day, int people = 2, bool own = false)
        {
            using var scope = Scope();
            var result = await scope.ServiceProvider.GetRequiredService<IGuestReservationService>().GetAvailabilityAsync(
                new() { Date = day, NumberOfPeople = people, ReservationId = own ? r.Reservation.Id : null }, own ? r.AccessToken : null);
            Assert.True(result.IsSuccess, result.Error); return result.Value!;
        }
        foreach (var status in new[] { "Pendiente", "Confirmada", "Cancelada", "Completada" })
        {
            await Sql($"UPDATE reserva SET estado='{status}' WHERE id_usuario IS NULL");
            var slots = await Read(date);
            Assert.Equal(status == "Cancelada", slots.Single(s => s.Time == "12:00").Available);
            Assert.Equal(status == "Cancelada", slots.Single(s => s.Time == "13:30").Available);
            Assert.True(slots.Single(s => s.Time == "14:00").Available);
        }
        await Sql("UPDATE reserva SET estado='Pendiente' WHERE id_usuario IS NULL");
        Assert.True((await Read(date, own: true)).Single(s => s.Time == "12:00").Available);
        Assert.All(await Read(date.AddDays(1)), s => Assert.True(s.Available));
        Assert.All(await Read(date, 5), s => Assert.False(s.Available));
        Assert.True((await Read(date)).Single(s => s.Time == "18:00").Available);
        Assert.True((await Reserve(start.AddHours(2))).Reservation.Id > 0);
        // A competing write after the advisory read must still be rejected by creation.
        Assert.True((await Reserve(start.AddHours(6))).Reservation.Id > 0);
        using (var scope = Scope()) Assert.True((await scope.ServiceProvider.GetRequiredService<IGuestReservationService>().CreateAsync(Request(start.AddHours(6)))).IsFailure);
        await Sql("UPDATE mesa SET estado='FueraServicio' WHERE id_mesa=1");
        Assert.All(await Read(date.AddDays(1)), s => Assert.False(s.Available));
    }

    [LocalPostgresFact]
    public async Task Concurrent_staff_preorder_activations_have_one_winner_satisfy_checks_and_enter_kitchen()
    {
        var r = await Reserve();
        using (var s = Scope())
            Assert.True((await s.ServiceProvider.GetRequiredService<IPreOrderService>().CreateAsync(r.Reservation.Id, r.AccessToken, Items)).IsSuccess);
        clock.Now = r.Reservation.ReservationDateTime;

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = Scope(); await start.Task;
            return await scope.ServiceProvider.GetRequiredService<IReservationService>().ActivatePreOrderAsync(r.Reservation.Id, 1);
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        var winner = Assert.Single(results, x => x.IsSuccess).Value!;
        Assert.Equal(7, results.Count(x => x.IsFailure));
        Assert.Equal(OrderStatus.Pending, winner.Status);
        Assert.Equal(1L, winner.UserId);
        Assert.Equal(25m, winner.Total);
        Assert.Equal("Completada", await Sql($"SELECT estado::text FROM reserva WHERE id_reserva={r.Reservation.Id}"));
        Assert.Equal("Ocupada", await Sql("SELECT estado::text FROM mesa WHERE id_mesa=1"));
        Assert.Equal("Pendiente", await Sql($"SELECT estado::text FROM orden WHERE id_reserva={r.Reservation.Id}"));
        using var read = Scope();
        Assert.Single(await read.ServiceProvider.GetRequiredService<IOrderRepository>().GetKitchenOrdersAsync());
    }

    [LocalPostgresFact]
    public async Task Early_reception_cannot_displace_previous_booking_and_received_party_keeps_its_slot()
    {
        var start = GuestFixture.NoonInSantoDomingo(1);
        var earlier = await Reserve(start);
        var next = await Reserve(start.AddHours(2));
        clock.Now = start.AddHours(2).AddMinutes(-30);
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IReservationService>();
            Assert.Equal("The table already has an overlapping reservation.", (await service.ReceiveAsync(next.Reservation.Id, 1)).Error);
            Assert.True((await service.ChangeStatusAsync(earlier.Reservation.Id, new() { Status = ReservationStatus.Cancelled })).IsSuccess);
        }
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IReservationService>();
            Assert.True((await service.ReceiveAsync(next.Reservation.Id, 1)).IsSuccess);
        }
        using (var scope = Scope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
            Assert.True(await repository.HasOverlapAsync(1, start.AddMinutes(90), start.AddHours(2)));
            Assert.True(await repository.HasOverlapAsync(1, start.AddHours(2), start.AddHours(4)));
            Assert.False(await repository.HasOverlapAsync(1, start.AddHours(4), start.AddHours(6)));
        }
        using (var scope = Scope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var tables = scope.ServiceProvider.GetRequiredService<ITableRepository>();
            Assert.Empty(await tables.GetAvailableAsync());
            Assert.Equal("The selected table is not available.", (await orders.CreateOrderAsync(TableOrder())).Error);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var linked = Enumerable.Range(0, 2).Select(async _ =>
        {
            using var scope = Scope(); await gate.Task;
            return await scope.ServiceProvider.GetRequiredService<IOrderService>().CreateOrderAsync(TableOrder(next.Reservation.Id));
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(linked).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.Error == "An order already exists for this reservation.");
        Assert.Equal("Ocupada", await Sql("SELECT estado::text FROM mesa WHERE id_mesa=1"));
    }

    [LocalPostgresFact]
    public async Task Early_payment_is_rejected_while_cancellation_and_kitchen_compete()
    {
        long id;
        long version;
        using (var scope = Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IOrderService>().CreateOrderAsync(TableOrder());
            Assert.True(result.IsSuccess, result.Error);
            id = result.Value!.Id;
            version = result.Value.Version;
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Run(Func<IServiceProvider, Task<bool>> action)
        {
            using var scope = Scope();
            await gate.Task;
            try { return await action(scope.ServiceProvider); }
            catch (Exception e) when (e is DbUpdateConcurrencyException or BusinessRuleException) { return false; }
        }
        var payment = Run(async services => (await services.GetRequiredService<IPaymentService>().ProcessPaymentAsync(
            new ProcessPaymentRequestDto { OrderId = id, UserId = 1, Amount = 12.50m, Method = PaymentMethod.Cash })).IsSuccess);
        var cancel = Run(async services => (await services.GetRequiredService<IOrderService>().UpdateStatusAsync(
            id, new() { Status = OrderStatus.Cancelled, Version = version })).IsSuccess);
        var kitchen = Run(async services => (await services.GetRequiredService<IKitchenService>().MarkInPreparationAsync(id)).IsSuccess);
        gate.SetResult();
        await Task.WhenAll(payment, cancel, kitchen).WaitAsync(TimeSpan.FromSeconds(20));

        var status = await Sql($"SELECT estado::text FROM orden WHERE id_orden={id}");
        var payments = await Sql($"SELECT count(*) FROM pago WHERE id_orden={id}");
        Assert.False(await payment);
        Assert.NotEqual(await cancel, await kitchen);
        Assert.Equal(await cancel ? "Cancelada" : "EnPreparacion", status);
        Assert.Equal(0L, payments);
        Assert.Equal(await cancel ? "Disponible" : "Ocupada", await Sql("SELECT estado::text FROM mesa WHERE id_mesa=1"));
    }

    private static CreateOrderRequestDto TableOrder(long? reservationId = null) => new()
    {
        UserId = 1, TableId = 1, ReservationId = reservationId, Origin = OrderOrigin.Desktop,
        Type = OrderType.Table, Items = [new() { ProductId = 1, Quantity = 1 }]
    };

    [LocalPostgresFact]
    public async Task Concurrent_payments_record_one_sale_and_release_table_once()
    {
        long id;
        using (var scope = Scope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var order = await orders.CreateOrderAsync(TableOrder());
            Assert.True(order.IsSuccess, order.Error);
            id = order.Value!.Id;
        }
        await Sql($"UPDATE orden SET estado='Servida', cuenta_solicitada=true WHERE id_orden={id}");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenders = Enumerable.Range(0, 4).Select(async _ =>
        {
            using var scope = Scope();
            await gate.Task;
            try
            {
                return (await scope.ServiceProvider.GetRequiredService<IPaymentService>().ProcessPaymentAsync(
                    new() { OrderId = id, UserId = 1, Amount = 12.50m, Method = PaymentMethod.Cash })).IsSuccess;
            }
            catch (DbUpdateConcurrencyException) { return false; }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" }) { return false; }
        }).ToArray();
        gate.SetResult();
        Assert.Single(await Task.WhenAll(contenders).WaitAsync(TimeSpan.FromSeconds(20)), succeeded => succeeded);
        Assert.Equal(1L, await Sql($"SELECT count(*) FROM pago WHERE id_orden={id} AND estado='Completado'"));
        Assert.Equal("Pagada", await Sql($"SELECT estado::text FROM orden WHERE id_orden={id}"));
        Assert.Equal("Disponible", await Sql("SELECT estado::text FROM mesa WHERE id_mesa=1"));
    }

    [LocalPostgresFact]
    public async Task Sales_use_completed_payment_date_and_half_open_local_days_without_duplicating_revenue()
    {
        await Sql("""
            INSERT INTO orden(id_orden,id_usuario,id_mesa,estado,fecha_hora,total) VALUES
                (101,1,1,'Pagada','2026-11-17 20:00-04',30),
                (102,1,1,'Pagada','2026-11-18 10:00-04',12.50),
                (103,1,1,'Pagada','2026-11-18 10:00-04',90),
                (104,1,1,'Pagada','2026-11-18 10:00-04',80),
                (105,1,1,'Pagada','2026-11-18 10:00-04',70),
                (106,1,1,'Pagada','2026-11-18 10:00-04',60);
            INSERT INTO detalle_orden(id_orden,id_producto,cantidad,precio_unitario,subtotal) VALUES
                (101,1,2,12.50,25), (101,2,1,5,5), (102,1,1,12.50,12.50),
                (103,1,9,10,90), (104,1,8,10,80), (105,1,7,10,70), (106,1,6,10,60);
            INSERT INTO pago(id_orden,id_usuario,monto,metodo,estado,fecha_pago) VALUES
                (101,1,30,'Efectivo','Completado','2026-11-18 00:00-04'),
                (102,1,12.50,'Efectivo','Completado','2026-11-19 00:00-04'),
                (103,1,90,'Efectivo','Fallido','2026-11-18 09:00-04'),
                (104,1,80,'Efectivo','Pendiente','2026-11-18 09:00-04'),
                (105,1,70,'Efectivo','Reembolsado','2026-11-18 09:00-04');
            """);
        var today = DateTimeOffset.Parse("2026-11-18T04:00:00Z");
        using var scope = Scope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportRepository>();
        var sales = await reports.GetSalesReportAsync(today, today.AddDays(1));
        Assert.Equal(1, sales.TotalOrders);
        Assert.Equal(30m, sales.TotalRevenue);
        Assert.Collection(sales.TopSellingProducts,
            first => { Assert.Equal(1, first.ProductId); Assert.Equal(2, first.QuantitySold); Assert.Equal(25m, first.Revenue); },
            second => { Assert.Equal(2, second.ProductId); Assert.Equal(5m, second.Revenue); });
        var top = await reports.GetTopSellingProductsAsync(today, today.AddDays(1), 1);
        Assert.Equal(1, Assert.Single(top).ProductId);
        var limited = await reports.GetSalesReportAsync(today, today.AddDays(1), productLimit: 1);
        Assert.Equal(30m, limited.TotalRevenue);
        Assert.Single(limited.TopSellingProducts);
        var tomorrow = await reports.GetSalesReportAsync(today.AddDays(1), today.AddDays(2));
        Assert.Equal(1, tomorrow.TotalOrders);
        Assert.Equal(12.50m, tomorrow.TotalRevenue);
        var yesterday = await reports.GetSalesReportAsync(today.AddDays(-1), today);
        Assert.Equal(0, yesterday.TotalOrders);
        Assert.Equal(0m, yesterday.TotalRevenue);
        Assert.Empty(yesterday.TopSellingProducts);
    }

    public async Task DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (!created) return;
        // Only the random database created by this fixture on the validated local host.
        await using var c = new NpgsqlConnection(adminConnection); await c.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", c).ExecuteNonQueryAsync();
    }
}
