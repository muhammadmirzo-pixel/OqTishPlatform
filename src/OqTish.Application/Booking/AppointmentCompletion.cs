using OqTish.Application.Common;
using OqTish.Domain.Common;

namespace OqTish.Application.Booking;

/// <summary>Facts about a completion, passed to <see cref="IAppointmentCompletedHandler"/>.</summary>
public sealed record AppointmentCompletedContext(
    Guid AppointmentId,
    Guid ClinicId,
    Guid BranchId,
    Guid ServiceId,
    Guid DoctorId,
    Guid? PatientUserId,
    Money ChargedAmount,
    Money QuotedPrice,
    DateTimeOffset StartsAt,
    DateTimeOffset CompletedAt,
    Guid ActorUserId);

/// <summary>
/// Hook for Finance (BE3): invoked by <c>POST …/appointments/{id}/complete</c> <b>inside the completion
/// transaction</b>, after the appointment row was switched to Completed and its block released, before
/// commit. BE3 creates the ServiceCompletion and the revenue ledger entry here; returning a failure rolls the
/// whole completion back (the error is returned to the caller). The default is a no-op; BE3 replaces the
/// registration (<c>services.Replace(ServiceDescriptor.Scoped&lt;IAppointmentCompletedHandler, …&gt;())</c>).
/// Implementations must use the same <c>IOqTishDb</c> (scoped) and must not open their own transaction.
/// </summary>
public interface IAppointmentCompletedHandler
{
    Task<Result> OnCompletedAsync(AppointmentCompletedContext context, CancellationToken cancellationToken);
}

/// <summary>Default until BE3: nothing beyond <c>charged_amount</c> on the appointment.</summary>
internal sealed class NoOpAppointmentCompletedHandler : IAppointmentCompletedHandler
{
    public Task<Result> OnCompletedAsync(AppointmentCompletedContext context, CancellationToken cancellationToken) => Task.FromResult(Result.Success);
}

/// <summary>Facts about a staff confirmation (Pending → Confirmed), passed to <see cref="IAppointmentConfirmedHandler"/>.</summary>
public sealed record AppointmentConfirmedContext(
    Guid AppointmentId,
    Guid ClinicId,
    Guid BranchId,
    Guid ServiceId,
    Guid DoctorId,
    Guid? PatientUserId,
    DateTimeOffset ConfirmedAt,
    Guid ActorUserId);

/// <summary>
/// Hook for Finance (BE3, ConfirmedAppointment commission basis): invoked by <c>POST …/appointments/{id}/confirm</c>
/// <b>inside the confirm transaction</b>, after the conditional UPDATE to Confirmed succeeded, before history,
/// notifications and commit. A failure result rolls the confirmation back. Same rules as
/// <see cref="IAppointmentCompletedHandler"/> (same scoped <c>IOqTishDb</c>, no own transaction). Walk-ins are
/// created Confirmed without this hook (they are not platform-originated bookings). Default: no-op.
/// </summary>
public interface IAppointmentConfirmedHandler
{
    Task<Result> OnConfirmedAsync(AppointmentConfirmedContext context, CancellationToken cancellationToken);
}

internal sealed class NoOpAppointmentConfirmedHandler : IAppointmentConfirmedHandler
{
    public Task<Result> OnConfirmedAsync(AppointmentConfirmedContext context, CancellationToken cancellationToken) => Task.FromResult(Result.Success);
}
