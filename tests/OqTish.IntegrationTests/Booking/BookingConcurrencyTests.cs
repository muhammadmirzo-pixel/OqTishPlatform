using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Notifications;
using OqTish.Application.Scheduling;
using OqTish.Domain.Booking;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Infrastructure;

namespace OqTish.IntegrationTests.Booking;

/// <summary>
/// TESTING.md §3 concurrency catalogue (B1, B2, B4, B6, B7, B9, B10, B12): each test loops
/// <see cref="Iterations"/> times over a fresh fixture, releases the racing operations together
/// (<see cref="Race"/>), and asserts both the outcomes and the database state; B18 invariants are checked at
/// the end. Real PostgreSQL, real handlers and transactions.
/// </summary>
[Trait("Category", "Concurrency")]
public class BookingConcurrencyTests(PostgresFixture postgres)
{
    public const int Iterations = 50;

    private static readonly AppointmentStatus[] ConfirmOrExpire = [AppointmentStatus.Confirmed, AppointmentStatus.Expired];
    private static readonly string[] LateConfirmCodes = ["booking.expired", "booking.not_pending"];
    private static readonly string[] SlotOrBusy = ["booking.slot_unavailable", "booking.doctor_busy"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Report(string name, Stopwatch sw, string? extra = null) =>
        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {Iterations} iterations in {sw.Elapsed.TotalSeconds:F1} s{(extra is null ? string.Empty : "; " + extra)}");

    private static (Guid?, Func<IServiceProvider, Task<Result<AppointmentDto>>>) Book(BookingWorld w, Guid patient, DateTimeOffset start, Guid? service = null, string? key = null) =>
        (patient, sp => sp.GetRequiredService<CreateAppointmentHandler>().HandleAsync(w.Request(start, service), key, Ct));

    [Fact]
    public async Task B1_twenty_concurrent_requests_for_one_doctor_slot_give_one_hold()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 20);
            var results = await Race.RunAsync(host, [.. w.Patients.Select(p => Book(w, p, w.At(10)))]);

