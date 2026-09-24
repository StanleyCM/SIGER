using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class TableConfiguration : IEntityTypeConfiguration<Table>
{
    public void Configure(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("mesa");
        builder.HasKey(table => table.Id).HasName("pk_mesa");
        builder.Property(table => table.Id).HasColumnName("id_mesa").UseIdentityByDefaultColumn();
        builder.Property(table => table.Number).HasColumnName("numero").IsRequired();
        builder.Property(table => table.Capacity).HasColumnName("capacidad").IsRequired();
        builder.Property(table => table.Status).HasColumnName("estado").HasColumnType("estado_mesa").IsRequired();
        builder.Property(table => table.Location).HasColumnName("ubicacion").HasMaxLength(100);
        builder.Property(table => table.CreatedAt).HasColumnName("fecha_creacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(table => table.UpdatedAt).HasColumnName("fecha_actualizacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(table => table.Version).HasColumnName("version").HasDefaultValue(1L).IsConcurrencyToken().IsRequired();
        builder.HasIndex(table => table.Number).IsUnique().HasDatabaseName("ux_mesa_numero");
        builder.HasIndex(table => table.Status).HasDatabaseName("ix_mesa_estado");
    }
}
