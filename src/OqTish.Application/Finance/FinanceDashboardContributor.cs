using OqTish.Application.Tenancy;
using OqTish.Domain.Common;

namespace OqTish.Application.Finance;

/// <summary>
/// Clinic dashboard (BE1b-16): <c>collected</c> = today's <b>net</b> money in (payments − failed-payment reversals −
/// refunds) of the context branches, from the ledger. Only when the caller has <c>finance.report.view</c>
/// (<see cref="ClinicDashboardContext.IncludeFinance"/>); otherwise <c>collected</c> stays null.
/// </summary>
public sealed class FinanceDashboardContributor(LedgerTotals totals) : IClinicDashboardContributor
{
    public async Task ContributeAsync(ClinicDashboardContext context, ClinicDashboardCounts counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(counts);
        if (!context.IncludeFinance || counts.Collected is not { } collected)
        {
            return;
        }

        var figures = await totals.ComputeAsync(context.ClinicId, context.BranchIds, context.LocalDate, context.LocalDate, cancellationToken);
        var sum = LedgerTotals.Sum(figures.Values);
        counts.Collected = collected + new Money(sum.Collected - sum.Refunded, collected.Currency);
    }
}
