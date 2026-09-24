using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Enums;
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

        var tableStatusTranslator = CreateTranslator(new Dictionary<TableStatus, string>()
        {
            [TableStatus.Available] = "Disponible",
            [TableStatus.Occupied] = "Ocupada",
            [TableStatus.Reserved] = "Reservada",
            [TableStatus.OutOfService] = "FueraServicio"
        });
        var orderStatusTranslator = CreateTranslator(new Dictionary<OrderStatus, string>()
        {
            [OrderStatus.Pending] = "Pendiente",
            [OrderStatus.InPreparation] = "EnPreparacion",
            [OrderStatus.Ready] = "Lista",
            [OrderStatus.Served] = "Servida",
            [OrderStatus.Paid] = "Pagada",
            [OrderStatus.Cancelled] = "Cancelada"
        });
        var paymentMethodTranslator = CreateTranslator(new Dictionary<PaymentMethod, string>()
        {
            [PaymentMethod.Cash] = "Efectivo",
            [PaymentMethod.Card] = "Tarjeta",
            [PaymentMethod.Transfer] = "Transferencia",
            [PaymentMethod.Other] = "Otro"
        });
        var paymentStatusTranslator = CreateTranslator(new Dictionary<PaymentStatus, string>()
        {
            [PaymentStatus.Pending] = "Pendiente",
            [PaymentStatus.Completed] = "Completado",
            [PaymentStatus.Failed] = "Fallido",
            [PaymentStatus.Refunded] = "Reembolsado"
        });
        var orderOriginTranslator = CreateTranslator(new Dictionary<OrderOrigin, string>()
        {
            [OrderOrigin.Desktop] = "Desktop",
            [OrderOrigin.Web] = "Web"
        });
        var orderTypeTranslator = CreateTranslator(new Dictionary<OrderType, string>()
        {
            [OrderType.Table] = "Mesa",
            [OrderType.TakeAway] = "ParaLlevar"
        });
        var reservationStatusTranslator = CreateTranslator(new Dictionary<ReservationStatus, string>()
        {
            [ReservationStatus.Pending] = "Pendiente",
            [ReservationStatus.Confirmed] = "Confirmada",
            [ReservationStatus.Cancelled] = "Cancelada",
            [ReservationStatus.Completed] = "Completada"
        });

        services.AddDbContext<SIGERDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MapEnum<TableStatus>("estado_mesa", nameTranslator: tableStatusTranslator);
                npgsql.MapEnum<OrderStatus>("estado_orden", nameTranslator: orderStatusTranslator);
                npgsql.MapEnum<PaymentMethod>("metodo_pago", nameTranslator: paymentMethodTranslator);
                npgsql.MapEnum<PaymentStatus>("estado_pago", nameTranslator: paymentStatusTranslator);
                npgsql.MapEnum<OrderOrigin>("origen_orden", nameTranslator: orderOriginTranslator);
                npgsql.MapEnum<OrderType>("tipo_orden", nameTranslator: orderTypeTranslator);
                npgsql.MapEnum<ReservationStatus>("estado_reserva", nameTranslator: reservationStatusTranslator);
            }));

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

    private static PostgreSqlEnumNameTranslator CreateTranslator<TEnum>(
        IReadOnlyDictionary<TEnum, string> labels)
        where TEnum : struct, Enum =>
        new(labels.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value));
}
