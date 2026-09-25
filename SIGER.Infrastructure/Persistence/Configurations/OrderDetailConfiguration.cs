using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class OrderDetailConfiguration : IEntityTypeConfiguration<OrderDetail>
{
    public void Configure(EntityTypeBuilder<OrderDetail> builder)
    {
        builder.ToTable("detalle_orden");
        builder.HasKey(detail => detail.Id).HasName("pk_detalle_orden");
        builder.Property(detail => detail.Id).HasColumnName("id_detalle").UseIdentityByDefaultColumn();
        builder.Property(detail => detail.OrderId).HasColumnName("id_orden").IsRequired();
        builder.Property(detail => detail.ProductId).HasColumnName("id_producto").IsRequired();
        builder.Property(detail => detail.Quantity).HasColumnName("cantidad").IsRequired();
        builder.Property(detail => detail.UnitPrice).HasColumnName("precio_unitario").HasPrecision(12, 2).IsRequired();
        builder.Property(detail => detail.Subtotal).HasColumnName("subtotal").HasPrecision(12, 2).IsRequired();
        builder.Property(detail => detail.Note).HasColumnName("notas").HasMaxLength(300);

        builder.HasIndex(detail => detail.OrderId).HasDatabaseName("ix_detalle_orden_id_orden");
        builder.HasIndex(detail => detail.ProductId).HasDatabaseName("ix_detalle_orden_id_producto");

        builder.HasOne(detail => detail.Order)
            .WithMany(order => order.Details)
            .HasForeignKey(detail => detail.OrderId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_detalle_orden_orden");

        builder.HasOne(detail => detail.Product)
            .WithMany(product => product.OrderDetails)
            .HasForeignKey(detail => detail.ProductId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_detalle_orden_producto");
    }
}
