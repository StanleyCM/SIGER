using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SIGER.Infrastructure.Persistence;

public sealed class SIGERDbContextFactory : IDesignTimeDbContextFactory<SIGERDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Database=siger_design_time;Username=siger_design_time";

    public SIGERDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SIGERDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DesignTimeConnectionString;
        }

        var options = new DbContextOptionsBuilder<SIGERDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MapSIGEREnums())
            .Options;

        return new SIGERDbContext(options);
    }
}
