using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using SIGER.Infrastructure.Persistence;
using Work = SIGER.Infrastructure.UnitOfWork.UnitOfWork;

namespace SIGER.Tests.Infrastructure;

public class UnitOfWorkTests
{
    private readonly Mock<SIGERDbContext> context = new(new DbContextOptionsBuilder<SIGERDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=never_connect").Options);
    private readonly Mock<IDbContextTransaction> transaction = new();
    private readonly Mock<DatabaseFacade> database;
    public UnitOfWorkTests()
    {
        database = new(context.Object);
        context.SetupGet(x => x.Database).Returns(database.Object);
        database.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Transaction_commits_and_disposes_after_success(bool generic)
    {
        using var cts = new CancellationTokenSource();
        var work = new Work(context.Object);
        if (generic) Assert.Equal(42, await work.ExecuteInTransactionAsync(t => { Assert.Equal(cts.Token, t); return Task.FromResult(42); }, cts.Token));
        else await work.ExecuteInTransactionAsync(t => { Assert.Equal(cts.Token, t); return Task.CompletedTask; }, cts.Token);
        transaction.Verify(x => x.CommitAsync(cts.Token), Times.Once);
        transaction.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Transaction_rolls_back_and_preserves_operation_error(bool generic)
    {
        var failure = new InvalidOperationException("simulated");
        var work = new Work(context.Object);
        var actual = generic
            ? await Assert.ThrowsAsync<InvalidOperationException>(() => work.ExecuteInTransactionAsync<int>(_ => Task.FromException<int>(failure)))
            : await Assert.ThrowsAsync<InvalidOperationException>(() => work.ExecuteInTransactionAsync(_ => Task.FromException(failure)));
        Assert.Same(failure, actual);
        transaction.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
        transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Nested_work_does_not_commit_owners_transaction()
    {
        database.SetupGet(x => x.CurrentTransaction).Returns(transaction.Object);
        var work = new Work(context.Object);
        await work.ExecuteInTransactionAsync(_ => Task.CompletedTask);
        Assert.Equal(1, await work.ExecuteInTransactionAsync(_ => Task.FromResult(1)));
        database.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

}
