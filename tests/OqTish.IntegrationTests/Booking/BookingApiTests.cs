using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Notifications;
using OqTish.Domain.Booking;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Booking;

/// <summary>
/// Booking over HTTP (real sign-in, real pipeline): patient flow, idempotency, and positive/negative
/// authorization for every booking endpoint — cross-clinic id substitution (404), out-of-branch (403),
/// patient A vs B (404), Doctor sees only own, FinanceManager read-only.
/// </summary>
public class BookingApiTests(PostgresFixture postgres)
{
    private static readonly string[] ScenarioDoctorsAtB1 = ["Dr. Bir", "Dr. Ikki"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> PostKeyAsync(HttpClient client, string url, object body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task Patient_books_replays_cancels_and_cannot_see_another_patients_appointment()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        var anon = s.Factory.CreateApiClient();
        var date = LocalFormats.Date(s.Date);

        var availability = await anon.GetOkAsync<AvailabilityDto>($"/api/public/branches/{s.B1}/availability?serviceId={s.Service}&date={date}");
        Assert.Equal(("Asia/Tashkent", 30), (availability.TimeZone, availability.DurationMinutes));
        Assert.Contains(availability.Slots, x => x.LocalTime == "10:00" && x.StartsAt == s.At(10));
        Assert.Equal("08:00", availability.Slots[0].LocalTime);
        Assert.Equal("19:30", availability.Slots[^1].LocalTime);
        await AssertProblemAsync(await anon.GetAsync($"/api/public/branches/{s.B1}/availability?serviceId={s.Service}&date=2020-01-01", Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await anon.GetAsync($"/api/public/branches/{s.B1}/availability?serviceId={Guid.NewGuid()}&date={date}", Ct), HttpStatusCode.NotFound, "common.not_found");

        var body = BookingScenario.Body(s.B1, s.Service, s.At(10));
        var created = await (await PostKeyAsync(s.P1.Client, "/api/appointments", body, "key-1")).ReadAsync<AppointmentDto>(HttpStatusCode.Created);
        Assert.Equal((AppointmentStatus.Pending, AppointmentSource.Patient, true, "+998901234567"), (created.Status, created.Source, created.CanCancel, created.ContactPhone));
        Assert.Null(created.Doctor); // the provisional doctor is never shown to the patient
        Assert.NotNull(created.ExpiresAt);
        using (var raw = JsonDocument.Parse(await (await s.P1.Client.GetAsync($"/api/me/appointments/{created.Id}", Ct)).Content.ReadAsStringAsync(Ct)))
        {
            Assert.Equal(JsonValueKind.Null, raw.RootElement.GetProperty("doctor").ValueKind);
            Assert.Equal("150000.00", raw.RootElement.GetProperty("quotedPrice").GetProperty("amount").GetString());
            Assert.Single(raw.RootElement.GetProperty("history").EnumerateArray());
        }

        var replay = await (await PostKeyAsync(s.P1.Client, "/api/appointments", body, "key-1")).ReadAsync<AppointmentDto>(HttpStatusCode.Created);
        Assert.Equal(created.Id, replay.Id);
        await AssertProblemAsync(await PostKeyAsync(s.P1.Client, "/api/appointments", BookingScenario.Body(s.B1, s.Service, s.At(11)), "key-1"), HttpStatusCode.UnprocessableEntity, "common.idempotency_mismatch");
        await AssertProblemAsync(await s.P1.Client.PostAsync("/api/appointments", new { branchId = s.B1, serviceId = s.Service, startsAt = s.At(12) }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await s.P1.Client.PostAsync("/api/appointments", BookingScenario.Body(Guid.NewGuid(), s.Service, s.At(12))), HttpStatusCode.NotFound, "common.not_found");

        var mine = await s.P1.Client.GetOkAsync<Page<AppointmentDto>>("/api/me/appointments");
        Assert.Equal([created.Id], mine.Items.Select(i => i.Id));
        Assert.Empty((await s.P2.Client.GetOkAsync<Page<AppointmentDto>>("/api/me/appointments")).Items);
        await AssertProblemAsync(await s.P2.Client.GetAsync($"/api/me/appointments/{created.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.P2.Client.PostAsync($"/api/me/appointments/{created.Id}/cancel", null, Ct), HttpStatusCode.NotFound, "common.not_found");

        var cancelled = await (await s.P1.Client.PostAsync($"/api/me/appointments/{created.Id}/cancel", null, Ct)).ReadAsync<AppointmentDto>();
        Assert.Equal((AppointmentStatus.CancelledByPatient, false), (cancelled.Status, cancelled.CanCancel));
        Assert.Equal([AppointmentStatus.Pending, AppointmentStatus.CancelledByPatient], cancelled.History!.Select(h => h.To));
        await AssertProblemAsync(await s.P1.Client.PostAsync($"/api/me/appointments/{created.Id}/cancel", null, Ct), HttpStatusCode.Conflict, "booking.invalid_transition");
        Assert.Contains((await anon.GetOkAsync<AvailabilityDto>($"/api/public/branches/{s.B1}/availability?serviceId={s.Service}&date={date}")).Slots, x => x.LocalTime == "10:00");

        await using var db = s.Factory.CreateDbContext();
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Type == BookingNotificationTypes.RequestedStaff && m.DedupKey.Contains(created.Id.ToString("N")), Ct));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Type == BookingNotificationTypes.CancelledByPatientStaff && m.DedupKey.Contains(created.Id.ToString("N")), Ct));
        Assert.DoesNotContain(await db.OutboxMessages.Select(m => m.Payload).ToListAsync(Ct), p => p.Contains("901234567", StringComparison.Ordinal) || p.Contains("Aziza", StringComparison.Ordinal));
        Assert.Equal(1, await db.AuditLog.CountAsync(a => a.Action == "booking.appointment.requested" && a.TargetId == created.Id.ToString(), Ct));
    }

    [Fact]
    public async Task Clinic_reads_and_triage_enforce_clinic_branch_doctor_and_finance_scope()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        var b1 = await s.BookAsync(s.P1, s.B1, s.At(10));
        var b2 = await s.BookAsync(s.P2, s.B2, s.At(10));

        // Lists (SQL-filtered): owner both, B1 receptionist only B1, finance read-only both, doctor none (nothing assigned).
        Assert.Equal(2, (await s.Owner.Client.GetOkAsync<Page<AppointmentDto>>($"{s.A}/appointments")).TotalCount);
        Assert.Equal([b1.Id], (await s.Reception.Client.GetOkAsync<Page<AppointmentDto>>($"{s.A}/appointments")).Items.Select(i => i.Id));
        await AssertProblemAsync(await s.Reception.Client.GetAsync($"{s.A}/appointments?branchId={s.B2}", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Reception.Client.GetAsync($"{s.A}/appointments?branchId={s.ClinicBBranch}", Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.Equal(2, (await s.Finance.Client.GetOkAsync<Page<AppointmentDto>>($"{s.A}/appointments")).TotalCount);
        Assert.Equal(0, (await s.Doctor.Client.GetOkAsync<Page<AppointmentDto>>($"{s.A}/appointments")).TotalCount);
        await AssertProblemAsync(await s.OwnerB.Client.GetAsync($"{s.A}/appointments", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.P1.Client.GetAsync($"{s.A}/appointments", Ct), HttpStatusCode.NotFound, "common.not_found");
        var range = await s.Owner.Client.GetOkAsync<Page<AppointmentDto>>($"{s.A}/appointments?from={Uri.EscapeDataString(s.At(10).ToString("O"))}&to={Uri.EscapeDataString(s.At(10, 30).ToString("O"))}&status=Pending");
        Assert.Equal(2, range.TotalCount);
        Assert.Equal(0, (await s.Owner.Client.GetOkAsync<Page<AppointmentDto>>($"{s.A}/appointments?from={Uri.EscapeDataString(s.At(10, 15).ToString("O"))}")).TotalCount);

        // Detail: cross-clinic id substitution 404, other branch 403, doctor not assigned 404, staff sees the phone.
        var detail = await s.Reception.Client.GetOkAsync<AppointmentDto>($"{s.A}/appointments/{b1.Id}");
        Assert.Equal(("+998901234567", false), (detail.ContactPhone, detail.CanCancel));
        await AssertProblemAsync(await s.OwnerB.Client.GetAsync($"/api/clinics/{s.ClinicB}/appointments/{b1.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.Owner.Client.GetAsync($"/api/clinics/{s.ClinicB}/appointments/{b1.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.Reception.Client.GetAsync($"{s.A}/appointments/{b2.Id}", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Doctor.Client.GetAsync($"{s.A}/appointments/{b1.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");

        // Eligible doctors: triage only.
        var eligible = await s.Reception.Client.GetOkAsync<List<EligibleDoctorDto>>($"{s.A}/appointments/{b1.Id}/eligible-doctors");
        Assert.Equal(new[] { s.D1, s.D2 }.Order(), eligible.Select(e => e.Id).Order());
        Assert.Single(eligible, e => e.Provisional);
        Assert.All(eligible, e => Assert.True(e.WithinWorkingHours));
        await AssertProblemAsync(await s.Finance.Client.GetAsync($"{s.A}/appointments/{b1.Id}/eligible-doctors", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Doctor.Client.GetAsync($"{s.A}/appointments/{b1.Id}/eligible-doctors", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.OwnerB.Client.GetAsync($"{s.A}/appointments/{b1.Id}/eligible-doctors", Ct), HttpStatusCode.NotFound, "common.not_found");

        // Confirm: negative cases leave the DB unchanged.
        var confirmUrl = $"{s.A}/appointments/{b1.Id}/confirm";
        await AssertProblemAsync(await s.Finance.Client.PostAsync(confirmUrl, new { doctorId = s.D1 }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Doctor.Client.PostAsync(confirmUrl, new { doctorId = s.D1 }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.OwnerB.Client.PostAsync($"/api/clinics/{s.ClinicB}/appointments/{b1.Id}/confirm", new { doctorId = s.D1 }), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{s.A}/appointments/{b2.Id}/confirm", new { doctorId = s.D3 }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Reception.Client.PostAsync(confirmUrl, new { doctorId = s.D3 }), HttpStatusCode.UnprocessableEntity, "booking.doctor_not_eligible");
        await using (var db = s.Factory.CreateDbContext())
        {
            var doctorB = await db.Doctors.Where(d => d.ClinicId == s.ClinicB).Select(d => d.Id).SingleAsync(Ct);
            await AssertProblemAsync(await s.Reception.Client.PostAsync(confirmUrl, new { doctorId = doctorB }), HttpStatusCode.UnprocessableEntity, "booking.doctor_not_eligible");
            await AssertProblemAsync(await s.Reception.Client.PostAsync(confirmUrl, new { }), HttpStatusCode.BadRequest, "common.validation");
            Assert.Equal(AppointmentStatus.Pending, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == b1.Id, Ct)).Status);
        }

        var confirmed = await (await s.Reception.Client.PostAsync(confirmUrl, new { doctorId = s.D1 })).ReadAsync<AppointmentDto>();
        Assert.Equal((AppointmentStatus.Confirmed, s.D1, true), (confirmed.Status, confirmed.Doctor!.Id, confirmed.CanCancel));
        await AssertProblemAsync(await s.Reception.Client.PostAsync(confirmUrl, new { doctorId = s.D2 }), HttpStatusCode.Conflict, "booking.not_pending");
        Assert.Equal("Dr. Bir", (await s.P1.Client.GetOkAsync<AppointmentDto>($"/api/me/appointments/{b1.Id}")).Doctor!.Name); // visible once confirmed
        Assert.Equal([b1.Id], (await s.Doctor.Client.GetOkAsync<Page<AppointmentDto>>($"{s.A}/appointments")).Items.Select(i => i.Id));
        Assert.Equal(b1.Id, (await s.Doctor.Client.GetOkAsync<AppointmentDto>($"{s.A}/appointments/{b1.Id}")).Id);

        // Reject: UI reason codes accepted; others 400; reject only Pending; clinic cancel only Confirmed.
        var rejectUrl = $"{s.A}/appointments/{b2.Id}/reject";
        await AssertProblemAsync(await s.Owner.Client.PostAsync(rejectUrl, new { reasonCode = "Bored" }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await s.Owner.Client.PostAsync($"{s.A}/appointments/{b2.Id}/cancel", new { reasonCode = "ClinicClosed" }), HttpStatusCode.Conflict, "booking.invalid_transition");
        await AssertProblemAsync(await s.Finance.Client.PostAsync(rejectUrl, new { reasonCode = "Other" }), HttpStatusCode.Forbidden, "common.forbidden");
        var rejected = await (await s.Owner.Client.PostAsync(rejectUrl, new { reasonCode = "PatientUnreachable" })).ReadAsync<AppointmentDto>();
        Assert.Equal((AppointmentStatus.Rejected, "PatientUnreachable"), (rejected.Status, rejected.History![^1].ReasonCode));
        await AssertProblemAsync(await s.Owner.Client.PostAsync($"{s.A}/appointments/{b1.Id}/reject", new { reasonCode = "Other" }), HttpStatusCode.Conflict, "booking.not_pending");

        await using var check = s.Factory.CreateDbContext();
        var keys = await check.OutboxMessages.Where(m => m.DedupKey.Contains(b1.Id.ToString("N"))).Select(m => new { m.Type, m.NotBefore }).ToListAsync(Ct);
        Assert.Contains(keys, k => k.Type == BookingNotificationTypes.ConfirmedPatient);
        Assert.Equal(2, keys.Count(k => k.Type == BookingNotificationTypes.ReminderPatient));
        Assert.Contains(keys, k => k.Type == BookingNotificationTypes.ReminderPatient && k.NotBefore == s.At(10).AddHours(-24));
        Assert.Equal(1, await check.OutboxMessages.CountAsync(m => m.Type == BookingNotificationTypes.RejectedPatient, Ct));
        Assert.Equal(1, await check.AuditLog.CountAsync(a => a.Action == "booking.appointment.confirmed", Ct));
    }

    [Fact]
    public async Task Reschedule_outcomes_walk_ins_and_clinic_cancel()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        var appt = await s.ConfirmedAsync(s.P1, s.B1, s.At(10), s.D1);

        // Reschedule (triage): new time + doctor atomically; old reminders cancelled, new ones enqueued.
        var url = $"{s.A}/appointments/{appt.Id}";
        await AssertProblemAsync(await s.Finance.Client.PostAsync($"{url}/reschedule", new { startsAt = s.At(11), doctorId = s.D2 }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{url}/reschedule", new { startsAt = s.At(11, 5), doctorId = s.D2 }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{url}/reschedule", new { startsAt = s.At(11), doctorId = s.D3 }), HttpStatusCode.UnprocessableEntity, "booking.doctor_not_eligible");
        var moved = await (await s.Reception.Client.PostAsync($"{url}/reschedule", new { startsAt = s.At(11), doctorId = s.D2 })).ReadAsync<AppointmentDto>();
        Assert.Equal((s.At(11), s.At(11, 30), s.D2, AppointmentStatus.Confirmed), (moved.StartsAt, moved.EndsAt, moved.Doctor!.Id, moved.Status));
        Assert.Equal("Rescheduled", moved.History![^1].ReasonCode);
        var other = await s.ConfirmedAsync(s.P2, s.B1, s.At(12), s.D1);
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{url}/reschedule", new { startsAt = s.At(12), doctorId = s.D1 }), HttpStatusCode.Conflict, "booking.doctor_busy");
        await using (var db = s.Factory.CreateDbContext())
        {
            var reminders = await db.OutboxMessages.Where(m => m.Type == BookingNotificationTypes.ReminderPatient && m.DedupKey.Contains(appt.Id.ToString("N")))
                .Select(m => new { m.Status, m.NotBefore }).ToListAsync(Ct);
            Assert.Equal(2, reminders.Count(r => r.Status == Domain.Notifications.OutboxStatus.Cancelled));
            Assert.Equal([s.At(11).AddHours(-24), s.At(11).AddHours(-2)], reminders.Where(r => r.Status == Domain.Notifications.OutboxStatus.Pending).Select(r => r.NotBefore).Order());
            Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Type == BookingNotificationTypes.RescheduledPatient, Ct));
        }

        // Complete: from start time, Doctor-own, finance cannot.
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{url}/complete", new { chargedAmount = new { amount = "150000.00", currency = "UZS" } }), HttpStatusCode.UnprocessableEntity, "booking.too_early_for_outcome");
        await BookingCalls.ShiftAsync(s.Factory.ConnectionString, appt.Id, s.At(11, days: -5));
        await AssertProblemAsync(await s.Doctor.Client.PostAsync($"{url}/complete", new { chargedAmount = new { amount = "1.00", currency = "UZS" } }), HttpStatusCode.NotFound, "common.not_found"); // assigned to D2 now
        await AssertProblemAsync(await s.Finance.Client.PostAsync($"{url}/complete", new { chargedAmount = new { amount = "1.00", currency = "UZS" } }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{url}/complete", new { chargedAmount = new { amount = "1.005", currency = "UZS" } }), HttpStatusCode.BadRequest, "finance.amount_invalid");
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{url}/complete", new { }), HttpStatusCode.BadRequest, "common.validation");
        var done = await (await s.Reception.Client.PostAsync($"{url}/complete", new { chargedAmount = new { amount = "175000.00", currency = "UZS" } })).ReadAsync<AppointmentDto>();
        Assert.Equal((AppointmentStatus.Completed, "175000.00"), (done.Status, done.ChargedAmount!.Value.ToAmountString()));
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{url}/no-show", null, Ct), HttpStatusCode.Conflict, "booking.invalid_transition");

        // No-show: from start + 15 min; the Doctor role may record it for its own appointment.
        var otherUrl = $"{s.A}/appointments/{other.Id}";
        await BookingCalls.ShiftAsync(s.Factory.ConnectionString, other.Id, DateTimeOffset.UtcNow.AddMinutes(-10));
        await AssertProblemAsync(await s.Doctor.Client.PostAsync($"{otherUrl}/no-show", null, Ct), HttpStatusCode.UnprocessableEntity, "booking.too_early_for_outcome");
        await BookingCalls.ShiftAsync(s.Factory.ConnectionString, other.Id, DateTimeOffset.UtcNow.AddMinutes(-20));
        Assert.Equal(AppointmentStatus.NoShow, (await (await s.Doctor.Client.PostAsync($"{otherUrl}/no-show", null, Ct)).ReadAsync<AppointmentDto>()).Status);

        // Walk-in (create_walkin): Confirmed directly, no account, idempotent; doctor busy → 409.
        var walkIn = new { branchId = s.B1, serviceId = s.Service, startsAt = s.At(15), doctorId = s.D2, contactName = "Telefon mijoz", contactPhone = "901112233" };
        var w1 = await (await PostKeyAsync(s.Reception.Client, $"{s.A}/appointments", walkIn, "walk-1")).ReadAsync<AppointmentDto>(HttpStatusCode.Created);
        Assert.Equal((AppointmentStatus.Confirmed, AppointmentSource.WalkIn, s.D2, "+998901112233"), (w1.Status, w1.Source, w1.Doctor!.Id, w1.ContactPhone));
        Assert.Equal(w1.Id, (await (await PostKeyAsync(s.Reception.Client, $"{s.A}/appointments", walkIn, "walk-1")).ReadAsync<AppointmentDto>(HttpStatusCode.Created)).Id);
        await AssertProblemAsync(await PostKeyAsync(s.Owner.Client, $"{s.A}/appointments", walkIn, "walk-2"), HttpStatusCode.Conflict, "booking.doctor_busy");
        await AssertProblemAsync(await s.Finance.Client.PostAsync($"{s.A}/appointments", walkIn), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{s.A}/appointments", walkIn), HttpStatusCode.Conflict, "booking.doctor_busy");
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{s.A}/appointments", new { branchId = s.B2, serviceId = s.Service, startsAt = s.At(15), doctorId = s.D3, contactName = "X", contactPhone = "901112233" }),
            HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Owner.Client.PostAsync($"{s.A}/appointments", new { branchId = s.ClinicBBranch, serviceId = s.Service, startsAt = s.At(15), doctorId = s.D2, contactName = "X", contactPhone = "901112233" }),
            HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.Owner.Client.PostAsync($"{s.A}/appointments", new { branchId = s.B1, serviceId = s.Service, startsAt = s.At(10, days: -6), doctorId = s.D2, contactName = "X", contactPhone = "901112233" }),
            HttpStatusCode.UnprocessableEntity, "booking.lead_time");

        // Clinic cancel (appointment.cancel) of a Confirmed appointment releases the time.
        await AssertProblemAsync(await s.Finance.Client.PostAsync($"{s.A}/appointments/{w1.Id}/cancel", new { reasonCode = "ClinicClosed" }), HttpStatusCode.Forbidden, "common.forbidden");
        var cancelled = await (await s.Reception.Client.PostAsync($"{s.A}/appointments/{w1.Id}/cancel", new { reasonCode = "ClinicClosed" })).ReadAsync<AppointmentDto>();
        Assert.Equal(AppointmentStatus.CancelledByClinic, cancelled.Status);
        Assert.Equal(HttpStatusCode.Created, (await PostKeyAsync(s.Reception.Client, $"{s.A}/appointments", walkIn, "walk-3")).StatusCode);
        await BookingInvariants.AssertAsync(s.Factory.ConnectionString);
    }

    [Fact]
    public async Task Calendar_and_time_off_follow_branch_and_doctor_own_rules()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        var appt = await s.ConfirmedAsync(s.P1, s.B1, s.At(10), s.D1);
        var pending = await s.BookAsync(s.P2, s.B1, s.At(13));
        var date = LocalFormats.Date(s.Date);

        var calendar = await s.Owner.Client.GetOkAsync<CalendarDto>($"{s.A}/calendar?branchId={s.B1}&date={date}");
        Assert.Equal(ScenarioDoctorsAtB1, calendar.Doctors.Select(d => d.Name));
        Assert.Contains(calendar.Doctors[0].Blocks, b => b.AppointmentId == appt.Id && b.Kind == Domain.Scheduling.TimeBlockKind.Appointment && b.ContactName == "Aziza Karimova");
        Assert.Contains(calendar.Doctors.SelectMany(d => d.Blocks), b => b.AppointmentId == pending.Id && b.Kind == Domain.Scheduling.TimeBlockKind.Hold);
        Assert.Equal([new CalendarHoursDto("08:00", "20:00")], calendar.Doctors[0].WorkingHours);
        await AssertProblemAsync(await s.Reception.Client.GetAsync($"{s.A}/calendar?branchId={s.B2}&date={date}", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.OwnerB.Client.GetAsync($"{s.A}/calendar?branchId={s.B1}&date={date}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.Owner.Client.GetAsync($"{s.A}/calendar?branchId={s.B1}&date=18.10.2026", Ct), HttpStatusCode.BadRequest, "common.validation");
        var own = await s.Doctor.Client.GetOkAsync<CalendarDto>($"{s.A}/calendar?branchId={s.B1}&date={date}");
        Assert.Equal([s.D1], own.Doctors.Select(d => d.Id));

        // Time off: schedule.manage on the doctor's branches, or the doctor for its own record.
        var to = (Guid doctor) => $"{s.A}/doctors/{doctor}/time-off";
        await AssertProblemAsync(await s.Reception.Client.PostAsync(to(s.D1), new { startsAt = s.At(9, 30), endsAt = s.At(10, 15) }), HttpStatusCode.Conflict, "schedule.timeoff_conflict");
        var off = await (await s.Reception.Client.PostAsync(to(s.D1), new { startsAt = s.At(14), endsAt = s.At(16), reason = "Konferensiya" })).ReadAsync<TimeOffDto>(HttpStatusCode.Created);
        await AssertProblemAsync(await s.Reception.Client.PostAsync(to(s.D1), new { startsAt = s.At(15), endsAt = s.At(17) }), HttpStatusCode.Conflict, "schedule.timeoff_conflict");
        await AssertProblemAsync(await s.Reception.Client.PostAsync(to(s.D3), new { startsAt = s.At(14), endsAt = s.At(16) }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Finance.Client.PostAsync(to(s.D1), new { startsAt = s.At(17), endsAt = s.At(18) }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Doctor.Client.PostAsync(to(s.D2), new { startsAt = s.At(17), endsAt = s.At(18) }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.OwnerB.Client.PostAsync(to(s.D1), new { startsAt = s.At(17), endsAt = s.At(18) }), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.Reception.Client.PostAsync(to(s.D1), new { startsAt = s.At(18), endsAt = s.At(17) }), HttpStatusCode.BadRequest, "common.validation");
        var mine = await (await s.Doctor.Client.PostAsync(to(s.D1), new { startsAt = s.At(17), endsAt = s.At(18) })).ReadAsync<TimeOffDto>(HttpStatusCode.Created);
        Assert.Equal(2, (await s.Doctor.Client.GetOkAsync<List<TimeOffDto>>(to(s.D1))).Count);
        Assert.Single(await s.Owner.Client.GetOkAsync<List<TimeOffDto>>($"{to(s.D1)}?from={Uri.EscapeDataString(s.At(13).ToString("O"))}&to={Uri.EscapeDataString(s.At(15).ToString("O"))}"));
        await AssertProblemAsync(await s.Reception.Client.GetAsync(to(s.D3), Ct), HttpStatusCode.Forbidden, "common.forbidden");
        Assert.Contains((await s.Owner.Client.GetOkAsync<CalendarDto>($"{s.A}/calendar?branchId={s.B1}&date={date}")).Doctors[0].Blocks,
            b => b.Kind == Domain.Scheduling.TimeBlockKind.TimeOff && b.BlockId == off.Id);
        await AssertProblemAsync(await s.Finance.Client.DeleteAsync($"{to(s.D1)}/{mine.Id}", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        Assert.Equal(HttpStatusCode.NoContent, (await s.Doctor.Client.DeleteAsync($"{to(s.D1)}/{mine.Id}", Ct)).StatusCode);
        await AssertProblemAsync(await s.Doctor.Client.DeleteAsync($"{to(s.D1)}/{mine.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.Client.DeleteAsync($"{to(s.D1)}/{off.Id}", Ct)).StatusCode);
        Assert.Equal(2, await ConsoleKit.AuditCountAsync(s.Factory, "schedule.timeoff.deleted", s.ClinicA));
        await BookingInvariants.AssertAsync(s.Factory.ConnectionString);
    }

    [Fact]
    public async Task Dashboards_doctor_link_guard_and_account_deletion()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        var pending = await s.BookAsync(s.P1, s.B1, s.At(10));
        var now = DateTimeOffset.UtcNow;
        var walkInStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute / 15 * 15, 0, TimeSpan.Zero); // today, on the grid
        var walk = await (await s.Owner.Client.PostAsync($"{s.A}/appointments",
            new { branchId = s.B1, serviceId = s.Service, startsAt = walkInStart, doctorId = s.D2, contactName = "Bugun", contactPhone = "901112233" })).ReadAsync<AppointmentDto>(HttpStatusCode.Created);

        var dashboard = await s.Owner.Client.GetOkAsync<OqTish.Application.Tenancy.DashboardDto>($"{s.A}/dashboard");
        Assert.Equal((1, 1), (dashboard.Pending, dashboard.Confirmed));
        var branchDashboard = await s.Reception.Client.GetOkAsync<OqTish.Application.Tenancy.DashboardDto>($"{s.A}/dashboard?branchId={s.B1}");
        Assert.Equal((1, 1), (branchDashboard.Pending, branchDashboard.Confirmed));
        var doctorDashboard = await s.Doctor.Client.GetOkAsync<OqTish.Application.Tenancy.DashboardDto>($"{s.A}/dashboard");
        Assert.Equal((0, 0), (doctorDashboard.Pending, doctorDashboard.Confirmed)); // Doctor: own appointments only
        var admin = await ConsoleKit.SignInAsPlatformAdminAsync(s.Factory, OqTish.Application.Staff.PlatformPermissions.AuditView);
        Assert.Equal(1, (await admin.Client.GetOkAsync<OqTish.Application.Admin.AdminDashboardDto>("/api/admin/dashboard")).AppointmentsToday);

        // A branch link referenced by appointments cannot be removed (RESTRICT FK) → 409.
        await AssertProblemAsync(await s.Owner.Client.PutAsync($"{s.A}/doctors/{s.D2}", new { displayName = "Dr. Ikki", isActive = true, branchIds = new[] { s.B2 }, serviceIds = new[] { s.Service } }),
            HttpStatusCode.Conflict, "schedule.link_in_use");
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.Client.PutAsync($"{s.A}/doctors/{s.D2}", new { displayName = "Dr. Ikki", isActive = false, branchIds = new[] { s.B1 }, serviceIds = new[] { s.Service } })).StatusCode);

        // Self-deletion cancels the patient's active requests, releases the hold and de-identifies contacts.
        Assert.Equal(HttpStatusCode.NoContent, (await s.P1.Client.PostAsync("/api/me/delete", null, Ct)).StatusCode);
        await using var db = s.Factory.CreateDbContext();
        var after = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == pending.Id, Ct);
        Assert.Equal((AppointmentStatus.CancelledByPatient, "AccountDeleted", Domain.Booking.Appointment.AnonymisedName, (string?)null), (after.Status, after.ReasonCode, after.ContactName, after.ContactPhone));
        Assert.Equal(0, await db.DoctorTimeBlocks.CountAsync(b => b.AppointmentId == pending.Id && b.IsActive, Ct));
        Assert.NotEqual(Guid.Empty, walk.Id);
        await BookingInvariants.AssertAsync(s.Factory.ConnectionString);
    }

    [Fact]
    public async Task Patient_booking_requests_are_rate_limited_per_session()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["RateLimiting:BookingPermitPerHour"] = "2" });
        var patient = await SignInAsPatientAsync(factory);
        var body = BookingScenario.Body(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(2));
        Assert.Equal(HttpStatusCode.NotFound, (await patient.Client.PostAsync("/api/appointments", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await patient.Client.PostAsync("/api/appointments", body)).StatusCode);
        var limited = await patient.Client.PostAsync("/api/appointments", body);
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "auth.rate_limited");
        Assert.True(limited.Headers.Contains("Retry-After"));
        var other = await SignInAsPatientAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync("/api/appointments", body)).StatusCode); // separate bucket
    }
}
