namespace OqTish.Domain.Tenancy;

/// <summary>
/// The row locked (<c>SELECT … FOR UPDATE</c>) to serialise booking decisions per branch (ADR-004).
/// Created automatically by a database trigger when a branch is inserted; never insert it manually.
/// Use <c>IRowLocks.LockBranchAsync</c>.
/// </summary>
public sealed class BranchBookingLock
{
    private BranchBookingLock()
    {
    }

    public Guid BranchId { get; private set; }

    public Guid ClinicId { get; private set; }
}
