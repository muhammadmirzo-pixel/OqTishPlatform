using System.Text.Json.Serialization;
using OqTish.Domain.Booking;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;
using OqTish.Domain.Scheduling;

namespace OqTish.Application.Booking;

public sealed record AppointmentDoctorDto(Guid Id, string Name);

public sealed record AppointmentHistoryDto(AppointmentStatus? From, AppointmentStatus To, DateTimeOffset At, HistoryActorKind Actor, string? ReasonCode);

/// <summary>
/// docs/API.md <c>AppointmentDto</c>. An overdue Pending request is presented as <c>Expired</c>. Additive
/// fields: <c>priceKind</c>, <c>hasReview</c>, <c>canReview</c>. <c>history</c> is present on detail endpoints only.
/// </summary>
public sealed record AppointmentDto(
    Guid Id,
    Guid ClinicId,
    string ClinicName,
    Guid BranchId,
    string BranchName,
    string TimeZone,
    Guid ServiceId,
    string ServiceName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    AppointmentStatus Status,
    DateTimeOffset? ExpiresAt,
    Money QuotedPrice,
    PriceKind PriceKind,
    Money? ChargedAmount,
    AppointmentDoctorDto? Doctor,
    string ContactName,
    string ContactPhone,
    AppointmentSource Source,
    bool CanCancel,
    bool HasReview,
    bool CanReview,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AppointmentHistoryDto>? History);

public sealed record CreateAppointmentCommand(Guid? BranchId, Guid? ServiceId, DateTimeOffset? StartsAt, string? ContactName, string? ContactPhone);

public sealed record WalkInCommand(Guid? BranchId, Guid? ServiceId, DateTimeOffset? StartsAt, Guid? DoctorId, string? ContactName, string? ContactPhone);

public sealed record ConfirmCommand(Guid? DoctorId);

public sealed record ReasonCommand(string? ReasonCode);

public sealed record RescheduleCommand(DateTimeOffset? StartsAt, Guid? DoctorId);

public sealed record CompleteCommand(Money? ChargedAmount);

/// <summary>Clinic list filters; <c>from</c>/<c>to</c> are UTC instants, half-open [from, to) on <c>startsAt</c>.</summary>
public sealed record AppointmentListQuery(Guid? BranchId, AppointmentStatus? Status, DateTimeOffset? From, DateTimeOffset? To, Guid? DoctorId, int? Page, int? PageSize);

/// <summary>
/// <c>GET …/eligible-doctors</c> item: provides the service at the branch, active and free for the interval.
/// Additive: <c>withinWorkingHours</c>, <c>provisional</c> (holds the request now).
/// </summary>
public sealed record EligibleDoctorDto(Guid Id, string Name, bool WithinWorkingHours, bool Provisional);

public sealed record AvailabilitySlotDto(DateTimeOffset StartsAt, string LocalTime);

public sealed record AvailabilityDto(string Date, string TimeZone, int DurationMinutes, IReadOnlyList<AvailabilitySlotDto> Slots);

/// <summary>
/// Calendar block. Additive: <c>otherBranch</c> (the doctor is busy at another branch: no details) and
/// <c>blockId</c> (time off id for deletion).
/// </summary>
public sealed record CalendarBlockDto(
    TimeBlockKind Kind,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid? AppointmentId,
    AppointmentStatus? Status,
    string? ServiceName,
    string? ContactName,
    bool OtherBranch,
    Guid? BlockId);

public sealed record CalendarHoursDto(string Start, string End);

public sealed record CalendarDoctorDto(Guid Id, string Name, IReadOnlyList<CalendarHoursDto> WorkingHours, IReadOnlyList<CalendarBlockDto> Blocks);

public sealed record CalendarDto(string Date, string TimeZone, Guid BranchId, IReadOnlyList<CalendarDoctorDto> Doctors);

public sealed record TimeOffDto(Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);

public sealed record TimeOffCommand(DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Reason);
