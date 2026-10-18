using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Domain.Finance;

namespace OqTish.Application.Finance;

/// <summary>
/// Period lock (OD-F6): a month is locked for a clinic once its settlement is Finalized (or Paid). A posting
/// whose reporting day falls into a locked month is <b>redirected</b> to the first day of the next open month,
/// so closed periods are never restated (FINANCIAL_RULES §5).
/// <para>
/// Concurrency: every posting takes the clinic's period advisory lock in <b>shared</b> mode before it reads the
/// lock state; settlement creation and finalization take it <b>exclusively</b>. A posting therefore either
/// commits before a settlement collects/finalizes the month (and is included), or sees the month locked and
/// is redirected — a line can never land unsettled in a finalized month.
/// </para>
/// </summary>
public sealed class PeriodLock(IOqTishDb db, IRowLocks locks)
{
    public static string LockKey(Guid clinicId) => $"finance-period:{clinicId}";

    /// <summary>Shared lock + redirection. Must run inside the posting transaction.</summary>
    public async Task<DateOnly> ResolveAsync(Guid clinicId, DateOnly localDate, CancellationToken ct)
    {
        await locks.AdvisoryLockSharedAsync(LockKey(clinicId), ct);
        var lockedMonths = await db.Settlements.AsNoTracking()
            .Where(s => s.ClinicId == clinicId && (s.Status == SettlementStatus.Finalized || s.Status == SettlementStatus.Paid) && s.PeriodEnd > localDate)
            .Select(s => s.PeriodStart)
            .ToListAsync(ct);
        var date = localDate;
        while (lockedMonths.Contains(FinanceTime.MonthStart(date)))
        {
            date = FinanceTime.MonthStart(date).AddMonths(1);
        }

        return date;
    }

    /// <summary>Exclusive lock for settlement creation/finalization (after the clinic row lock).</summary>
    public Task LockExclusiveAsync(Guid clinicId, CancellationToken ct) => locks.AdvisoryLockAsync(LockKey(clinicId), ct);
}
