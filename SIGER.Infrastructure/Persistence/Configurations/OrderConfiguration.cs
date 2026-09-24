using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orden");
        builder.HasKey(order => order.Id).HasName("pk_orden");
        builder.Property(order => order.Id).HasColumnName("id_orden").UseIdentityByDefaultColumn();
        builder.Property(order => order.TableId).HasColumnName("id_mesa");
        builder.Property(order => order.UserId).HasColumnName("id_usuario").IsRequired();
        builder.Property(order => order.ClientId).HasColumnName("id_cliente");
        builder.Property(order => order.OrderDateTime).HasColumnName("fecha_hora").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(order => order.Status).HasColumnName("estado").HasColumnType("estado_orden").IsRequired();
        builder.Property(order => order.Origin).HasColumnName("origen").HasColumnType("origen_orden").IsRequired();
        builder.Property(order => order.Type).HasColumnName("tipo").HasColumnType("tipo_orden").IsRequired();
        builder.Property(order => order.Total).HasColumnName("total").HasPrecision(12, 2).IsRequired();
        builder.Property(order => order.Notes).HasColumnName("notas").HasMaxLength(500);
        builder.Property(order => order.AccountRequested).HasColumnName("cuenta_solicitada").IsRequired();
        builder.Property(order => order.UpdatedAt).HasColumnName("fecha_actualizacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(order => order.Version).HasColumnName("version").HasDefaultValue(1L).IsConcurrencyToken().IsRequired();

        builder.HasIndex(order => order.OrderDateTime).HasDatabaseName("ix_orden_fecha_hora");
        builder.HasIndex(order => order.Status).HasDatabaseName("ix_orden_estado");
        builder.HasIndex(order => order.TableId).HasDatabaseName("ix_orden_id_mesa");
        builder.HasIndex(order => order.UserId).HasDatabaseName("ix_orden_id_usuario");
        builder.HasIndex(order => order.ClientId).HasDatabaseName("ix_orden_id_cliente");

        builder.HasOne(order => order.Table)
            .WithMany(table => table.Orders)
            .HasForeignKey(order => order.TableId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_orden_mesa");

        builder.HasOne(order => order.User)
            .WithMany(user => user.CreatedOrders)
            .HasForeignKey(order => order.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_orden_usuario");

        builder.HasOne(order => order.Client)
            .WithMany(user => user.ClientOrders)
            .HasForeignKey(order => order.ClientId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_orden_cliente");
    }
}
