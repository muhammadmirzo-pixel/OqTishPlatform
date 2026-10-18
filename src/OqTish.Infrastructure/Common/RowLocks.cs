using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Domain.Tenancy;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Infrastructure.Common;

/// <summary><c>SELECT … FOR UPDATE</c> helpers built from EF metadata (table/key names are never user input).</summary>
internal sealed class RowLocks(OqTishDbContext db) : IRowLocks
{
    public Task<bool> LockBranchAsync(Guid branchId, CancellationToken cancellationToken) =>
        LockAsync<BranchBookingLock>(branchId, cancellationToken);

    public async Task<bool> LockAsync<TEntity>(object key, CancellationToken cancellationToken)
        where TEntity : class
    {
        EnsureTransaction();
        var (table, keyColumn) = Describe<TEntity>();
#pragma warning disable EF1002 // Table/column names come from EF metadata and are quoted; the key is a parameter.
        var rows = await db.Database
            .SqlQueryRaw<int>($"SELECT 1 AS \"Value\" FROM {table} WHERE {keyColumn} = {{0}} FOR UPDATE", key)
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002
        return rows.Count > 0;
    }

    public async Task<TEntity?> LoadForUpdateAsync<TEntity>(object key, CancellationToken cancellationToken)
        where TEntity : class
    {
        EnsureTransaction();
        var (table, keyColumn) = Describe<TEntity>();
#pragma warning disable EF1002 // See LockAsync.
        var rows = await db.Set<TEntity>()
            .FromSqlRaw($"SELECT * FROM {table} WHERE {keyColumn} = {{0}} FOR UPDATE", key)
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002
        return rows.SingleOrDefault();
    }

    public async Task AdvisoryLockAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        EnsureTransaction();
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public async Task AdvisoryLockSharedAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        EnsureTransaction();
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared(hashtextextended({key}, 0))", cancellationToken);
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Row locks require an explicit transaction (ITransactionRunner).");
        }
    }

    private (string Table, string KeyColumn) Describe<TEntity>()
        where TEntity : class
    {
        var entity = db.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped.");
        var pk = entity.FindPrimaryKey() ?? throw new InvalidOperationException($"{typeof(TEntity).Name} has no primary key.");
        if (pk.Properties.Count != 1)
        {
            throw new InvalidOperationException($"{typeof(TEntity).Name} has a composite key; lock it with a dedicated method.");
        }

        var tableName = entity.GetTableName()!;
        var schema = entity.GetSchema();
        var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(tableName, schema);
        var column = pk.Properties[0].GetColumnName(store)!;
        var table = schema is null ? Quote(tableName) : $"{Quote(schema)}.{Quote(tableName)}";
        return (table, Quote(column));
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
