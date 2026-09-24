using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Infrastructure.Authentication;
using SIGER.Infrastructure.Persistence;
using SIGER.Infrastructure.Repositories;

namespace SIGER.Infrastructure.DependencyInjection;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("SIGERDatabase")
            ?? throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:SIGERDatabase' is not configured.");

        services.AddDbContext<SIGERDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MapSIGEREnums()));

        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ITableRepository, TableRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork.UnitOfWork>();

        var supabaseUrl = configuration["Supabase:Url"]
            ?? throw new InvalidOperationException("Configuration value 'Supabase:Url' is not configured.");
        var serviceRoleKey = configuration["Supabase:ServiceRoleKey"]
            ?? throw new InvalidOperationException(
                "Configuration value 'Supabase:ServiceRoleKey' is not configured.");

        services.AddHttpClient<IAuthProvider, SupabaseAuthService>(client =>
        {
            client.BaseAddress = new Uri($"{supabaseUrl.TrimEnd('/')}/auth/v1/", UriKind.Absolute);
            client.DefaultRequestHeaders.Add("apikey", serviceRoleKey);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        });

        return services;
    }
}
