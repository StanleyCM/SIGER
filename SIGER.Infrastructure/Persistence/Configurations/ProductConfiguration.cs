using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("producto");
        builder.HasKey(product => product.Id).HasName("pk_producto");
        builder.Property(product => product.Id).HasColumnName("id_producto").UseIdentityByDefaultColumn();
        builder.Property(product => product.CategoryId).HasColumnName("id_categoria").IsRequired();
        builder.Property(product => product.Name).HasColumnName("nombre").HasMaxLength(150).IsRequired();
        builder.Property(product => product.Description).HasColumnName("descripcion").HasColumnType("text");
        builder.Property(product => product.Price).HasColumnName("precio").HasPrecision(10, 2).IsRequired();
        builder.Property(product => product.IsAvailable).HasColumnName("disponible").IsRequired();
        builder.Property(product => product.ImageUrl).HasColumnName("imagen_url").HasMaxLength(500);
        builder.Property(product => product.CreatedAt).HasColumnName("fecha_creacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(product => product.UpdatedAt).HasColumnName("fecha_actualizacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(product => product.Version).HasColumnName("version").HasDefaultValue(1L).IsConcurrencyToken().IsRequired();

        builder.HasIndex(product => product.CategoryId).HasDatabaseName("ix_producto_id_categoria");
        builder.HasIndex(product => product.IsAvailable).HasDatabaseName("ix_producto_disponible");

        builder.HasOne(product => product.Category)
            .WithMany(category => category.Products)
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_producto_categoria");
    }
}
