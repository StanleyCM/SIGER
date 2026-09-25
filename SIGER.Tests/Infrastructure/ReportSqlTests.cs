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

public class ReportSqlTests
{
    [Fact]
    public async Task Real_report_repository_translates_aggregates_to_SQL_without_database_IO()
    {
        var capture = new CaptureCommands();
        var services = new ServiceCollection();
        services.AddDbContext<SIGERDbContext>(o => o.AddInterceptors(new NeverOpenConnection(), capture));
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SIGERDatabase"] = "Host=127.0.0.1;Port=1;Database=never_connect",
            ["Supabase:Url"] = "https://test.invalid", ["Supabase:ServiceRoleKey"] = "synthetic-test-only"
        }).Build());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SIGERDbContext>();
        var report = await new ReportRepository(context).GetSalesReportAsync(
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        Assert.Equal(0, report.TotalOrders); Assert.Equal(0m, report.TotalRevenue);
        Assert.Empty(report.TopSellingProducts);
        Assert.Equal(3, capture.Sql.Count);
        Assert.Contains("count(", capture.Sql[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sum(", capture.Sql[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", capture.Sql[2]); Assert.Contains("sum(", capture.Sql[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", capture.Sql[2]);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private sealed class NeverOpenConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();
    }

    private sealed class CaptureCommands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            var data = new DataTable();
            if (Sql.Count == 1) { data.Columns.Add("value", typeof(int)); data.Rows.Add(0); }
            else if (Sql.Count == 2) { data.Columns.Add("value", typeof(decimal)); data.Rows.Add(0m); }
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(data.CreateDataReader()));
        }
    }
}
