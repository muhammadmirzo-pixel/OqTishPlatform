using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Notifications;
using OqTish.Application.Scheduling;
using OqTish.Domain.Booking;
using OqTish.Domain.Tenancy;
using OqTish.IntegrationTests.Infrastructure;

namespace OqTish.IntegrationTests.Booking;

/// <summary>TESTING.md §3: B3, B5, B8, B11, B12 (sequential/mismatch), B13–B17, with B18 invariants after each test.</summary>
public class BookingRulesTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task ExecAsync(BookingHost host, string sql)
    {
        await using var c = new NpgsqlConnection(host.ConnectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<long> ScalarAsync(BookingHost host, string sql)
    {
        await using var c = new NpgsqlConnection(host.ConnectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task B3_adjacent_intervals_on_one_doctor_both_succeed_concurrently()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 2);
        var results = await Race.RunAsync<Result<AppointmentDto>>(host,
        [
            (w.Patients[0], sp => sp.GetRequiredService<CreateAppointmentHandler>().HandleAsync(w.Request(w.At(10)), null, Ct)),
            (w.Patients[1], sp => sp.GetRequiredService<CreateAppointmentHandler>().HandleAsync(w.Request(w.At(10, 30)), null, Ct)),
        ]);
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Code));
        Assert.Equal(2, await db.DoctorTimeBlocks.CountAsync(b => b.DoctorId == w.Doctors[0] && b.IsActive, Ct));
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B5_least_flexible_doctor_is_held_first_and_the_heuristic_limit_is_documented()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();

        // A provides {X, Y}, B provides {X}: X goes to B, so Y can still be served by A.
        var w = await BookingWorld.CreateAsync(db, doctors: 2, patients: 2);
        var (a, b) = (w.Doctors[0], w.Doctors[1]);
        await db.DoctorServices.Where(x => x.DoctorId == b && x.ServiceId == w.Service60).ExecuteDeleteAsync(Ct);
        var x = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
        var y = await host.BookAsync(w, w.Patients[1], w.At(10), w.Service60);
        Assert.True(y.IsSuccess, y.Error?.Code);
        Assert.Equal(b, await host.HeldDoctorAsync(x.Id));
        Assert.Equal(a, await host.HeldDoctorAsync(y.Value.Id));

        // Heuristic limit: A provides {X, Y}, B provides {X, Z} — equally flexible, so X goes to A (lower id). A later Y
        // request at the same time then finds no doctor although moving X to B would have made room (409). Staff can
        // still reassign at confirm time; an optimal assignment is out of scope (DECISIONS.md OD-01, BE2).
        var w2 = await BookingWorld.CreateAsync(db, doctors: 2, patients: 2);
        var (a2, b2) = (w2.Doctors.Min(), w2.Doctors.Max());
        var z = Domain.Catalog.Service.Create(w2.ClinicId, "Z", "other", true, DateTimeOffset.UtcNow);
        db.Services.Add(z);
        await db.SaveChangesAsync(Ct);
        db.DoctorServices.Add(Domain.Scheduling.DoctorService.Create(w2.ClinicId, b2, z.Id));
        await db.SaveChangesAsync(Ct);
        await db.DoctorServices.Where(s => s.DoctorId == b2 && s.ServiceId == w2.Service60).ExecuteDeleteAsync(Ct);
        var x2 = (await host.BookAsync(w2, w2.Patients[0], w2.At(10))).Value;
        Assert.Equal(a2, await host.HeldDoctorAsync(x2.Id));
        Assert.Equal("booking.slot_unavailable", (await host.BookAsync(w2, w2.Patients[1], w2.At(10), w2.Service60)).Error!.Code);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B8_confirm_after_the_deadline_with_the_sweeper_stopped_is_rejected_and_reads_show_expired()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
        Assert.Equal(AppointmentStatus.Pending, booked.Status);
        Assert.InRange(booked.ExpiresAt!.Value - booked.CreatedAt, TimeSpan.FromMinutes(29.9), TimeSpan.FromMinutes(30.1));
        await host.SetDeadlineAsync(booked.Id, TimeSpan.FromSeconds(-1));

        var confirm = await host.ConfirmAsync(w, booked.Id, w.Doctors[0]);
        Assert.Equal("booking.expired", confirm.Error!.Code);
        Assert.Equal(AppointmentStatus.Pending, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id, Ct)).Status); // no sweeper ran

        var patientView = await host.RunAsync(w.Patients[0], sp => sp.GetRequiredService<PatientAppointmentHandlers>().GetAsync(booked.Id, Ct));
        Assert.Equal((AppointmentStatus.Expired, false), (patientView.Value.Status, patientView.Value.CanCancel));
        var expiredList = await host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<ClinicAppointmentQueries>().ListAsync(w.ClinicId,
            new AppointmentListQuery(null, AppointmentStatus.Expired, null, null, null, null, null), Ct));
        Assert.Equal([booked.Id], expiredList.Value.Items.Select(i => i.Id));
        var pendingList = await host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<ClinicAppointmentQueries>().ListAsync(w.ClinicId,
            new AppointmentListQuery(null, AppointmentStatus.Pending, null, null, null, null, null), Ct));
        Assert.Empty(pendingList.Value.Items);
        Assert.Equal("booking.expired", (await host.RunAsync(w.Patients[0], sp => sp.GetRequiredService<PatientAppointmentHandlers>().CancelAsync(booked.Id, Ct))).Error!.Code);

        Assert.Equal(1, await host.SweepAsync());
        Assert.Equal(AppointmentStatus.Expired, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id, Ct)).Status);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Type == BookingNotificationTypes.ExpiredPatient && m.DedupKey.Contains(booked.Id.ToString("N")), Ct));
        Assert.Equal(0, await host.SweepAsync());
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B11_failure_injected_after_the_block_insert_rolls_everything_back()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        await ExecAsync(host, $"""
            CREATE FUNCTION public.test_fail_after_block() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected failure after block insert'; END $$;
            CREATE TRIGGER test_fail_after_block AFTER INSERT ON scheduling.doctor_time_blocks
              FOR EACH ROW WHEN (NEW.doctor_id = '{w.Doctors[0]}') EXECUTE FUNCTION public.test_fail_after_block();
            """);

        await Assert.ThrowsAnyAsync<Exception>(() => host.BookAsync(w, w.Patients[0], w.At(10), key: "b11-key"));

        Assert.Equal(0, await db.Appointments.CountAsync(a => a.ClinicId == w.ClinicId, Ct));
        Assert.Equal(0, await db.DoctorTimeBlocks.CountAsync(b => b.ClinicId == w.ClinicId, Ct));
        Assert.Equal(0, await db.AppointmentStatusHistory.CountAsync(h => h.ClinicId == w.ClinicId, Ct));
        Assert.Equal(0, await db.OutboxMessages.CountAsync(Ct));
        Assert.Equal(0, await db.IdempotencyKeys.CountAsync(k => k.Key == "b11-key", Ct));
        Assert.Equal(0, await db.AuditLog.CountAsync(a => a.ClinicId == w.ClinicId, Ct));

        await ExecAsync(host, "DROP TRIGGER test_fail_after_block ON scheduling.doctor_time_blocks");
        Assert.True((await host.BookAsync(w, w.Patients[0], w.At(10), key: "b11-key")).IsSuccess); // the key was not consumed
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B12_same_key_sequential_replays_and_a_different_body_is_a_mismatch()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 2, patients: 1);
        var first = await host.BookAsync(w, w.Patients[0], w.At(10), key: "same");
        var replay = await host.BookAsync(w, w.Patients[0], w.At(10), key: "same");
        Assert.Equal(first.Value.Id, replay.Value.Id);
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.ClinicId == w.ClinicId, Ct));
        Assert.Equal("common.idempotency_mismatch", (await host.BookAsync(w, w.Patients[0], w.At(11), key: "same")).Error!.Code);
        Assert.True((await host.BookAsync(w, w.Patients[0], w.At(11), key: "other")).IsSuccess);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B13_reject_expire_and_cancel_release_the_block_and_the_slot_is_rebookable()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var actions = new Dictionary<string, Func<BookingWorld, Guid, Task>>
        {
            ["reject"] = async (w, id) => Assert.True((await host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>()
                .RejectAsync(w.ClinicId, id, new ReasonCommand("NoDoctorAvailable"), Ct))).IsSuccess),
            ["expire"] = async (_, id) =>
            {
                await host.SetDeadlineAsync(id, TimeSpan.FromSeconds(-1));
                Assert.Equal(1, await host.SweepAsync());
            },
            ["patient-cancel"] = async (w, id) => Assert.True((await host.RunAsync(w.Patients[0], sp => sp.GetRequiredService<PatientAppointmentHandlers>().CancelAsync(id, Ct))).IsSuccess),
            ["clinic-cancel"] = async (w, id) =>
            {
                Assert.True((await host.ConfirmAsync(w, id, w.Doctors[0])).IsSuccess);
                Assert.True((await host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>()
                    .CancelAsync(w.ClinicId, id, new ReasonCommand("ClinicEmergency"), Ct))).IsSuccess);
            },
        };
        foreach (var (name, act) in actions)
        {
            var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 2);
            var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
            Assert.Equal("booking.slot_unavailable", (await host.BookAsync(w, w.Patients[1], w.At(10))).Error!.Code);
            await act(w, booked.Id);
            Assert.Equal(0, await db.DoctorTimeBlocks.CountAsync(b => b.AppointmentId == booked.Id && b.IsActive, Ct));
            var again = await host.BookAsync(w, w.Patients[1], w.At(10));
            Assert.True(again.IsSuccess, $"{name}: {again.Error?.Code}");
        }

        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B14_an_overdue_pending_request_does_not_block_a_new_booking_without_the_sweeper()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 2);
        var old = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
        await host.SetDeadlineAsync(old.Id, TimeSpan.FromSeconds(-1));

        var fresh = await host.BookAsync(w, w.Patients[1], w.At(10));
        Assert.True(fresh.IsSuccess, fresh.Error?.Code);
        Assert.Equal(AppointmentStatus.Expired, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == old.Id, Ct)).Status);
        var history = await db.AppointmentStatusHistory.SingleAsync(h => h.AppointmentId == old.Id && h.To == AppointmentStatus.Expired, Ct);
        Assert.Equal(HistoryActorKind.System, history.ActorKind);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Type == BookingNotificationTypes.ExpiredPatient && m.DedupKey.Contains(old.Id.ToString("N")), Ct));

        // Also for a doctor-scoped write: time off over an overdue hold of the doctor succeeds.
        var w2 = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        var hold = (await host.BookAsync(w2, w2.Patients[0], w2.At(10))).Value;
        await host.SetDeadlineAsync(hold.Id, TimeSpan.FromSeconds(-1));
        var timeOff = await host.RunAsync(w2.StaffUserId, sp => sp.GetRequiredService<TimeOffHandlers>().CreateAsync(w2.ClinicId, w2.Doctors[0], new TimeOffCommand(w2.At(9), w2.At(11), null), Ct));
        Assert.True(timeOff.IsSuccess, timeOff.Error?.Code);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B15_outside_hours_shift_end_time_off_inactive_offers_and_unapproved_clinics_are_rejected()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 4);
        async Task<string?> Code(BookingWorld world, DateTimeOffset start, Guid? service = null, int patient = 0) =>
            (await host.BookAsync(world, world.Patients[patient], start, service)) is { IsSuccess: false } r ? r.Error!.Code : null;

        Assert.Equal("booking.outside_hours", await Code(w, w.At(21)));
        Assert.Equal("booking.outside_hours", await Code(w, w.At(19, 45))); // 19:45–20:15 crosses the shift end
        Assert.Equal("booking.outside_hours", await Code(w, w.At(7, 45)));
        Assert.Equal("common.validation", await Code(w, w.At(10, 7)));    // off the 15-minute grid
        Assert.Equal("booking.lead_time", await Code(w, RoundUp(DateTimeOffset.UtcNow.AddMinutes(20)))); // < 60 min ahead
        Assert.Equal("booking.lead_time", await Code(w, w.At(10, days: 28))); // today + 31

        Assert.True((await host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<TimeOffHandlers>()
            .CreateAsync(w.ClinicId, w.Doctors[0], new TimeOffCommand(w.At(9), w.At(12), "ta'til"), Ct))).IsSuccess);
        Assert.Equal("booking.outside_hours", await Code(w, w.At(10)));    // during time off
        Assert.Null(await Code(w, w.At(12)));                              // right after it

        db.BranchOpeningHours.Add(BranchOpeningHours.Create(w.ClinicId, w.BranchId, BookingTime.IsoWeekday(w.Date), new TimeOnly(9, 0), new TimeOnly(18, 0)));
        await db.SaveChangesAsync(Ct);
        Assert.Equal("booking.outside_hours", await Code(w, w.At(18, 30), patient: 1)); // doctor works, branch closed
        Assert.Null(await Code(w, w.At(17, 0), patient: 1));

        Assert.Null(await Code(w, w.At(13), patient: 2));
        Assert.Null(await Code(w, w.At(14), patient: 2));
        Assert.Null(await Code(w, w.At(15), patient: 2));
        Assert.Equal("booking.too_many_pending", await Code(w, w.At(16), patient: 2));

        await db.BranchServices.Where(o => o.BranchId == w.BranchId && o.ServiceId == w.Service60).ExecuteUpdateAsync(s => s.SetProperty(o => o.IsActive, false), Ct);
        Assert.Equal("booking.clinic_unavailable", await Code(w, w.At(15), w.Service60, patient: 3));
        await db.Services.Where(s => s.Id == w.Service30).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), Ct);
        Assert.Equal("booking.clinic_unavailable", await Code(w, w.At(15), patient: 3));
        await db.Services.Where(s => s.Id == w.Service30).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true), Ct);
        await db.Clinics.Where(c => c.Id == w.ClinicId).ExecuteUpdateAsync(s => s.SetProperty(c => c.VerificationStatus, ClinicVerificationStatus.Pending), Ct);
        Assert.Equal("booking.clinic_unavailable", await Code(w, w.At(15), patient: 3));
        await db.Clinics.Where(c => c.Id == w.ClinicId).ExecuteUpdateAsync(s => s.SetProperty(c => c.VerificationStatus, ClinicVerificationStatus.Suspended), Ct);
        Assert.Equal("booking.clinic_unavailable", await Code(w, w.At(15), patient: 3));
        await db.Clinics.Where(c => c.Id == w.ClinicId).ExecuteUpdateAsync(s => s.SetProperty(c => c.VerificationStatus, ClinicVerificationStatus.Approved), Ct);
        await db.Branches.Where(b => b.Id == w.BranchId).ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false), Ct);
        Assert.Equal("booking.clinic_unavailable", await Code(w, w.At(15), patient: 3));
        Assert.Equal("common.not_found", (await host.RunAsync(w.Patients[3], sp => sp.GetRequiredService<CreateAppointmentHandler>()
            .HandleAsync(w.Request(w.At(15)) with { BranchId = Guid.NewGuid() }, null, Ct))).Error!.Code);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    private static DateTimeOffset RoundUp(DateTimeOffset t)
    {
        var minutes = (int)Math.Ceiling((t.Minute + t.Second / 60.0 + 0.001) / 15.0) * 15;
        return new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, TimeSpan.Zero).AddMinutes(minutes);
    }

    [Fact]
    public async Task B16_branch_local_time_near_utc_midnight_maps_to_the_right_date_and_is_stored_utc()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1, hoursStart: new TimeOnly(0, 0), hoursEnd: new TimeOnly(6, 0));
        var start = w.At(0, 15); // 00:15 in Tashkent (UTC+5) = 19:15 UTC of the previous day
        Assert.Equal((w.Date.AddDays(-1), 19, 15), (DateOnly.FromDateTime(start.UtcDateTime), start.UtcDateTime.Hour, start.UtcDateTime.Minute));

        var day = await host.RunAsync<Result<AvailabilityDto>>(null, sp => sp.GetRequiredService<AvailabilityQuery>()
            .HandleAsync(w.BranchId, w.Service30, LocalFormats.Date(w.Date), Ct));
        var slot = day.Value.Slots.Single(s => s.LocalTime == "00:15");
        Assert.Equal(start, slot.StartsAt);
        Assert.Equal("00:00", day.Value.Slots[0].LocalTime);
        Assert.Equal("05:30", day.Value.Slots[^1].LocalTime);
        var previous = await host.RunAsync<Result<AvailabilityDto>>(null, sp => sp.GetRequiredService<AvailabilityQuery>()
            .HandleAsync(w.BranchId, w.Service30, LocalFormats.Date(w.Date.AddDays(-1)), Ct));
        Assert.DoesNotContain(previous.Value.Slots, s => s.StartsAt == start);

        var booked = (await host.BookAsync(w, w.Patients[0], start)).Value;
        Assert.Equal(TimeSpan.Zero, booked.StartsAt.Offset);
        var stored = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id, Ct);
        Assert.Equal(start.UtcDateTime, stored.StartsAt.UtcDateTime);
        var calendar = await host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<ClinicAppointmentQueries>().CalendarAsync(w.ClinicId, w.BranchId, LocalFormats.Date(w.Date), Ct));
        Assert.Contains(calendar.Value.Doctors.Single().Blocks, b => b.AppointmentId == booked.Id && b.Kind == Domain.Scheduling.TimeBlockKind.Hold);
        var calendarBefore = await host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<ClinicAppointmentQueries>().CalendarAsync(w.ClinicId, w.BranchId, LocalFormats.Date(w.Date.AddDays(-1)), Ct));
        Assert.DoesNotContain(calendarBefore.Value.Doctors.Single().Blocks, b => b.AppointmentId == booked.Id);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B17_forced_deadlock_is_retried_at_most_three_times_then_409()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        await ExecAsync(host, """
            CREATE SEQUENCE public.test_b17_attempts;
            CREATE TABLE public.test_b17_config (failures int NOT NULL);
            INSERT INTO public.test_b17_config VALUES (1000);
            CREATE FUNCTION public.test_b17() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.contact_name = 'force-40P01' AND nextval('public.test_b17_attempts') <= (SELECT failures FROM public.test_b17_config) THEN
                RAISE EXCEPTION 'forced deadlock' USING ERRCODE = '40P01';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER test_b17 BEFORE INSERT ON booking.appointments FOR EACH ROW EXECUTE FUNCTION public.test_b17();
            """);
        var command = w.Request(w.At(10)) with { ContactName = "force-40P01" };

        var failed = await host.RunAsync(w.Patients[0], sp => sp.GetRequiredService<CreateAppointmentHandler>().HandleAsync(command, null, Ct));
        Assert.Equal((ErrorKind.Conflict, "common.concurrency"), (failed.Error!.Kind, failed.Error.Code));
        Assert.Equal(3, await ScalarAsync(host, "SELECT last_value FROM public.test_b17_attempts"));
        Assert.Equal(0, await db.Appointments.CountAsync(a => a.ClinicId == w.ClinicId, Ct));
        Assert.Equal(0, await db.DoctorTimeBlocks.CountAsync(b => b.ClinicId == w.ClinicId, Ct));

        // One forced deadlock, then success on the retry.
        await ExecAsync(host, "UPDATE public.test_b17_config SET failures = 1; SELECT setval('public.test_b17_attempts', 1, false);");
        var retried = await host.RunAsync(w.Patients[0], sp => sp.GetRequiredService<CreateAppointmentHandler>().HandleAsync(command, null, Ct));
        Assert.True(retried.IsSuccess, retried.Error?.Code);
        Assert.Equal(2, await ScalarAsync(host, "SELECT last_value FROM public.test_b17_attempts"));
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.ClinicId == w.ClinicId, Ct));
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }
}