            Assert.Single(results, r => r.IsSuccess);
            Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal("booking.slot_unavailable", r.Error!.Code));
            Assert.Equal(1, await db.Appointments.CountAsync(a => a.ClinicId == w.ClinicId, Ct));
            Assert.Equal(1, await db.DoctorTimeBlocks.CountAsync(b => b.ClinicId == w.ClinicId && b.IsActive, Ct));
            Assert.Equal(AppointmentStatus.Pending, (await db.Appointments.SingleAsync(a => a.ClinicId == w.ClinicId, Ct)).Status);
        }

        Report("B1", sw);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    /// <summary>
    /// B1 across branches: branch locks differ, so only the exclusion constraint serialises the shared doctor A.
    /// Ten patient requests at branch 1 (doctors A and B) race ten walk-ins for doctor A at branch 2: exactly two
    /// bookings win, on different doctors (a request that loses A on 23P01 rolls back to its savepoint and takes B).
    /// </summary>
    [Fact]
    public async Task B1_cross_branch_requests_and_walk_ins_for_a_shared_doctor_never_double_book()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 2, patients: 10);
            var now = DateTimeOffset.UtcNow;
            var branch2 = Domain.Tenancy.Branch.Create(w.ClinicId, "Filial 2", "Chilonzor", "Test 2", null, null, null, "Asia/Tashkent", now);
            db.Branches.Add(branch2);
            await db.SaveChangesAsync(Ct);
            db.BranchServices.Add(Domain.Catalog.BranchService.Create(w.ClinicId, branch2.Id, w.Service30, Domain.Common.Money.Uzs(100000), Domain.Catalog.PriceKind.Fixed, 30, true, now));
            db.DoctorBranches.Add(Domain.Scheduling.DoctorBranch.Create(w.ClinicId, w.Doctors[0], branch2.Id));
            await db.SaveChangesAsync(Ct);
            var start = w.At(10);
            var walkIn = new WalkInCommand(branch2.Id, w.Service30, start, w.Doctors[0], "Walk-in", "+998901112233");
            var results = await Race.RunAsync(host,
            [
                .. w.Patients.Select(p => Book(w, p, start)),
                .. Enumerable.Range(0, 10).Select(_ => ((Guid?)w.StaffUserId,
                    (Func<IServiceProvider, Task<Result<AppointmentDto>>>)(sp => sp.GetRequiredService<WalkInAppointmentHandler>().HandleAsync(w.ClinicId, walkIn, null, Ct)))),
            ]);

            Assert.Equal(2, results.Count(r => r.IsSuccess));
            Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Contains(r.Error!.Code, SlotOrBusy));
            var held = await db.DoctorTimeBlocks.Where(b => b.ClinicId == w.ClinicId && b.IsActive).Select(b => b.DoctorId).ToListAsync(Ct);
            Assert.Equal(w.Doctors.Order(), held.Order());
        }

        Report("B1-cross-branch", sw);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    /// <summary>
    /// OD-07 under concurrency: one patient fires six requests at two branches at once (branch locks differ, so
    /// only the per-patient advisory lock serialises the Pending count): exactly three succeed.
    /// </summary>
    [Fact]
    public async Task Pending_limit_per_patient_holds_across_branches_under_concurrency()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
            var now = DateTimeOffset.UtcNow;
            var branch2 = Domain.Tenancy.Branch.Create(w.ClinicId, "Filial 2", "Chilonzor", "Test 2", null, null, null, "Asia/Tashkent", now);
            var doctor2 = Domain.Scheduling.Doctor.Create(w.ClinicId, "Dr. Z", true, null, now);
            db.Branches.Add(branch2);
            db.Doctors.Add(doctor2);
            await db.SaveChangesAsync(Ct);
            db.BranchServices.Add(Domain.Catalog.BranchService.Create(w.ClinicId, branch2.Id, w.Service30, Domain.Common.Money.Uzs(100000), Domain.Catalog.PriceKind.Fixed, 30, true, now));
            db.DoctorBranches.Add(Domain.Scheduling.DoctorBranch.Create(w.ClinicId, doctor2.Id, branch2.Id));
            db.DoctorServices.Add(Domain.Scheduling.DoctorService.Create(w.ClinicId, doctor2.Id, w.Service30));
            await db.SaveChangesAsync(Ct);
            for (short day = 1; day <= 7; day++)
            {
                db.WorkingHours.Add(Domain.Scheduling.WorkingHours.Create(w.ClinicId, doctor2.Id, branch2.Id, day, new TimeOnly(8, 0), new TimeOnly(20, 0)));
            }

            await db.SaveChangesAsync(Ct);
            var patient = w.Patients[0];
            var results = await Race.RunAsync(host,
            [
                .. Enumerable.Range(0, 3).Select(k => Book(w, patient, w.At(10 + k))),
                .. Enumerable.Range(0, 3).Select(k => ((Guid?)patient, (Func<IServiceProvider, Task<Result<AppointmentDto>>>)(sp =>
                    sp.GetRequiredService<CreateAppointmentHandler>().HandleAsync(w.Request(w.At(10 + k)) with { BranchId = branch2.Id }, null, Ct)))),
            ]);

            Assert.Equal(3, results.Count(r => r.IsSuccess));
            Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal("booking.too_many_pending", r.Error!.Code));
            Assert.Equal(3, await db.Appointments.CountAsync(a => a.PatientUserId == patient && a.Status == AppointmentStatus.Pending, Ct));
        }

        Report("pending-limit", sw);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B2_overlapping_30_and_60_minute_requests_on_one_doctor_exactly_one_wins()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 2);
            var results = await Race.RunAsync(host, [Book(w, w.Patients[0], w.At(10)), Book(w, w.Patients[1], w.At(10, 15), w.Service60)]);

            Assert.Single(results, r => r.IsSuccess);
            Assert.Equal("booking.slot_unavailable", results.Single(r => !r.IsSuccess).Error!.Code);
            Assert.Equal(1, await db.DoctorTimeBlocks.CountAsync(b => b.ClinicId == w.ClinicId && b.IsActive, Ct));
        }

        Report("B2", sw);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B4_two_doctors_three_concurrent_overlapping_requests_two_succeed_on_different_doctors()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 2, patients: 3);
            var results = await Race.RunAsync(host, [.. w.Patients.Select(p => Book(w, p, w.At(10)))]);

            Assert.Equal(2, results.Count(r => r.IsSuccess));
            Assert.Equal("booking.slot_unavailable", results.Single(r => !r.IsSuccess).Error!.Code);
            var held = await db.DoctorTimeBlocks.Where(b => b.ClinicId == w.ClinicId && b.IsActive).Select(b => b.DoctorId).ToListAsync(Ct);
            Assert.Equal(w.Doctors.Order(), held.Order());
        }

        Report("B4", sw);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B6_two_staff_confirm_the_same_request_concurrently_one_wins()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 2, patients: 1);
            var second = Domain.Identity.User.Create("Admin", DateTimeOffset.UtcNow);
            db.Users.Add(second);
            await db.SaveChangesAsync(Ct);
            db.ClinicMemberships.Add(ClinicMembership.Create(w.ClinicId, second.Id, StaffRole.ClinicAdmin, MembershipScope.Clinic, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
            var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;

            var results = await Race.RunAsync<Result<AppointmentDto>>(host,
            [
                (w.StaffUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>().ConfirmAsync(w.ClinicId, booked.Id, new ConfirmCommand(w.Doctors[0]), Ct)),
                (second.Id, sp => sp.GetRequiredService<StaffAppointmentHandlers>().ConfirmAsync(w.ClinicId, booked.Id, new ConfirmCommand(w.Doctors[1]), Ct)),
            ]);

            var winner = Assert.Single(results, r => r.IsSuccess);
            Assert.Equal("booking.not_pending", results.Single(r => !r.IsSuccess).Error!.Code);
            var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id, Ct);
            Assert.Equal((AppointmentStatus.Confirmed, winner.Value.Doctor!.Id), (appointment.Status, appointment.DoctorId!.Value));
            Assert.Equal(1, await db.AppointmentStatusHistory.CountAsync(h => h.AppointmentId == booked.Id && h.To == AppointmentStatus.Confirmed, Ct));
            Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Type == BookingNotificationTypes.ConfirmedPatient && m.DedupKey.Contains(booked.Id.ToString("N")), Ct));
        }

        Report("B6", sw);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B7_confirm_racing_the_sweeper_at_the_deadline_yields_exactly_one_outcome()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        var outcomes = new Dictionary<AppointmentStatus, int>();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
            var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
            // Controlled clock: the deadline is set relative to database time so that it falls inside the race window.
            await host.SetDeadlineAsync(booked.Id, TimeSpan.FromMilliseconds(25));

            Result<AppointmentDto>? confirm = null;
            await Race.RunAsync<int>(host,
            [
                (w.StaffUserId, async sp =>
                {
                    await Task.Delay(i % 10 * 6, Ct);
                    confirm = await sp.GetRequiredService<StaffAppointmentHandlers>().ConfirmAsync(w.ClinicId, booked.Id, new ConfirmCommand(w.Doctors[0]), Ct);
                    return 0;
                }),
                (null, async sp =>
                {
                    var expired = 0;
                    for (var k = 0; k < 15 && expired == 0; k++)
                    {
                        expired += await sp.GetRequiredService<ExpirePendingAppointmentsHandler>().RunAsync(Ct);
                        await Task.Delay(5, Ct);
                    }

                    return expired;
                }),
            ]);
            await Task.Delay(80, Ct);
            await host.SweepAsync();

            var a = await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == booked.Id, Ct);
            Assert.Contains(a.Status, ConfirmOrExpire);
            if (confirm!.Value.IsSuccess)
            {
                Assert.Equal(AppointmentStatus.Confirmed, a.Status);
            }
            else
            {
                Assert.Equal(AppointmentStatus.Expired, a.Status);
                Assert.Contains(confirm.Value.Error!.Code, LateConfirmCodes);
            }

            var history = await db.AppointmentStatusHistory.Where(h => h.AppointmentId == booked.Id).Select(h => h.To).ToListAsync(Ct);
            Assert.Equal(2, history.Count);
            Assert.Equal(1, await db.OutboxMessages.CountAsync(m => (m.Type == BookingNotificationTypes.ConfirmedPatient || m.Type == BookingNotificationTypes.ExpiredPatient)
                && m.DedupKey.Contains(booked.Id.ToString("N")), Ct));
            Assert.Equal(a.Status == AppointmentStatus.Confirmed ? 1 : 0, await db.DoctorTimeBlocks.CountAsync(b => b.AppointmentId == booked.Id && b.IsActive, Ct));
            outcomes[a.Status] = outcomes.GetValueOrDefault(a.Status) + 1;
        }

        Report("B7", sw, string.Join(", ", outcomes.Select(o => $"{o.Key}={o.Value}")));
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B9_patient_cancel_racing_staff_confirm_is_consistent()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        var confirmedFirst = 0;
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
            var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
            // Odd iterations give the confirm a head start so that both orders are exercised.
            var head = i % 2 == 1 ? 25 : 0;
            (Guid?, Func<IServiceProvider, Task<Result<AppointmentDto>>>) cancel = (w.Patients[0], async sp =>
            {
                await Task.Delay(head, Ct);
                return await sp.GetRequiredService<PatientAppointmentHandlers>().CancelAsync(booked.Id, Ct);
            });
            (Guid?, Func<IServiceProvider, Task<Result<AppointmentDto>>>) confirmCall = (w.StaffUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>().ConfirmAsync(w.ClinicId, booked.Id, new ConfirmCommand(w.Doctors[0]), Ct));
            var raced = await Race.RunAsync<Result<AppointmentDto>>(host, i % 2 == 0 ? [cancel, confirmCall] : [confirmCall, cancel]);
            var results = i % 2 == 0 ? raced : [raced[1], raced[0]];

            // The cancel always succeeds (Pending any time; Confirmed until start − 2 h). The confirm either came
            // first (then the cancel cancels a Confirmed appointment) or lost with booking.not_pending.
            Assert.True(results[0].IsSuccess, results[0].Error?.Code);
            var history = await db.AppointmentStatusHistory.Where(h => h.AppointmentId == booked.Id).OrderBy(h => h.OccurredAt).Select(h => h.To).ToListAsync(Ct);
            if (results[1].IsSuccess)
            {
                confirmedFirst++;
                Assert.Equal([AppointmentStatus.Pending, AppointmentStatus.Confirmed, AppointmentStatus.CancelledByPatient], history);
            }
            else
            {
                Assert.Equal("booking.not_pending", results[1].Error!.Code);
                Assert.Equal([AppointmentStatus.Pending, AppointmentStatus.CancelledByPatient], history);
            }

            Assert.Equal(AppointmentStatus.CancelledByPatient, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id, Ct)).Status);
            Assert.Equal(0, await db.DoctorTimeBlocks.CountAsync(b => b.AppointmentId == booked.Id && b.IsActive, Ct));
        }

        Report("B9", sw, $"confirm first {confirmedFirst}, cancel first {Iterations - confirmedFirst}");
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B10_time_off_racing_a_confirm_onto_the_same_doctor_one_commits()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        var confirmWins = 0;
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 2, patients: 1);
            var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
            var holder = await host.HeldDoctorAsync(booked.Id);
            var target = w.Doctors.Single(d => d != holder);
            var head = i % 2 == 1 ? 25 : 0; // odd iterations: the confirm gets a head start
            (Guid?, Func<IServiceProvider, Task<bool>>) confirmCall = (w.StaffUserId, async sp =>
            {
                var r = await sp.GetRequiredService<StaffAppointmentHandlers>().ConfirmAsync(w.ClinicId, booked.Id, new ConfirmCommand(target), Ct);
                return r.IsSuccess || (r.Error!.Code == "booking.doctor_busy" ? false : throw new InvalidOperationException(r.Error.Code));
            });
            (Guid?, Func<IServiceProvider, Task<bool>>) timeOffCall = (w.StaffUserId, async sp =>
            {
                await Task.Delay(head, Ct);
                var r = await sp.GetRequiredService<TimeOffHandlers>().CreateAsync(w.ClinicId, target, new TimeOffCommand(w.At(9), w.At(12), "race"), Ct);
                return r.IsSuccess || (r.Error!.Code == "schedule.timeoff_conflict" ? false : throw new InvalidOperationException(r.Error.Code));
            });
            var raced = await Race.RunAsync<bool>(host, i % 2 == 0 ? [confirmCall, timeOffCall] : [timeOffCall, confirmCall]);
            var results = i % 2 == 0 ? raced : [raced[1], raced[0]];

            Assert.Single(results, ok => ok);
            var timeOff = await db.DoctorTimeBlocks.CountAsync(b => b.DoctorId == target && b.IsActive && b.Kind == Domain.Scheduling.TimeBlockKind.TimeOff, Ct);
            var status = (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id, Ct)).Status;
            if (results[0])
            {
                confirmWins++;
                Assert.Equal((AppointmentStatus.Confirmed, 0), (status, timeOff));
            }
            else
            {
                Assert.Equal((AppointmentStatus.Pending, 1), (status, timeOff));
            }
        }

        Report("B10", sw, $"confirm won {confirmWins}, time off won {Iterations - confirmWins}");
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task B12_concurrent_duplicates_with_one_idempotency_key_create_one_appointment()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
            var key = "k-" + Guid.NewGuid().ToString("N");
            var results = await Race.RunAsync(host, [.. Enumerable.Range(0, 5).Select(_ => Book(w, w.Patients[0], w.At(10), key: key))]);

            Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Code));
            Assert.Single(results.Select(r => r.Value.Id).Distinct());
            Assert.Equal(1, await db.Appointments.CountAsync(a => a.ClinicId == w.ClinicId, Ct));
            Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Type == BookingNotificationTypes.RequestedStaff && m.DedupKey.Contains(results[0].Value.Id.ToString("N")), Ct));
        }

        Report("B12", sw);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }
}
