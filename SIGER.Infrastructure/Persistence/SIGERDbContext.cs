using Microsoft.EntityFrameworkCore;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence;

public class SIGERDbContext : DbContext
{
    public SIGERDbContext(DbContextOptions<SIGERDbContext> options)
        : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Table> Tables => Set<Table>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionProduct> PromotionProducts => Set<PromotionProduct>();
    public DbSet<Audit> Audits => Set<Audit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SIGERDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareTrackedEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareTrackedEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareTrackedEntities()
    {
        var now = DateTimeOffset.UtcNow;

        PrepareUserTimestamps(now);
        PrepareCategoryTimestamps(now);
        PrepareProduct(now);
        PrepareTable(now);
        PrepareOrder(now);
        PreparePayment(now);
        PrepareReservation(now);
        PreparePromotion(now);
        PrepareAudit(now);
    }

    private void PrepareUserTimestamps(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
    }

    private void PrepareCategoryTimestamps(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<Category>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
    }

    private void PrepareProduct(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<Product>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version = entry.Entity.Version <= 0 ? 1 : entry.Entity.Version;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version = entry.Property(product => product.Version).OriginalValue + 1;
            }
        }
    }

    private void PrepareTable(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<Table>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version = entry.Entity.Version <= 0 ? 1 : entry.Entity.Version;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version = entry.Property(table => table.Version).OriginalValue + 1;
            }
        }
    }

    private void PrepareOrder(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<Order>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.OrderDateTime == default) entry.Entity.OrderDateTime = now;
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version = entry.Entity.Version <= 0 ? 1 : entry.Entity.Version;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version = entry.Property(order => order.Version).OriginalValue + 1;
            }
        }
    }

    private void PreparePayment(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<Payment>())
        {
            if (entry.State == EntityState.Added && entry.Entity.PaymentDate == default) entry.Entity.PaymentDate = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
    }

    private void PrepareReservation(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<Reservation>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
    }

    private void PreparePromotion(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<Promotion>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
        }
    }

    private void PrepareAudit(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<Audit>())
        {
            if (entry.State == EntityState.Added && entry.Entity.Timestamp == default) entry.Entity.Timestamp = now;
        }
    }
}
