using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SIGER.Domain.Enums;

namespace SIGER.Infrastructure.Persistence;

internal static class PostgreSqlEnumMappings
{
    // Stable translator instances keep Npgsql's options/model cache reusable across request scopes.
    private static readonly PostgreSqlEnumNameTranslator TableStatusTranslator = CreateTranslator(new Dictionary<TableStatus, string>
    {
        [TableStatus.Available] = "Disponible",
        [TableStatus.Occupied] = "Ocupada",
        [TableStatus.Reserved] = "Reservada",
        [TableStatus.OutOfService] = "FueraServicio"
    });

    private static readonly PostgreSqlEnumNameTranslator OrderStatusTranslator = CreateTranslator(new Dictionary<OrderStatus, string>
    {
        [OrderStatus.Pending] = "Pendiente",
        [OrderStatus.InPreparation] = "EnPreparacion",
        [OrderStatus.Ready] = "Lista",
        [OrderStatus.Served] = "Servida",
        [OrderStatus.Paid] = "Pagada",
        [OrderStatus.Cancelled] = "Cancelada"
    });

    private static readonly PostgreSqlEnumNameTranslator PaymentMethodTranslator = CreateTranslator(new Dictionary<PaymentMethod, string>
    {
        [PaymentMethod.Cash] = "Efectivo",
        [PaymentMethod.Card] = "Tarjeta",
        [PaymentMethod.Transfer] = "Transferencia",
        [PaymentMethod.Other] = "Otro"
    });

    private static readonly PostgreSqlEnumNameTranslator PaymentStatusTranslator = CreateTranslator(new Dictionary<PaymentStatus, string>
    {
        [PaymentStatus.Pending] = "Pendiente",
        [PaymentStatus.Completed] = "Completado",
        [PaymentStatus.Failed] = "Fallido",
        [PaymentStatus.Refunded] = "Reembolsado"
    });

    private static readonly PostgreSqlEnumNameTranslator OrderOriginTranslator = CreateTranslator(new Dictionary<OrderOrigin, string>
    {
        [OrderOrigin.Desktop] = "Desktop",
        [OrderOrigin.Web] = "Web"
    });

    private static readonly PostgreSqlEnumNameTranslator OrderTypeTranslator = CreateTranslator(new Dictionary<OrderType, string>
    {
        [OrderType.Table] = "Mesa",
        [OrderType.TakeAway] = "ParaLlevar"
    });

    private static readonly PostgreSqlEnumNameTranslator ReservationStatusTranslator = CreateTranslator(new Dictionary<ReservationStatus, string>
    {
        [ReservationStatus.Pending] = "Pendiente",
        [ReservationStatus.Confirmed] = "Confirmada",
        [ReservationStatus.Cancelled] = "Cancelada",
        [ReservationStatus.Completed] = "Completada"
    });

    public static NpgsqlDbContextOptionsBuilder MapSIGEREnums(
        this NpgsqlDbContextOptionsBuilder options)
    {
        options.MapEnum<TableStatus>("estado_mesa", nameTranslator: TableStatusTranslator);
        options.MapEnum<OrderStatus>("estado_orden", nameTranslator: OrderStatusTranslator);
        options.MapEnum<PaymentMethod>("metodo_pago", nameTranslator: PaymentMethodTranslator);
        options.MapEnum<PaymentStatus>("estado_pago", nameTranslator: PaymentStatusTranslator);
        options.MapEnum<OrderOrigin>("origen_orden", nameTranslator: OrderOriginTranslator);
        options.MapEnum<OrderType>("tipo_orden", nameTranslator: OrderTypeTranslator);
        options.MapEnum<ReservationStatus>("estado_reserva", nameTranslator: ReservationStatusTranslator);

        return options;
    }

    private static PostgreSqlEnumNameTranslator CreateTranslator<TEnum>(
        IReadOnlyDictionary<TEnum, string> labels)
        where TEnum : struct, Enum =>
        new(labels.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value));
}
