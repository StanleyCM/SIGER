using Microsoft.EntityFrameworkCore;
using SIGER.Domain.Entities;
using SIGER.Application.Interfaces.Persistence;

namespace SIGER.Infrastructure.Persistence;

public class SIGERDbContext : DbContext
{
    private readonly IAuditActor? auditActor;
    public SIGERDbContext(DbContextOptions<SIGERDbContext> options)
        : this(options, null)
    {
    }

    public SIGERDbContext(DbContextOptions<SIGERDbContext> options, IAuditActor? auditActor) : base(options)
        => this.auditActor = auditActor;

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
        var drafts = BusinessAudit.Capture(ChangeTracker, auditActor);
        PrepareTrackedEntities();
        if (drafts.Count == 0) return base.SaveChanges(acceptAllChangesOnSuccess);
        using var owned = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        var transaction = Database.CurrentTransaction!;
        var savepoint = "siger_audit_" + Guid.NewGuid().ToString("N");
        if (owned is null) transaction.CreateSavepoint(savepoint);
        try
        {
            var count = base.SaveChanges(false);
            if (count > 0)
                foreach (var command in BusinessAudit.Commands(this, drafts)) Database.ExecuteSqlRaw(command.Sql, command.Parameters);
            if (owned is not null) owned.Commit(); else transaction.ReleaseSavepoint(savepoint);
            if (acceptAllChangesOnSuccess && count > 0) ChangeTracker.AcceptAllChanges();
            return count;
        }
        catch
        {
            try { if (owned is not null) owned.Rollback(); else transaction.RollbackToSavepoint(savepoint); }
            finally { ChangeTracker.Clear(); }
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var drafts = BusinessAudit.Capture(ChangeTracker, auditActor);
        PrepareTrackedEntities();
        if (drafts.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        await using var owned = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = Database.CurrentTransaction!;
        var savepoint = "siger_audit_" + Guid.NewGuid().ToString("N");
        if (owned is null) await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            var count = await base.SaveChangesAsync(false, cancellationToken);
            if (count > 0)
                foreach (var command in BusinessAudit.Commands(this, drafts))
                    await Database.ExecuteSqlRawAsync(command.Sql, command.Parameters, cancellationToken);
            if (owned is not null) await owned.CommitAsync(cancellationToken); else await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            if (acceptAllChangesOnSuccess && count > 0) ChangeTracker.AcceptAllChanges();
            return count;
        }
        catch
        {
            try
            {
                if (owned is not null) await owned.RollbackAsync(CancellationToken.None);
                else await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            }
            finally { ChangeTracker.Clear(); }
            throw;
        }
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
