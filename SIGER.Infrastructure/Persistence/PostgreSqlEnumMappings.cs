using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SIGER.Domain.Enums;

namespace SIGER.Infrastructure.Persistence;

internal static class PostgreSqlEnumMappings
{
    public static NpgsqlDbContextOptionsBuilder MapSIGEREnums(
        this NpgsqlDbContextOptionsBuilder options)
    {
        options.MapEnum<TableStatus>("estado_mesa", nameTranslator: CreateTranslator(new Dictionary<TableStatus, string>
        {
            [TableStatus.Available] = "Disponible",
            [TableStatus.Occupied] = "Ocupada",
            [TableStatus.Reserved] = "Reservada",
            [TableStatus.OutOfService] = "FueraServicio"
        }));
        options.MapEnum<OrderStatus>("estado_orden", nameTranslator: CreateTranslator(new Dictionary<OrderStatus, string>
        {
            [OrderStatus.Pending] = "Pendiente",
            [OrderStatus.InPreparation] = "EnPreparacion",
            [OrderStatus.Ready] = "Lista",
            [OrderStatus.Served] = "Servida",
            [OrderStatus.Paid] = "Pagada",
            [OrderStatus.Cancelled] = "Cancelada"
        }));
        options.MapEnum<PaymentMethod>("metodo_pago", nameTranslator: CreateTranslator(new Dictionary<PaymentMethod, string>
        {
            [PaymentMethod.Cash] = "Efectivo",
            [PaymentMethod.Card] = "Tarjeta",
            [PaymentMethod.Transfer] = "Transferencia",
            [PaymentMethod.Other] = "Otro"
        }));
        options.MapEnum<PaymentStatus>("estado_pago", nameTranslator: CreateTranslator(new Dictionary<PaymentStatus, string>
        {
            [PaymentStatus.Pending] = "Pendiente",
            [PaymentStatus.Completed] = "Completado",
            [PaymentStatus.Failed] = "Fallido",
            [PaymentStatus.Refunded] = "Reembolsado"
        }));
        options.MapEnum<OrderOrigin>("origen_orden", nameTranslator: CreateTranslator(new Dictionary<OrderOrigin, string>
        {
            [OrderOrigin.Desktop] = "Desktop",
            [OrderOrigin.Web] = "Web"
        }));
        options.MapEnum<OrderType>("tipo_orden", nameTranslator: CreateTranslator(new Dictionary<OrderType, string>
        {
            [OrderType.Table] = "Mesa",
            [OrderType.TakeAway] = "ParaLlevar"
        }));
        options.MapEnum<ReservationStatus>("estado_reserva", nameTranslator: CreateTranslator(new Dictionary<ReservationStatus, string>
        {
            [ReservationStatus.Pending] = "Pendiente",
            [ReservationStatus.Confirmed] = "Confirmada",
            [ReservationStatus.Cancelled] = "Cancelada",
            [ReservationStatus.Completed] = "Completada"
        }));

        return options;
    }

    private static PostgreSqlEnumNameTranslator CreateTranslator<TEnum>(
        IReadOnlyDictionary<TEnum, string> labels)
        where TEnum : struct, Enum =>
        new(labels.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value));
}
