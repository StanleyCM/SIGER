using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

public class PromotionProductConfiguration : IEntityTypeConfiguration<PromotionProduct>
{
    public void Configure(EntityTypeBuilder<PromotionProduct> builder)
    {
        builder.ToTable("promocion_producto");
        builder.HasKey(item => new { item.PromotionId, item.ProductId }).HasName("pk_promocion_producto");
        builder.Property(item => item.PromotionId).HasColumnName("id_promocion");
        builder.Property(item => item.ProductId).HasColumnName("id_producto");
        builder.HasIndex(item => item.ProductId).HasDatabaseName("ix_promocion_producto_id_producto");

        builder.HasOne(item => item.Promotion)
            .WithMany(promotion => promotion.PromotionProducts)
            .HasForeignKey(item => item.PromotionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_promocion_producto_promocion");

        builder.HasOne(item => item.Product)
            .WithMany(product => product.PromotionProducts)
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_promocion_producto_producto");
    }
}
