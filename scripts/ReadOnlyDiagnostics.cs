// Opt-in read-only diagnostics; never included in the automated test suite.
// Run: dotnet run --file scripts/ReadOnlyDiagnostics.cs
#:project ../SIGER.API/SIGER.API.csproj

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SIGER.Infrastructure.DependencyInjection;
using SIGER.Infrastructure.Persistence;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(SIGER.API.Configuration.JwtSettings).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();
var connectionText = configuration.GetConnectionString("SIGERDatabase") ?? string.Empty;
Console.WriteLine($"Connection format is PostgreSQL URI: {connectionText.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) || connectionText.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)}");
try
{
    var parsed = new NpgsqlConnectionStringBuilder(connectionText);
    Console.WriteLine($"Npgsql connection string parses: true; host present={!string.IsNullOrWhiteSpace(parsed.Host)}; database present={!string.IsNullOrWhiteSpace(parsed.Database)}");
}
catch (ArgumentException)
{
    Console.WriteLine("Npgsql connection string parses: false. Value withheld.");
    Environment.ExitCode = 1;
    return;
}
var services = new ServiceCollection();
services.AddInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var context = scope.ServiceProvider.GetRequiredService<SIGERDbContext>();
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    var products = await context.Products.AsNoTracking().Include(product => product.Category)
        .Where(product => product.IsAvailable).Take(1).ToListAsync(timeout.Token);
    Console.WriteLine($"Read-only product mapping: OK; sample count={products.Count}");
}
catch (Exception error)
{
    for (Exception? current = error; current is not null; current = current.InnerException)
    {
        Console.WriteLine($"Error type: {current.GetType().Name}");
        if (current is PostgresException pg)
            Console.WriteLine($"SQLSTATE: {pg.SqlState}");
        if (current is System.Net.Sockets.SocketException socket)
            Console.WriteLine($"Socket code: {socket.SocketErrorCode}");
    }
    // Never emit messages, SQL, connection strings, authentication values or stack traces.
    Environment.ExitCode = 1;
}
