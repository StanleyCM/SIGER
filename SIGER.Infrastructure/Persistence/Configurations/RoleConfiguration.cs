using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("rol");
        builder.HasKey(role => role.Id).HasName("pk_rol");
        builder.Property(role => role.Id).HasColumnName("id_rol").UseIdentityByDefaultColumn();
        builder.Property(role => role.Name).HasColumnName("nombre").HasMaxLength(50).IsRequired();
        builder.Property(role => role.Description).HasColumnName("descripcion").HasMaxLength(250);
        builder.Property(role => role.IsActive).HasColumnName("estado").IsRequired();
        builder.HasIndex(role => role.Name).IsUnique().HasDatabaseName("ux_rol_nombre");
    }
}
