using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class AuditConfiguration : IEntityTypeConfiguration<Audit>
{
    public void Configure(EntityTypeBuilder<Audit> builder)
    {
        var ipAddressConverter = new ValueConverter<string?, IPAddress?>(
            value => value == null ? null : IPAddress.Parse(value),
            value => value == null ? null : value.ToString());

        builder.ToTable("auditoria");
        builder.HasKey(audit => audit.Id).HasName("pk_auditoria");
        builder.Property(audit => audit.Id).HasColumnName("id_auditoria").UseIdentityByDefaultColumn();
        builder.Property(audit => audit.UserId).HasColumnName("id_usuario");
        builder.Property(audit => audit.Action).HasColumnName("accion").HasMaxLength(100).IsRequired();
        builder.Property(audit => audit.Entity).HasColumnName("entidad").HasMaxLength(100).IsRequired();
        builder.Property(audit => audit.EntityId).HasColumnName("id_entidad");
        builder.Property(audit => audit.PreviousData).HasColumnName("datos_anteriores").HasColumnType("jsonb");
        builder.Property(audit => audit.NewData).HasColumnName("datos_nuevos").HasColumnType("jsonb");
        builder.Property(audit => audit.IpAddress).HasColumnName("ip").HasConversion(ipAddressConverter).HasColumnType("inet");
        builder.Property(audit => audit.Timestamp).HasColumnName("fecha_hora").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(audit => audit.Timestamp).HasDatabaseName("ix_auditoria_fecha_hora");
        builder.HasIndex(audit => audit.UserId).HasDatabaseName("ix_auditoria_id_usuario");

        builder.HasOne(audit => audit.User)
            .WithMany(user => user.Audits)
            .HasForeignKey(audit => audit.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_auditoria_usuario");
    }
}
