using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Domain.Finance;

namespace OqTish.Application.Finance;

/// <summary>Key of one branch-local reporting day.</summary>
public readonly record struct DayKey(Guid ClinicId, Guid BranchId, DateOnly Date);

/// <summary>Ledger figures of one day plus the highest journal entry sequence included.</summary>
public sealed record DayFigures(DailyTotals Totals, long Watermark);

/// <summary>
/// The single definition of the reported figures, computed <b>from the ledger</b> (FINANCIAL_RULES §7) and used by
/// the reports, the dashboard and the nightly summary rebuild. Per (branch, branch-local day):
/// <list type="bullet">
/// <item><c>visits</c> = service completions;</item>
/// <item><c>charged</c> = −Σ ClinicServiceRevenue of ServiceCompleted entries (gross);</item>
/// <item><c>collected</c> = Σ ClinicCollected{Cash,Card,Transfer} (payments minus failed-payment reversals);</item>
/// <item><c>refunded</c> = −Σ ClinicRefundsPaid; <c>priorPeriodRefunded</c> = the part whose original payment was posted in an earlier month (F9);</item>
/// <item><c>commission</c> = −Σ PlatformCommissionRevenue (accruals minus reversals).</item>
/// </list>
/// Clinic-level entries (no branch: settlement paid) are not part of any day.
/// </summary>
public sealed class LedgerTotals(IOqTishDb db)
{
    private static readonly LedgerAccount[] Collected = [LedgerAccount.ClinicCollectedCash, LedgerAccount.ClinicCollectedCard, LedgerAccount.ClinicCollectedTransfer];

    public async Task<Dictionary<DayKey, DayFigures>> ComputeAsync(Guid? clinicId, IReadOnlyCollection<Guid>? branchIds, DateOnly fromDate, DateOnly toInclusive, CancellationToken ct)
    {
        var ids = branchIds?.ToList();
        var lines =
            from l in db.LedgerLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where e.BranchId != null && e.PostingDateLocal >= fromDate && e.PostingDateLocal <= toInclusive
            select new { e.ClinicId, BranchId = e.BranchId!.Value, e.PostingDateLocal, e.EntryType, l.Account, l.Amount, e.Sequence };
        var priorLines =
            from l in db.LedgerLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            join r in db.Payments.AsNoTracking() on e.PaymentId equals r.Id
            join o in db.JournalEntries.AsNoTracking() on r.RefundOfPaymentId equals o.PaymentId
            where e.EntryType == JournalEntryType.PaymentRefunded && l.Account == LedgerAccount.ClinicRefundsPaid && o.EntryType == JournalEntryType.PaymentRecorded
                  && e.BranchId != null && e.PostingDateLocal >= fromDate && e.PostingDateLocal <= toInclusive
                  && (o.PostingDateLocal.Year * 12) + o.PostingDateLocal.Month < (e.PostingDateLocal.Year * 12) + e.PostingDateLocal.Month
            select new { e.ClinicId, BranchId = e.BranchId!.Value, e.PostingDateLocal, l.Amount };
        var completions = db.ServiceCompletions.AsNoTracking().Where(c => c.PostingDateLocal >= fromDate && c.PostingDateLocal <= toInclusive);
        if (clinicId is { } clinic)
        {
            lines = lines.Where(x => x.ClinicId == clinic);
            priorLines = priorLines.Where(x => x.ClinicId == clinic);
            completions = completions.Where(x => x.ClinicId == clinic);
        }

        if (ids is not null)
        {
            lines = lines.Where(x => ids.Contains(x.BranchId));
            priorLines = priorLines.Where(x => ids.Contains(x.BranchId));
            completions = completions.Where(x => ids.Contains(x.BranchId));
        }

        var grouped = await lines
            .GroupBy(x => new { x.ClinicId, x.BranchId, x.PostingDateLocal, x.EntryType, x.Account })
            .Select(g => new { g.Key.ClinicId, g.Key.BranchId, g.Key.PostingDateLocal, g.Key.EntryType, g.Key.Account, Sum = g.Sum(x => x.Amount), MaxSeq = g.Max(x => x.Sequence) })
            .ToListAsync(ct);
        var prior = await priorLines
            .GroupBy(x => new { x.ClinicId, x.BranchId, x.PostingDateLocal })
            .Select(g => new { g.Key.ClinicId, g.Key.BranchId, g.Key.PostingDateLocal, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var visits = await completions
            .GroupBy(c => new { c.ClinicId, c.BranchId, c.PostingDateLocal })
            .Select(g => new { g.Key.ClinicId, g.Key.BranchId, g.Key.PostingDateLocal, Count = g.Count() })
            .ToListAsync(ct);

        var result = new Dictionary<DayKey, DayFigures>();
        DayFigures Get(DayKey key) => result.TryGetValue(key, out var f) ? f : new DayFigures(DailyTotals.Zero, 0);

        foreach (var g in grouped)
        {
            var key = new DayKey(g.ClinicId, g.BranchId, g.PostingDateLocal);
            var add = DailyTotals.Zero with
            {
                Charged = g.Account == LedgerAccount.ClinicServiceRevenue && g.EntryType == JournalEntryType.ServiceCompleted ? -g.Sum : 0m,
                Collected = Collected.Contains(g.Account) ? g.Sum : 0m,
                Refunded = g.Account == LedgerAccount.ClinicRefundsPaid ? -g.Sum : 0m,
                Commission = g.Account == LedgerAccount.PlatformCommissionRevenue ? -g.Sum : 0m,
            };
            var current = Get(key);
            result[key] = new DayFigures(current.Totals.Plus(add), Math.Max(current.Watermark, g.MaxSeq));
        }

        foreach (var p in prior)
        {
            var key = new DayKey(p.ClinicId, p.BranchId, p.PostingDateLocal);
            var current = Get(key);
            result[key] = current with { Totals = current.Totals.Plus(DailyTotals.Zero with { PriorPeriodRefunded = -p.Sum }) };
        }

        foreach (var v in visits)
        {
            var key = new DayKey(v.ClinicId, v.BranchId, v.PostingDateLocal);
            var current = Get(key);
            result[key] = current with { Totals = current.Totals.Plus(DailyTotals.Zero with { Visits = v.Count }) };
        }

        return result;
    }

    /// <summary>Σ over all keys (one figure set for a range / branch set).</summary>
    public static DailyTotals Sum(IEnumerable<DayFigures> figures) => figures.Aggregate(DailyTotals.Zero, (acc, f) => acc.Plus(f.Totals));
}
