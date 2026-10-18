using Microsoft.AspNetCore.Mvc;
using OqTish.Api.Common;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Reviews;
using OqTish.Application.Scheduling;
using OqTish.Domain.Booking;

namespace OqTish.Api.Endpoints;

/// <summary>
/// Booking endpoints (docs/API.md §3–§4, BE2): patient requests and own appointments, reviews and abuse
/// reports, and the clinic console's appointments, calendar and doctor time off. Handlers do every
/// authorization check.
/// </summary>
public static class BookingEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        MapPatient(app);
        MapClinic(app.MapGroup("/api/clinics/{clinicId:guid}").WithTags("Clinic console").RequireAuthorization());
        return app;
    }

    private static void MapPatient(IEndpointRouteBuilder app)
    {
        var patient = app.MapGroup("/api").WithTags("Patient").RequireAuthorization();

        patient.MapPost("/appointments", async (CreateAppointmentCommand body, [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                    CreateAppointmentHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(body, idempotencyKey, ct)).ToHttp(http, r => TypedResults.Created($"/api/me/appointments/{r.Id}", r)))
            .RequireRateLimiting(RateLimitPolicies.Booking)
            .WithName("CreateAppointment").WithSummary("Request an appointment (Pending, provisional doctor hold; optional Idempotency-Key)")
            .Produces<AppointmentDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422).ProducesProblem(429);

        patient.MapGet("/me/appointments", async (AppointmentStatus? status, int? page, int? pageSize, PatientAppointmentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(status, page, pageSize, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("MyAppointments").WithSummary("Own appointments, newest first")
            .Produces<Page<AppointmentDto>>().ProducesProblem(400).ProducesProblem(401);

        patient.MapGet("/me/appointments/{appointmentId:guid}", async (Guid appointmentId, PatientAppointmentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.GetAsync(appointmentId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("MyAppointment").WithSummary("One own appointment with status history (another patient's → 404)")
            .Produces<AppointmentDto>().ProducesProblem(401).ProducesProblem(404);

        patient.MapPost("/me/appointments/{appointmentId:guid}/cancel", async (Guid appointmentId, PatientAppointmentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CancelAsync(appointmentId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("CancelMyAppointment").WithSummary("Cancel: Pending any time, Confirmed until 2 h before start")
            .Produces<AppointmentDto>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        patient.MapPost("/me/appointments/{appointmentId:guid}/review", async (Guid appointmentId, ReviewCommand body, ReviewHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateAsync(appointmentId, body, ct)).ToHttp(http, r => TypedResults.Created($"/api/me/appointments/{appointmentId}", r)))
            .WithName("ReviewMyAppointment").WithSummary("Review an own Completed appointment (within 30 days, once; moderated)")
            .Produces<MyReviewDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        patient.MapPost("/reports", async (ReportCommand body, ReportHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateAsync(body, ct)).ToHttp(http, r => TypedResults.Created((string?)null, r)))
            .WithName("CreateReport").WithSummary("Report a clinic or a review to platform admins (one open report per target)")
            .Produces<ReportCreatedDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);
    }

    private static void MapClinic(RouteGroupBuilder clinic)
    {
        clinic.MapGet("/appointments", async (Guid clinicId, Guid? branchId, AppointmentStatus? status, DateTimeOffset? from, DateTimeOffset? to, Guid? doctorId, int? page, int? pageSize,
                    ClinicAppointmentQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.ListAsync(clinicId, new AppointmentListQuery(branchId, status, from, to, doctorId, page, pageSize), ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ClinicAppointments").WithSummary("Appointments in the caller's scope (appointment.view; Doctor: own); from/to = UTC [from, to) on startsAt")
            .Produces<Page<AppointmentDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPost("/appointments", async (Guid clinicId, WalkInCommand body, [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                    WalkInAppointmentHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(clinicId, body, idempotencyKey, ct)).ToHttp(http, r => TypedResults.Created($"/api/clinics/{clinicId}/appointments/{r.Id}", r)))
            .WithName("CreateWalkIn").WithSummary("Walk-in / phone booking, Confirmed directly (appointment.create_walkin; optional Idempotency-Key)")
            .Produces<AppointmentDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        clinic.MapGet("/appointments/{appointmentId:guid}", async (Guid clinicId, Guid appointmentId, ClinicAppointmentQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.GetAsync(clinicId, appointmentId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ClinicAppointment").WithSummary("Appointment detail with history (appointment.view; Doctor: own)")
            .Produces<AppointmentDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapGet("/appointments/{appointmentId:guid}/eligible-doctors", async (Guid clinicId, Guid appointmentId, ClinicAppointmentQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.EligibleDoctorsAsync(clinicId, appointmentId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("EligibleDoctors").WithSummary("Active doctors providing the service at the branch and free for the interval (appointment.triage)")
            .Produces<IReadOnlyList<EligibleDoctorDto>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPost("/appointments/{appointmentId:guid}/confirm", async (Guid clinicId, Guid appointmentId, ConfirmCommand body, StaffAppointmentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ConfirmAsync(clinicId, appointmentId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ConfirmAppointment").WithSummary("Pending → Confirmed with a doctor; deadline, eligibility and overlap re-checked atomically (appointment.triage)")
            .Produces<AppointmentDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        clinic.MapPost("/appointments/{appointmentId:guid}/reject", async (Guid clinicId, Guid appointmentId, ReasonCommand body, StaffAppointmentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.RejectAsync(clinicId, appointmentId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("RejectAppointment").WithSummary("Pending → Rejected with a reason code; releases the hold (appointment.triage)")
            .Produces<AppointmentDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        clinic.MapPost("/appointments/{appointmentId:guid}/cancel", async (Guid clinicId, Guid appointmentId, ReasonCommand body, StaffAppointmentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CancelAsync(clinicId, appointmentId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("CancelAppointmentByClinic").WithSummary("Confirmed → CancelledByClinic with a reason code; releases the time (appointment.cancel)")
            .Produces<AppointmentDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        clinic.MapPost("/appointments/{appointmentId:guid}/reschedule", async (Guid clinicId, Guid appointmentId, RescheduleCommand body, StaffAppointmentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.RescheduleAsync(clinicId, appointmentId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("RescheduleAppointment").WithSummary("Move a Confirmed appointment to a new time/doctor atomically (appointment.triage)")
            .Produces<AppointmentDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        clinic.MapPost("/appointments/{appointmentId:guid}/complete", async (Guid clinicId, Guid appointmentId, CompleteCommand body, StaffAppointmentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CompleteAsync(clinicId, appointmentId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("CompleteAppointment").WithSummary("Confirmed → Completed from the start time, with the charged amount (appointment.outcome; Doctor: own)")
            .Produces<AppointmentDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        clinic.MapPost("/appointments/{appointmentId:guid}/no-show", async (Guid clinicId, Guid appointmentId, StaffAppointmentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.NoShowAsync(clinicId, appointmentId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("NoShowAppointment").WithSummary("Confirmed → NoShow from start + 15 min (appointment.outcome; Doctor: own)")
            .Produces<AppointmentDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        clinic.MapGet("/calendar", async (Guid clinicId, Guid? branchId, string? date, ClinicAppointmentQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.CalendarAsync(clinicId, branchId, date, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ClinicCalendar").WithSummary("Day view of a branch: doctors × blocks in branch time (appointment.view)")
            .Produces<CalendarDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapGet("/doctors/{doctorId:guid}/time-off", async (Guid clinicId, Guid doctorId, DateTimeOffset? from, DateTimeOffset? to, TimeOffHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(clinicId, doctorId, from, to, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ListTimeOff").WithSummary("Doctor time off overlapping [from, to) (default: not yet ended)")
            .Produces<IReadOnlyList<TimeOffDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPost("/doctors/{doctorId:guid}/time-off", async (Guid clinicId, Guid doctorId, TimeOffCommand body, TimeOffHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateAsync(clinicId, doctorId, body, ct)).ToHttp(http, r => TypedResults.Created($"/api/clinics/{clinicId}/doctors/{doctorId}/time-off/{r.Id}", r)))
            .WithName("CreateTimeOff").WithSummary("Add time off (schedule.manage on the doctor's branches, or the doctor for own); overlap → 409")
            .Produces<TimeOffDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        clinic.MapDelete("/doctors/{doctorId:guid}/time-off/{timeOffId:guid}", async (Guid clinicId, Guid doctorId, Guid timeOffId, TimeOffHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.DeleteAsync(clinicId, doctorId, timeOffId, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("DeleteTimeOff").WithSummary("Remove time off (same rights as adding)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }
}
