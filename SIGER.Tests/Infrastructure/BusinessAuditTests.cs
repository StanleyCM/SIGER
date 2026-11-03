using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Moq;
using SIGER.Application.DTOs.Auth;
using SIGER.Application.Exceptions;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Application.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Infrastructure.Persistence;
using SIGER.Infrastructure.Repositories;
using Work = SIGER.Infrastructure.UnitOfWork.UnitOfWork;

namespace SIGER.Tests.Infrastructure;

public class BusinessAuditTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Create_commits_audit_with_generated_ID_and_actor(bool sync)
    {
        using var f = new Fixture(); f.Actor.UserId = 1; f.Actor.IpAddress = "127.0.0.1";
        var category = new Category { Name = "SECRET-TEXT", IsActive = true }; f.Db.Add(category);
        if (sync) f.Db.SaveChanges(); else await f.Db.SaveChangesAsync();
        var audit = Assert.Single(await f.Db.Audits.ToArrayAsync());
        Assert.True(category.Id > 0); Assert.Equal(category.Id, audit.EntityId); Assert.Equal("Create", audit.Action);
        Assert.Equal("Category", audit.Entity); Assert.Equal(1, audit.UserId); Assert.Equal("127.0.0.1", audit.IpAddress);
        Assert.Null(audit.PreviousData); Assert.True(audit.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
        using var json = JsonDocument.Parse(audit.NewData!);
        Assert.Equal(category.Id, json.RootElement.GetProperty("Values").GetProperty("Id").GetInt64());
        Assert.DoesNotContain("SECRET-TEXT", audit.NewData);
    }

    [Fact]
    public async Task Profile_free_text_and_secrets_are_never_serialized()
    {
        using var f = new Fixture();
        const string secret = "password JWT refresh_token Authorization sb_secret_ ConnectionString PRIVATE";
        var user = new User { Email = secret, FirstName = secret, LastName = secret, Phone = secret, AuthUserId = Guid.NewGuid() };
        f.Db.Add(user); f.Db.Add(new Product { Name = secret, Description = secret, ImageUrl = secret });
        f.Db.Add(new Payment { Reference = secret }); f.Db.Add(new Order { Notes = secret });
        await f.Db.SaveChangesAsync(); user.Email = "changed@example.test"; await f.Db.SaveChangesAsync();
        foreach (var a in await f.Db.Audits.ToArrayAsync())
        {
            var text = a.PreviousData + a.NewData; Assert.DoesNotContain(secret, text);
            Assert.DoesNotContain(user.AuthUserId.ToString(), text); Assert.DoesNotContain("changed@example.test", text);
        }
        Assert.Contains(await f.Db.Audits.ToArrayAsync(), a => a.Action == "EmailChange" && a.NewData!.Contains("Email"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Audit_failure_rolls_back_business_even_if_outer_transaction_catches(bool outer)
    {
        using var f = new Fixture(); await using var tx = outer ? await f.Db.Database.BeginTransactionAsync() : null;
        f.Commands.FailAudit = true;
        f.Db.Add(new Category { Name = "temporary" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        if (tx is not null) await tx.CommitAsync();
        Assert.Empty(await f.Db.Categories.ToArrayAsync());
        Assert.Empty(await f.Db.Audits.ToArrayAsync());
    }

    [Fact]
    public async Task Outer_rollback_removes_business_and_audit()
    {
        using var f = new Fixture(); await using var tx = await f.Db.Database.BeginTransactionAsync();
        f.Db.Add(new Category { Name = "temporary" }); await f.Db.SaveChangesAsync();
        Assert.Single(await f.Db.Audits.AsNoTracking().ToArrayAsync()); await tx.RollbackAsync();
        Assert.Empty(await f.Db.Categories.AsNoTracking().ToArrayAsync()); Assert.Empty(await f.Db.Audits.AsNoTracking().ToArrayAsync());
    }

    private sealed class Actor : IAuditActor { public long? UserId { get; set; } public string? IpAddress { get; set; } }

    private sealed class Counter : DbCommandInterceptor
    {
        public int AuditInserts;
        public bool FailAudit;
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"auditoria\"") && FailAudit) throw new InvalidOperationException("Injected audit failure.");
            return result;
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"auditoria\""))
            {
                AuditInserts++;
                if (FailAudit) throw new InvalidOperationException("Injected audit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=False");
        public Actor Actor { get; } = new(); public Counter Commands { get; } = new();
        public TestContext Db { get; }
        public Fixture()
        {
            connection.Open();
            Db = new TestContext(new DbContextOptionsBuilder<SIGERDbContext>().UseSqlite(connection).AddInterceptors(Commands).Options, Actor);
            Db.Database.EnsureCreated(); // Isolated SQLite only. Never calls Supabase or uses secrets.
        }
        public void Dispose() { Db.Dispose(); connection.Dispose(); }
    }
    private sealed class TestContext(DbContextOptions<SIGERDbContext> options, IAuditActor actor) : SIGERDbContext(options, actor)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            // Adapt provider-specific storage for transactional tests; production Npgsql model remains tested separately.
            foreach (var e in b.Model.GetEntityTypes())
            foreach (var p in e.GetProperties())
            {
                if (p.ClrType == typeof(long)) p.SetColumnType("INTEGER");
                if (p.ClrType == typeof(DateTimeOffset)) p.SetColumnType("TEXT");
            }
            b.Entity<Audit>().Property(a => a.IpAddress).HasConversion(new ValueConverter<string?, string?>(v => v, v => v)).HasColumnType("TEXT");
        }
    }
}
