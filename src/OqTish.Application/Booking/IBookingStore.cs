using OqTish.Domain.Scheduling;

namespace OqTish.Application.Booking;

public enum BlockMoveResult
{
    Moved = 1,

    /// <summary>The exclusion constraint rejected the new period/doctor (23P01).</summary>
    Conflict = 2,

    /// <summary>The appointment has no active block.</summary>
    NotFound = 3,
}

/// <summary>
/// Booking writes that need PostgreSQL specifics (savepoints, exclusion-violation handling, row-lock
/// queries). Implemented in Infrastructure. Every method must run inside <c>ITransactionRunner</c>.
/// </summary>
public interface IBookingStore
{
    /// <summary>
    /// <c>SAVEPOINT; INSERT</c> the block; on 23P01 (<c>ex_doctor_time_blocks_no_overlap</c>) rolls back to the
    /// savepoint and returns false, leaving the transaction usable. Any other error propagates.
    /// </summary>
    Task<bool> TryInsertBlockAsync(DoctorTimeBlock block, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the appointment's block (its single row, <c>appointment_id</c> UNIQUE) onto a doctor/period/kind and
    /// marks it active, in one UPDATE under a savepoint; the exclusion constraint re-checks the new values.
    /// </summary>
    Task<BlockMoveResult> TryMoveBlockAsync(Guid appointmentId, Guid doctorId, DateTimeOffset startsAt, DateTimeOffset endsAt, TimeBlockKind kind, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Deactivates the active blocks of the appointments (release). Returns the number of rows changed.</summary>
    Task<int> ReleaseBlocksAsync(IReadOnlyCollection<Guid> appointmentIds, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Locks (<c>FOR UPDATE</c>, or <c>FOR UPDATE SKIP LOCKED</c> for the sweeper) and returns ids of Pending
    /// appointments whose deadline passed by database time (<c>expires_at &lt;= statement_timestamp()</c>),
    /// ordered by id. Filter: the branch, or an active hold of one of the doctors; neither = all branches.
    /// <paramref name="excludeAppointmentId"/> is never selected (the request a confirm is working on).
    /// </summary>
    Task<IReadOnlyList<Guid>> LockOverduePendingAsync(Guid? branchId, IReadOnlyCollection<Guid> doctorIds, bool skipLocked, int limit, CancellationToken cancellationToken, Guid? excludeAppointmentId = null);
}
