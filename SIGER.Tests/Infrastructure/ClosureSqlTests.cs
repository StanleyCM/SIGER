using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIGER.Infrastructure.DependencyInjection;
using SIGER.Infrastructure.Persistence;
using SIGER.Infrastructure.Repositories;

namespace SIGER.Tests.Infrastructure;

public class ClosureSqlTests
{
    [Fact]
    public async Task Product_batch_is_one_filtered_query_and_empty_batch_has_no_IO()
    {
        using var f = new Fixture(); var repository = new ProductRepository(f.Context);
        Assert.Empty(await repository.GetByIdsAsync([])); Assert.Empty(f.Capture.Sql);
        Assert.Empty(await repository.GetByIdsAsync([3, 4, 5]));
        var sql = Assert.Single(f.Capture.Sql);
        Assert.Contains("FROM producto", sql); Assert.Contains("ANY", sql);
        Assert.DoesNotContain("JOIN", sql);
    }

    [Fact]
    public async Task Product_pagination_joins_category_filters_and_has_stable_SQL_order()
    {
        using var f = new Fixture();
        var page = await new ProductRepository(f.Context).GetPagedAsync(2, 20, 7);
        Assert.Empty(page.Items); Assert.Equal(2, f.Capture.Sql.Count);
        var sql = f.Capture.Sql[1];
        Assert.Contains("JOIN categoria", sql); Assert.Contains("id_categoria =", sql);
        Assert.Contains("ORDER BY p.nombre, p.id_producto", sql);
        Assert.Contains("LIMIT", sql); Assert.Contains("OFFSET", sql);
    }

    [Fact]
    public async Task Reservation_overlap_is_server_side_exists_with_half_open_two_hour_interval()
    {
        using var f = new Fixture(); var start = DateTimeOffset.Parse("2030-01-01T19:00:00Z");
        Assert.False(await new ReservationRepository(f.Context).HasOverlapAsync(4, start, start.AddHours(2), 8));
        var sql = Assert.Single(f.Capture.Sql);
        Assert.Contains("EXISTS", sql); Assert.Contains("Pendiente", sql); Assert.Contains("Confirmada", sql);
        Assert.DoesNotContain("Cancelada", sql); Assert.DoesNotContain("Completada", sql);
        Assert.Contains("r.id_reserva <>", sql); Assert.Contains("r.fecha_hora <", sql);
        Assert.Contains("INTERVAL '2 hours'", sql); Assert.Contains(" > ", sql);
    }

    [Fact]
    public async Task Table_and_reservation_locks_refuse_to_run_without_transaction()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TableRepository(f.Context).GetByIdForUpdateAsync(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReservationRepository(f.Context).GetByIdForUpdateAsync(1));
        Assert.Empty(f.Capture.Sql);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly IServiceScope scope;
        public CaptureCommands Capture { get; } = new();
        public SIGERDbContext Context { get; }
        public Fixture()
        {
            var services = new ServiceCollection();
            services.AddDbContext<SIGERDbContext>(o => o.AddInterceptors(new NoConnection(), Capture));
            services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SIGERDatabase"] = "Host=127.0.0.1;Port=1;Database=never_connect",
                ["Supabase:Url"] = "https://test.invalid", ["Supabase:ServiceRoleKey"] = "synthetic-test-only"
            }).Build());
            provider = services.BuildServiceProvider(); scope = provider.CreateScope();
            Context = scope.ServiceProvider.GetRequiredService<SIGERDbContext>();
        }
        public void Dispose()
        {
            Assert.Equal(ConnectionState.Closed, Context.Database.GetDbConnection().State);
            scope.Dispose(); provider.Dispose();
        }
    }
    private sealed class NoConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection c, ConnectionEventData e,
            InterceptionResult result, CancellationToken token = default) => ValueTask.FromResult(InterceptionResult.Suppress());
    }
    private sealed class CaptureCommands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            Sql.Add(c.CommandText); var table = new DataTable();
            if (c.CommandText.StartsWith("SELECT EXISTS", StringComparison.Ordinal))
            { table.Columns.Add("value", typeof(bool)); table.Rows.Add(false); }
            else if (c.CommandText.Contains("count(*)", StringComparison.OrdinalIgnoreCase))
            { table.Columns.Add("value", typeof(int)); table.Rows.Add(0); }
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}
