using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;

namespace OqTish.Application.Finance;

/// <summary>
/// Clinic finance reports (docs/API.md §4.3), <c>finance.report.view</c> (Owner, FinanceManager; Branches-scoped
/// managers see their branches only) or a platform admin with <c>platform.finance.settle</c>. Figures come
/// <b>from the ledger</b> (<see cref="LedgerTotals"/>), never from the cache; the monthly report compares the
/// cache (<c>daily_financial_summaries</c>) with the ledger for its <c>reconciled</c> flag.
/// </summary>
public sealed class FinanceReportQueries(IOqTishDb db, FinanceAccess financeAccess, LedgerTotals totals, IOptions<FinanceOptions> options)
{
    /// <summary><c>?branchId&amp;from&amp;to</c>: branch-local dates YYYY-MM-DD, inclusive, one row per day (zeros included).</summary>
    public async Task<Result<IReadOnlyList<DailyReportRowDto>>> DailyAsync(Guid clinicId, Guid? branchId, string? from, string? to, CancellationToken ct)
    {
        var errors = new FieldErrors();
        if (!FinanceTime.TryParseDate(from, out var fromDate))
        {
            errors.Add("from", "Required, YYYY-MM-DD (branch-local date).");
        }

        if (!FinanceTime.TryParseDate(to, out var toDate))
        {
            errors.Add("to", "Required, YYYY-MM-DD (branch-local date, inclusive).");
        }

        if (!errors.HasErrors && toDate < fromDate)
        {
            errors.Add("to", "Must not be before 'from'.");
        }

        var maxDays = Math.Max(1, options.Value.DailyReportMaxDays);
        if (!errors.HasErrors && toDate.DayNumber - fromDate.DayNumber + 1 > maxDays)
        {
            errors.Add("to", $"At most {maxDays} days.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var branches = await financeAccess.ReportBranchesAsync(clinicId, branchId, Permissions.FinanceReportView, ct);
        if (!branches.IsSuccess)
        {
            return branches.Error;
        }

        var figures = await totals.ComputeAsync(clinicId, branches.Value, fromDate, toDate, ct);
        var rows = new List<DailyReportRowDto>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var day = d;
            var sum = LedgerTotals.Sum(figures.Where(kv => kv.Key.Date == day).Select(kv => kv.Value));
            rows.Add(new DailyReportRowDto(LocalFormats.Date(day), branchId, sum.Visits, Uzs(sum.Charged), Uzs(sum.Collected), Uzs(sum.Refunded), Uzs(sum.Commission),
                Uzs(sum.PriorPeriodRefunded)));
        }

        return rows;
    }

    /// <summary><c>?year&amp;month&amp;branchId</c>: totals of the branch-local month, the reconciliation flag and the contract flag.</summary>
    public async Task<Result<MonthlyReportDto>> MonthlyAsync(Guid clinicId, int? year, int? month, Guid? branchId, CancellationToken ct)
    {
        var errors = new FieldErrors();
        if (year is not (>= 2020 and <= 2100))
        {
            errors.Add("year", "Must be between 2020 and 2100.");
        }

        if (month is not (>= 1 and <= 12))
        {
            errors.Add("month", "Must be between 1 and 12.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var branches = await financeAccess.ReportBranchesAsync(clinicId, branchId, Permissions.FinanceReportView, ct);
        if (!branches.IsSuccess)
        {
            return branches.Error;
        }

        var start = new DateOnly(year!.Value, month!.Value, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var ids = branches.Value.ToList();
        var ledger = await totals.ComputeAsync(clinicId, ids, start, end, ct);
        var cache = await db.DailyFinancialSummaries.AsNoTracking()
            .Where(s => s.ClinicId == clinicId && ids.Contains(s.BranchId) && s.LocalDate >= start && s.LocalDate <= end)
            .ToListAsync(ct);
        var cached = cache.ToDictionary(s => new DayKey(s.ClinicId, s.BranchId, s.LocalDate), s => s.Totals);
        var reconciled = ledger.Keys.Union(cached.Keys).All(k =>
            (ledger.TryGetValue(k, out var l) ? l.Totals : DailyTotals.Zero) == (cached.TryGetValue(k, out var c) ? c : DailyTotals.Zero));

        var (from, to) = FinanceTime.MonthRange(start.Year, start.Month);
        var configured = await db.CommissionRules.AsNoTracking().AnyAsync(r => r.ClinicId == clinicId && r.EffectiveFrom < to && (r.EffectiveTo == null || r.EffectiveTo > from), ct);
        var locked = await db.Settlements.AsNoTracking().AnyAsync(s => s.ClinicId == clinicId && s.PeriodStart == start
            && (s.Status == SettlementStatus.Finalized || s.Status == SettlementStatus.Paid), ct);
        var sum = LedgerTotals.Sum(ledger.Values);
        return new MonthlyReportDto(start.Year, start.Month, branchId, sum.Visits, Uzs(sum.Charged), Uzs(sum.Collected), Uzs(sum.Refunded), Uzs(sum.Commission),
            Uzs(sum.PriorPeriodRefunded), reconciled, configured, locked);
    }

    private static Money Uzs(decimal amount) => new(amount, Currency.UZS);
}
