namespace OqTish.Application.Common;

/// <summary>
/// Pessimistic locks (<c>SELECT … FOR UPDATE</c>). Must be called inside <see cref="ITransactionRunner"/>;
/// calling outside a transaction throws. Lock order is always <b>branch → appointment → others</b>
/// (CONVENTIONS §4) to avoid deadlocks.
/// </summary>
public interface IRowLocks
{
    /// <summary>Locks the branch's row in <c>tenancy.branch_booking_locks</c>. Returns false if the branch does not exist.</summary>
    Task<bool> LockBranchAsync(Guid branchId, CancellationToken cancellationToken);

    /// <summary>
    /// Locks one row of an entity table by primary key without loading it. Returns false when no row
    /// matches. Works for any mapped entity with a single-column key.
    /// </summary>
    Task<bool> LockAsync<TEntity>(object key, CancellationToken cancellationToken)
        where TEntity : class;

    /// <summary>
    /// Locks and loads (tracked) one entity by primary key: <c>SELECT * … WHERE pk = @key FOR UPDATE</c>.
    /// </summary>
    Task<TEntity?> LoadForUpdateAsync<TEntity>(object key, CancellationToken cancellationToken)
        where TEntity : class;

    /// <summary>Transaction-scoped advisory lock on an arbitrary key (e.g. "otp:&lt;identifier hash&gt;").</summary>
    Task AdvisoryLockAsync(string key, CancellationToken cancellationToken);
}
