using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Application.Services;
using SIGER.Domain.Enums;
using SIGER.Infrastructure.DependencyInjection;
using SIGER.Infrastructure.Persistence;

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
    private bool created;
    private static string Root => FindRoot();
    private static string Script => File.ReadAllText(Path.Combine(Root, "scripts/database/003_guest_reservations_preorders.sql"));
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
        Name = "PRIVATE-NAME", Phone = "+584121234567", Email = "private@example.invalid", Notes = "PRIVATE-NOTES",
        NumberOfPeople = 2, ReservationDateTime = date ?? DateTimeOffset.UtcNow.AddDays(1)
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
    public async Task Script_reuses_equivalent_indexes_under_other_names()
    {
        await Sql("ALTER INDEX ux_orden_id_reserva RENAME TO existing_order_reservation; ALTER INDEX ux_reserva_token_acceso_hash RENAME TO existing_token; ALTER INDEX ix_reserva_mesa_fecha_disponibilidad RENAME TO existing_availability;");
        var count = await Sql("SELECT count(*) FROM pg_indexes WHERE schemaname='public'");
        await Sql(Script);
        Assert.Equal(count, await Sql("SELECT count(*) FROM pg_indexes WHERE schemaname='public'"));
        Assert.Equal(3L, await Sql("SELECT count(*) FROM pg_indexes WHERE indexname IN ('existing_order_reservation','existing_token','existing_availability')"));
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
        var when = DateTimeOffset.UtcNow.AddDays(2);
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
    public async Task Half_open_intervals_allow_back_to_back_reservations()
    {
        var when = DateTimeOffset.UtcNow.AddDays(3);
        await Reserve(when); await Reserve(when.AddHours(2));
        Assert.Equal(2L, await Sql("SELECT count(*) FROM reserva WHERE id_usuario IS NULL"));
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
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() => Sql("DELETE FROM reserva WHERE id_usuario IS NULL"))).SqlState);
    }

    [LocalPostgresFact]
    public async Task Public_catalog_filters_in_SQL_before_pagination_and_audit_excludes_contacts_and_credentials()
    {
        var r = await Reserve();
        using var scope = Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<IPublicCatalogService>();
        var page = (await catalog.GetProductsAsync(1, 1, null)).Value!;
        Assert.Equal(1, page.TotalCount); Assert.Equal(1, Assert.Single(page.Items).Id);
        Assert.Single((await catalog.GetCategoriesAsync()).Value!);
        var db = scope.ServiceProvider.GetRequiredService<SIGERDbContext>();
        var reservation = await db.Reservations.SingleAsync(x => x.Id == r.Reservation.Id);
        Assert.Equal(32, reservation.AccessTokenHash!.Length);
        foreach (var audit in await db.Audits.ToArrayAsync())
        {
            var json = audit.PreviousData + audit.NewData;
            Assert.DoesNotContain("PRIVATE", json); Assert.DoesNotContain("private@example.invalid", json);
            Assert.DoesNotContain("+584121234567", json); Assert.DoesNotContain(r.AccessToken, json);
            Assert.DoesNotContain(Convert.ToBase64String(reservation.AccessTokenHash), json);
        }
    }

    [LocalPostgresFact]
    public async Task Guest_edit_reassigns_preorder_preserving_credentials_prices_and_nonoperational_state()
    {
        var r = await Reserve();
        using (var s = Scope()) Assert.True((await s.ServiceProvider.GetRequiredService<IPreOrderService>().CreateAsync(r.Reservation.Id, r.AccessToken, Items)).IsSuccess);
        var hash = await Sql("SELECT encode(token_acceso_hash,'hex') FROM reserva WHERE id_usuario IS NULL");
        var details = await Sql("SELECT jsonb_agg(to_jsonb(d))::text FROM detalle_orden d");
        await Sql("INSERT INTO mesa(numero,capacidad) VALUES (2,12)");
        var tablesBefore = await Sql("SELECT jsonb_agg(to_jsonb(t) ORDER BY id_mesa)::text FROM mesa t");
        var productsBefore = await Sql("SELECT jsonb_agg(to_jsonb(p) ORDER BY id_producto)::text FROM producto p");
        var date = DateTimeOffset.UtcNow.AddDays(4);
        using (var s = Scope())
        {
            var result = await s.ServiceProvider.GetRequiredService<IGuestReservationService>().UpdateAsync(r.Reservation.Id, r.AccessToken,
                new() { Name = "PRIVATE-EDIT", Phone = "8095550199", NumberOfPeople = 8, ReservationDateTime = date, Notes = "PRIVATE-EDIT-NOTES" });
            Assert.True(result.IsSuccess, result.Error); Assert.Equal(date.AddHours(2), result.Value!.AccessExpiresAt);
        }
        Assert.Equal(hash, await Sql("SELECT encode(token_acceso_hash,'hex') FROM reserva WHERE id_usuario IS NULL"));
        Assert.Equal(details, await Sql("SELECT jsonb_agg(to_jsonb(d))::text FROM detalle_orden d"));
        Assert.Equal(1L, await Sql("SELECT count(*) FROM reserva r JOIN orden o ON o.id_reserva=r.id_reserva WHERE r.id_mesa=2 AND o.id_mesa=r.id_mesa AND r.estado='Pendiente' AND o.estado='Preordenada' AND o.origen='Web' AND o.total=25"));
        Assert.Equal(0L, await Sql("SELECT count(*) FROM mesa WHERE estado <> 'Disponible'"));
        Assert.Equal(0L, await Sql("SELECT count(*) FROM pago"));
        Assert.Equal(tablesBefore, await Sql("SELECT jsonb_agg(to_jsonb(t) ORDER BY id_mesa)::text FROM mesa t"));
        Assert.Equal(productsBefore, await Sql("SELECT jsonb_agg(to_jsonb(p) ORDER BY id_producto)::text FROM producto p"));
        using var read = Scope();
        Assert.True((await read.ServiceProvider.GetRequiredService<IGuestReservationService>().GetAsync(r.Reservation.Id, r.AccessToken)).IsSuccess);
        Assert.Empty(await read.ServiceProvider.GetRequiredService<IOrderRepository>().GetKitchenOrdersAsync());
        var audits = (string)(await Sql("SELECT coalesce(jsonb_agg(to_jsonb(a))::text,'') FROM auditoria a"))!;
        Assert.DoesNotContain("PRIVATE", audits); Assert.DoesNotContain(r.AccessToken, audits); Assert.DoesNotContain("8095550199", audits);
    }

    [LocalPostgresFact]
    public async Task Guest_edit_excludes_self_allows_adjacent_intervals_and_failure_preserves_every_field()
    {
        var date = DateTimeOffset.UtcNow.AddDays(5); var r = await Reserve(date);
        using (var s = Scope()) Assert.True((await s.ServiceProvider.GetRequiredService<IGuestReservationService>().UpdateAsync(r.Reservation.Id, r.AccessToken,
            new() { ReservationDateTime = date.AddMinutes(30), NumberOfPeople = 3 })).IsSuccess);
        await Reserve(date.AddHours(2.5));
        var before = await Sql("SELECT jsonb_agg(to_jsonb(r) ORDER BY id_reserva)::text FROM reserva r");
        using (var s = Scope()) Assert.Equal("No table is available for this reservation.",
            (await s.ServiceProvider.GetRequiredService<IGuestReservationService>().UpdateAsync(r.Reservation.Id, r.AccessToken,
                new() { ReservationDateTime = date.AddHours(1), Name = "Do not save" })).Error);
        Assert.Equal(before, await Sql("SELECT jsonb_agg(to_jsonb(r) ORDER BY id_reserva)::text FROM reserva r"));
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
        var date = DateTimeOffset.UtcNow.AddDays(6);
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
            Assert.Equal(status is "Cancelada" or "Completada", slots.Single(s => s.Time == "12:00").Available);
            Assert.Equal(status is "Cancelada" or "Completada", slots.Single(s => s.Time == "13:30").Available);
            Assert.True(slots.Single(s => s.Time == "14:00").Available);
        }
        await Sql("UPDATE reserva SET estado='Pendiente' WHERE id_usuario IS NULL");
        Assert.True((await Read(date, own: true)).Single(s => s.Time == "12:00").Available);
        Assert.All(await Read(date.AddDays(1)), s => Assert.True(s.Available));
        Assert.All(await Read(date, 5), s => Assert.False(s.Available));
        Assert.True((await Read(date)).Single(s => s.Time == "18:00").Available);
        // A competing write after the advisory read must still be rejected by creation.
        Assert.True((await Reserve(start.AddHours(6))).Reservation.Id > 0);
        using (var scope = Scope()) Assert.True((await scope.ServiceProvider.GetRequiredService<IGuestReservationService>().CreateAsync(Request(start.AddHours(6)))).IsFailure);
        await Sql("UPDATE mesa SET estado='FueraServicio' WHERE id_mesa=1");
        Assert.All(await Read(date.AddDays(1)), s => Assert.False(s.Available));
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
