using SIGER.Application.Interfaces.Persistence;
using SIGER.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIGER.Infrastructure.UnitOfWork;

public sealed class UnitOfWork(SIGERDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (context.Database.CurrentTransaction is not null)
        {
            await operation(cancellationToken);
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var commitStarted = false;
        try
        {
            await operation(cancellationToken);
            commitStarted = true;
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackAsync(transaction, commitStarted);
            throw;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (context.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var commitStarted = false;
        try
        {
            var result = await operation(cancellationToken);
            commitStarted = true;
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await RollbackAsync(transaction, commitStarted);
            throw;
        }
    }

    private static async Task RollbackAsync(IDbContextTransaction transaction, bool commitStarted)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        // A rejected PostgreSQL COMMIT may already have ended and reverted the transaction.
        // Preserve that original error instead of replacing it with "transaction completed".
        catch (InvalidOperationException) when (commitStarted) { }
    }
}
