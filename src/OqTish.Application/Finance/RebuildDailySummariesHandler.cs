using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Tenancy;
using OqTish.Domain.Finance;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Finance;

/// <summary>A cached day that differed from the ledger (and was overwritten).</summary>
public sealed record SummaryMismatch(Guid ClinicId, Guid BranchId, DateOnly Date, DailyTotals Stored, DailyTotals Ledger);

public sealed record SummaryRebuildResult(DateOnly From, DateOnly To, int Written, IReadOnlyList<SummaryMismatch> Mismatches);

/// <summary>
/// Nightly summary rebuild + reconciliation (FINANCIAL_RULES §7, run by the Worker's <c>FinanceSummaryService</c>):
/// for every (branch, branch-local day) of the trailing window (<c>Finance:SummaryWindowDays</c>, default 35 days
/// up to today in Asia/Tashkent, database time) the figures are recomputed from the ledger
/// (<see cref="LedgerTotals"/>) and written to <c>finance.daily_financial_summaries</c>. A stored row that differs
/// from the ledger is a <b>mismatch</b>: it is overwritten, an audit row <c>finance.summary.mismatch</c> is written
/// (ids, date and both figure sets) and the caller logs a warning. One transaction, serialised by an advisory lock.
/// </summary>
public sealed class RebuildDailySummariesHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IDbClock clock,
    LedgerTotals totals,
    IAuditLog audit,
    IOptions<FinanceOptions> options)
{
    public async Task<SummaryRebuildResult> RunAsync(CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var result = await transactions.RunAsync<SummaryRebuildResult>(async t =>
        {
            await locks.AdvisoryLockAsync("finance-summary-rebuild", t);
            var now = await clock.NowAsync(t);
            var today = TenancyRules.LocalToday(now, Branch.DefaultTimeZone);
            var last = to ?? today;
            var first = from ?? last.AddDays(-(Math.Max(1, options.Value.SummaryWindowDays) - 1));
            var ledger = await totals.ComputeAsync(null, null, first, last, t);
            var stored = await db.DailyFinancialSummaries.Where(s => s.LocalDate >= first && s.LocalDate <= last).ToListAsync(t);
            var byKey = stored.ToDictionary(s => new DayKey(s.ClinicId, s.BranchId, s.LocalDate));
            var mismatches = new List<SummaryMismatch>();
            var written = 0;
            foreach (var key in ledger.Keys.Union(byKey.Keys))
            {
                var figures = ledger.TryGetValue(key, out var f) ? f : new DayFigures(DailyTotals.Zero, 0);
                if (byKey.TryGetValue(key, out var row))
                {
                    if (row.Totals != figures.Totals)
                    {
                        mismatches.Add(new SummaryMismatch(key.ClinicId, key.BranchId, key.Date, row.Totals, figures.Totals));
                        audit.Record(new AuditEvent("finance.summary.mismatch", key.ClinicId, key.BranchId, "daily_financial_summary", $"{key.BranchId}:{LocalFormats.Date(key.Date)}",
                            new { date = LocalFormats.Date(key.Date), stored = row.Totals, ledger = figures.Totals }));
                        written++;
                    }

                    row.Overwrite(figures.Totals, figures.Watermark, now);
                }
                else if (!figures.Totals.IsZero)
                {
                    db.DailyFinancialSummaries.Add(DailyFinancialSummary.Create(key.ClinicId, key.BranchId, key.Date, figures.Totals, figures.Watermark, now));
                    written++;
                }
            }

            await db.SaveChangesAsync(t);
            return new SummaryRebuildResult(first, last, written, mismatches);
        }, ct);
        return result.IsSuccess ? result.Value : new SummaryRebuildResult(from ?? default, to ?? default, 0, []);
    }
}
