using OqTish.Application.Booking;
using OqTish.Application.Common;

namespace OqTish.Application.Finance;

/// <summary>
/// Replaces the booking no-op completion hook (SERVICES.md §3.9): inside the completion transaction creates the
/// ServiceCompletion, the revenue + receivable entry and the CompletedService / NewPatient commission
/// (<see cref="LedgerPoster.PostCompletionAsync"/>). An exception rolls the whole completion back.
/// </summary>
public sealed class FinanceCompletionHandler(LedgerPoster poster, IAuditLog audit) : IAppointmentCompletedHandler
{
    public async Task<Result> OnCompletedAsync(AppointmentCompletedContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var completion = await poster.PostCompletionAsync(context, cancellationToken);
        audit.Record(new AuditEvent("finance.service_completion.created", context.ClinicId, context.BranchId, "service_completion", completion.Id.ToString(),
            new { appointmentId = context.AppointmentId, chargedAmount = context.ChargedAmount.ToAmountString(), postingDate = Common.LocalFormats.Date(completion.PostingDateLocal) }));
        return Result.Success;
    }
}

/// <summary>Confirm hook: the ConfirmedAppointment fixed fee when the clinic has such a rule at the confirmation time.</summary>
public sealed class FinanceConfirmationHandler(LedgerPoster poster) : IAppointmentConfirmedHandler
{
    public async Task<Result> OnConfirmedAsync(AppointmentConfirmedContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await poster.PostConfirmationAsync(context, cancellationToken);
        return Result.Success;
    }
}
