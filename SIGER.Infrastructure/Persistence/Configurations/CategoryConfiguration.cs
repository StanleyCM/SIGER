using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categoria");
        builder.HasKey(category => category.Id).HasName("pk_categoria");
        builder.Property(category => category.Id).HasColumnName("id_categoria").UseIdentityByDefaultColumn();
        builder.Property(category => category.Name).HasColumnName("nombre").HasMaxLength(100).IsRequired();
        builder.Property(category => category.Description).HasColumnName("descripcion").HasMaxLength(250);
        builder.Property(category => category.IsActive).HasColumnName("estado").IsRequired();
        builder.Property(category => category.CreatedAt).HasColumnName("fecha_creacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(category => category.UpdatedAt).HasColumnName("fecha_actualizacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(category => category.Name).IsUnique().HasDatabaseName("ux_categoria_nombre");
    }
}
