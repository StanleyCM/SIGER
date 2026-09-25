using System.Data;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using NpgsqlTypes;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Infrastructure.Persistence;

/// <summary>Explicit scalar allowlists: no entity graphs, credentials, free text or personal profile values.</summary>
internal static class BusinessAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Dictionary<Type, string[]> Fields = new()
    {
        [typeof(User)] = ["Id", "RoleId", "IsActive", "CreatedAt", "UpdatedAt"],
        [typeof(Category)] = ["Id", "IsActive", "CreatedAt", "UpdatedAt"],
        [typeof(Product)] = ["Id", "CategoryId", "Price", "IsAvailable", "Version", "CreatedAt", "UpdatedAt"],
        [typeof(Table)] = ["Id", "Number", "Capacity", "Status", "Version", "CreatedAt", "UpdatedAt"],
        [typeof(Order)] = ["Id", "TableId", "UserId", "ClientId", "Status", "Origin", "Type", "Total", "AccountRequested", "Version", "OrderDateTime", "UpdatedAt"],
        [typeof(OrderDetail)] = ["Id", "OrderId", "ProductId", "Quantity", "UnitPrice", "Subtotal"],
        [typeof(Payment)] = ["Id", "OrderId", "UserId", "Amount", "Method", "Status", "PaymentDate", "UpdatedAt"],
        [typeof(Reservation)] = ["Id", "UserId", "TableId", "ReservationDateTime", "NumberOfPeople", "Status", "CreatedAt", "UpdatedAt"],
        [typeof(Promotion)] = ["Id", "DiscountPercentage", "StartDate", "EndDate", "IsActive", "CreatedAt"],
        [typeof(PromotionProduct)] = ["PromotionId", "ProductId"]
    };

    public static List<Draft> Capture(ChangeTracker tracker, IAuditActor? actor)
    {
        tracker.DetectChanges();
        var entries = tracker.Entries().Where(e => Fields.ContainsKey(e.Metadata.ClrType) &&
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (entries.Length == 0) return [];
        var userId = actor?.UserId;
        if (userId is <= 0) throw new InvalidOperationException("Invalid audit actor.");
        var ip = IPAddress.TryParse(actor?.IpAddress, out var address) ? address.ToString() : null;
        return entries.Select(e => new Draft(e, userId, ip)).Where(d => d.State != EntityState.Modified || d.ChangedFields.Length > 0).ToList();
    }

    internal sealed class Draft
    {
        private readonly EntityEntry entry;
        private readonly long? actor;
        private readonly string? ip;
        private readonly string? previous;
        private readonly string action;
        public EntityState State { get; }
        public string[] ChangedFields { get; }

        public Draft(EntityEntry entry, long? actor, string? ip)
        {
            this.entry = entry; this.actor = actor; this.ip = ip; State = entry.State;
            ChangedFields = State == EntityState.Modified ? entry.Properties
                .Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue) && p.Metadata.Name is not "UpdatedAt" and not "Version")
                .Select(p => p.Metadata.Name).Order().ToArray() : [];
            previous = State == EntityState.Added ? null : Snapshot(true);
            action = Action();
        }

        public Audit Complete() => new()
        {
            UserId = actor, IpAddress = ip, Action = action, Entity = entry.Metadata.ClrType.Name,
            EntityId = entry.Metadata.ClrType == typeof(PromotionProduct) ? null : (long?)entry.Property("Id").CurrentValue,
            PreviousData = previous, NewData = State == EntityState.Deleted ? null : Snapshot(false), Timestamp = DateTimeOffset.UtcNow
        };

        private string Snapshot(bool original)
        {
            var values = Fields[entry.Metadata.ClrType].ToDictionary(name => name,
                name => original ? entry.Property(name).OriginalValue : entry.Property(name).CurrentValue);
            return JsonSerializer.Serialize(new { SchemaVersion = 1, Values = values, ChangedFields }, JsonOptions);
        }

        private string Action()
        {
            if (State == EntityState.Deleted) return "Delete";
            if (entry.Entity is Payment && (PaymentStatus)entry.Property("Status").CurrentValue! == PaymentStatus.Completed) return "Pay";
            if (State == EntityState.Added) return "Create";
            if (ChangedFields.Contains("IsActive")) return (bool)entry.Property("IsActive").CurrentValue! ? "Activate" : "Deactivate";
            if (ChangedFields.Contains("IsAvailable")) return "AvailabilityChange";
            if (ChangedFields.Contains("Status"))
            {
                if (entry.Entity is Order && (OrderStatus)entry.Property("Status").CurrentValue! == OrderStatus.Cancelled ||
                    entry.Entity is Reservation && (ReservationStatus)entry.Property("Status").CurrentValue! == ReservationStatus.Cancelled) return "Cancel";
                return "StatusChange";
            }
            if (entry.Entity is User && ChangedFields.Contains("Email")) return "EmailChange";
            return "Update";
        }
    }

    // One parameterized multi-row INSERT per bounded batch, in the existing SaveChanges transaction.
    // Going through EF's command pipeline preserves interceptors/test failure injection and logging policy.
    public static IEnumerable<(string Sql, object[] Parameters)> Commands(DbContext context, IReadOnlyList<Draft> drafts)
    {
        foreach (var batch in drafts.Chunk(64))
        {
            var parameters = new List<object>(); var rows = new List<string>();
            using var command = context.Database.GetDbConnection().CreateCommand();
            foreach (var draft in batch)
            {
                var a = draft.Complete();
                var names = new[]
                {
                    Add(a.UserId, DbType.Int64), Add(a.Action, DbType.String), Add(a.Entity, DbType.String), Add(a.EntityId, DbType.Int64),
                    Add(a.PreviousData, DbType.String, NpgsqlDbType.Jsonb), Add(a.NewData, DbType.String, NpgsqlDbType.Jsonb),
                    Add(a.IpAddress, DbType.String, NpgsqlDbType.Inet), Add(a.Timestamp, DbType.DateTimeOffset)
                };
                rows.Add("(" + string.Join(",", names) + ")");
            }
            yield return ("INSERT INTO \"auditoria\" (\"id_usuario\",\"accion\",\"entidad\",\"entidad_id\",\"datos_anteriores\",\"datos_nuevos\",\"direccion_ip\",\"fecha_hora\") VALUES " + string.Join(",", rows), parameters.ToArray());

            string Add(object? value, DbType type, NpgsqlDbType? pgType = null)
            {
                var p = command.CreateParameter(); p.ParameterName = "@audit" + parameters.Count; p.DbType = type;
                p.Value = value ?? DBNull.Value;
                if (p is NpgsqlParameter np && pgType.HasValue)
                {
                    np.NpgsqlDbType = pgType.Value;
                    if (pgType == NpgsqlDbType.Inet && value is string text) np.Value = IPAddress.Parse(text);
                }
                parameters.Add(p); return p.ParameterName;
            }
        }
    }
}
