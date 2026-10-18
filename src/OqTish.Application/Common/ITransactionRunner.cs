using System.Data;

namespace OqTish.Application.Common;

/// <summary>
/// Runs a unit of work in one explicit database transaction (ADR-005).
/// <list type="bullet">
/// <item>Isolation defaults to READ COMMITTED; correctness comes from row locks and constraints.</item>
/// <item>The work commits only when it returns success; a failure <see cref="Result"/> rolls everything back,
/// so a business error never leaves partial writes.</item>
/// <item>Only serialization failures (40001) and deadlocks (40P01) are retried, at most
/// <c>maxAttempts</c> times in total, with jittered backoff. The change tracker is cleared between
/// attempts, so the work delegate must (re)load everything it needs. Exhaustion returns
/// <see cref="CommonErrors.Concurrency"/> (409 <c>common.concurrency</c>).</item>
/// <item>A nested call joins the ambient transaction (no savepoint, no retry of its own).</item>
/// </list>
/// Audit rows and outbox messages written inside the work commit atomically with it.
/// </summary>
public interface ITransactionRunner
{
    Task<Result<T>> RunAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        int maxAttempts = 3);

    Task<Result> RunAsync(
        Func<CancellationToken, Task<Result>> work,
        CancellationToken cancellationToken,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        int maxAttempts = 3);
}
