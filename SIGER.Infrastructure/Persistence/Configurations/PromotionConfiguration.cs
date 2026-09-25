using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("promocion");
        builder.HasKey(promotion => promotion.Id).HasName("pk_promocion");
        builder.Property(promotion => promotion.Id).HasColumnName("id_promocion").UseIdentityByDefaultColumn();
        builder.Property(promotion => promotion.Name).HasColumnName("nombre").HasMaxLength(150).IsRequired();
        builder.Property(promotion => promotion.Description).HasColumnName("descripcion").HasMaxLength(500);
        builder.Property(promotion => promotion.DiscountPercentage).HasColumnName("porcentaje_descuento").HasPrecision(5, 2).IsRequired();
        builder.Property(promotion => promotion.StartDate).HasColumnName("fecha_inicio").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(promotion => promotion.EndDate).HasColumnName("fecha_fin").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(promotion => promotion.IsActive).HasColumnName("estado").IsRequired();
        builder.Property(promotion => promotion.CreatedAt).HasColumnName("fecha_creacion").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(promotion => new { promotion.IsActive, promotion.StartDate, promotion.EndDate })
            .HasDatabaseName("ix_promocion_vigencia");
    }
}
