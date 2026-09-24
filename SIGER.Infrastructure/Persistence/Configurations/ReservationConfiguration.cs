using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reserva");
        builder.HasKey(reservation => reservation.Id).HasName("pk_reserva");
        builder.Property(reservation => reservation.Id).HasColumnName("id_reserva").UseIdentityByDefaultColumn();
        builder.Property(reservation => reservation.UserId).HasColumnName("id_usuario").IsRequired();
        builder.Property(reservation => reservation.TableId).HasColumnName("id_mesa").IsRequired();
        builder.Property(reservation => reservation.ReservationDateTime).HasColumnName("fecha_hora").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(reservation => reservation.NumberOfPeople).HasColumnName("cantidad_personas").IsRequired();
        builder.Property(reservation => reservation.Status).HasColumnName("estado").HasColumnType("estado_reserva").IsRequired();
        builder.Property(reservation => reservation.Notes).HasColumnName("notas").HasMaxLength(500);
        builder.Property(reservation => reservation.CreatedAt).HasColumnName("fecha_creacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(reservation => reservation.UpdatedAt).HasColumnName("fecha_actualizacion").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(reservation => reservation.ReservationDateTime).HasDatabaseName("ix_reserva_fecha_hora");
        builder.HasIndex(reservation => reservation.UserId).HasDatabaseName("ix_reserva_id_usuario");
        builder.HasIndex(reservation => reservation.TableId).HasDatabaseName("ix_reserva_id_mesa");

        builder.HasOne(reservation => reservation.User)
            .WithMany(user => user.Reservations)
            .HasForeignKey(reservation => reservation.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_reserva_usuario");

        builder.HasOne(reservation => reservation.Table)
            .WithMany(table => table.Reservations)
            .HasForeignKey(reservation => reservation.TableId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_reserva_mesa");
    }
}
