using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("pago");
        builder.HasKey(payment => payment.Id).HasName("pk_pago");
        builder.Property(payment => payment.Id).HasColumnName("id_pago").UseIdentityByDefaultColumn();
        builder.Property(payment => payment.OrderId).HasColumnName("id_orden").IsRequired();
        builder.Property(payment => payment.UserId).HasColumnName("id_usuario").IsRequired();
        builder.Property(payment => payment.Amount).HasColumnName("monto").HasPrecision(12, 2).IsRequired();
        builder.Property(payment => payment.Method).HasColumnName("metodo").HasColumnType("metodo_pago").IsRequired();
        builder.Property(payment => payment.Status).HasColumnName("estado").HasColumnType("estado_pago").IsRequired();
        builder.Property(payment => payment.Reference).HasColumnName("referencia").HasMaxLength(150);
        builder.Property(payment => payment.PaymentDate).HasColumnName("fecha_pago").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(payment => payment.UpdatedAt).HasColumnName("fecha_actualizacion").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(payment => payment.OrderId).HasDatabaseName("ix_pago_id_orden");
        builder.HasIndex(payment => payment.UserId).HasDatabaseName("ix_pago_id_usuario");

        builder.HasOne(payment => payment.Order)
            .WithMany(order => order.Payments)
            .HasForeignKey(payment => payment.OrderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pago_orden");

        builder.HasOne(payment => payment.User)
            .WithMany(user => user.Payments)
            .HasForeignKey(payment => payment.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pago_usuario");
    }
}
