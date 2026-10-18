namespace OqTish.Application.Common;

/// <summary>
/// Database time. Booking rules (deadlines, lead time, cancellation cutoffs, outcome times) are evaluated
/// against the database clock so that every API/Worker instance agrees (ADR-006); application clocks are
/// used only for created/updated stamps elsewhere.
/// </summary>
public interface IDbClock
{
    /// <summary><c>SELECT clock_timestamp()</c> — the current database time (not the transaction start).</summary>
    Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken);
}

/// <summary>
/// SQL functions usable inside LINQ predicates (mapped in <c>OqTishDbContext</c>). Calling them in memory throws.
/// </summary>
public static class DbTime
{
    /// <summary>
    /// <c>statement_timestamp()</c>: the start of the current statement. Deadline predicates use it instead of
    /// <c>now()</c>, which is frozen at transaction start and would accept a write that waited on a lock past
    /// the deadline.
    /// </summary>
    public static DateTimeOffset StatementTimestamp() => throw new InvalidOperationException("DbTime.StatementTimestamp() is translated to SQL only.");
}
