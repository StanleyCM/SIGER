using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("usuario");
        builder.HasKey(user => user.Id).HasName("pk_usuario");
        builder.Property(user => user.Id).HasColumnName("id_usuario").UseIdentityByDefaultColumn();
        builder.Property(user => user.RoleId).HasColumnName("id_rol").IsRequired();
        builder.Property(user => user.AuthUserId).HasColumnName("auth_user_id").IsRequired();
        builder.Property(user => user.FirstName).HasColumnName("nombre").HasMaxLength(100).IsRequired();
        builder.Property(user => user.LastName).HasColumnName("apellido").HasMaxLength(100).IsRequired();
        builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(150).IsRequired();
        builder.Property(user => user.Phone).HasColumnName("telefono").HasMaxLength(20);
        builder.Property(user => user.IsActive).HasColumnName("estado").IsRequired();
        builder.Property(user => user.CreatedAt).HasColumnName("fecha_creacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(user => user.UpdatedAt).HasColumnName("fecha_actualizacion").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(user => user.AuthUserId).IsUnique().HasDatabaseName("ux_usuario_auth_user_id");
        builder.HasIndex(user => user.Email).HasDatabaseName("ix_usuario_email");
        builder.HasIndex(user => user.RoleId).HasDatabaseName("ix_usuario_id_rol");

        builder.HasOne(user => user.Role)
            .WithMany(role => role.Users)
            .HasForeignKey(user => user.RoleId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_usuario_rol");
    }
}
