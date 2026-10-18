using OqTish.Domain.Booking;

namespace OqTish.UnitTests.Booking;

/// <summary>Every (status, action) pair: exactly the documented transitions are allowed.</summary>
public class AppointmentStateMachineTests
{
    private static readonly Dictionary<(AppointmentStatus, AppointmentAction), AppointmentStatus> Expected = new()
    {
        [(AppointmentStatus.Pending, AppointmentAction.Confirm)] = AppointmentStatus.Confirmed,
        [(AppointmentStatus.Pending, AppointmentAction.Reject)] = AppointmentStatus.Rejected,
        [(AppointmentStatus.Pending, AppointmentAction.CancelByPatient)] = AppointmentStatus.CancelledByPatient,
        [(AppointmentStatus.Pending, AppointmentAction.Expire)] = AppointmentStatus.Expired,
        [(AppointmentStatus.Confirmed, AppointmentAction.CancelByPatient)] = AppointmentStatus.CancelledByPatient,
        [(AppointmentStatus.Confirmed, AppointmentAction.CancelByClinic)] = AppointmentStatus.CancelledByClinic,
        [(AppointmentStatus.Confirmed, AppointmentAction.Reschedule)] = AppointmentStatus.Confirmed,
        [(AppointmentStatus.Confirmed, AppointmentAction.Complete)] = AppointmentStatus.Completed,
        [(AppointmentStatus.Confirmed, AppointmentAction.NoShow)] = AppointmentStatus.NoShow,
    };

    public static TheoryData<AppointmentStatus, AppointmentAction> AllPairs()
    {
        var data = new TheoryData<AppointmentStatus, AppointmentAction>();
        foreach (var s in Enum.GetValues<AppointmentStatus>())
        {
            foreach (var a in Enum.GetValues<AppointmentAction>())
            {
                data.Add(s, a);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Transition_is_allowed_exactly_when_documented(AppointmentStatus from, AppointmentAction action)
    {
        var allowed = AppointmentStateMachine.TryTransition(from, action, out var to);
        Assert.Equal(Expected.ContainsKey((from, action)), allowed);
        Assert.Equal(allowed, AppointmentStateMachine.CanApply(from, action));
        if (allowed)
        {
            Assert.Equal(Expected[(from, action)], to);
        }
    }

    [Fact]
    public void Terminal_statuses_allow_nothing()
    {
        var terminal = new[] { AppointmentStatus.Rejected, AppointmentStatus.CancelledByPatient, AppointmentStatus.CancelledByClinic, AppointmentStatus.Expired, AppointmentStatus.Completed, AppointmentStatus.NoShow };
        Assert.All(terminal, s => Assert.True(AppointmentStateMachine.IsTerminal(s)));
        Assert.False(AppointmentStateMachine.IsTerminal(AppointmentStatus.Pending));
        Assert.False(AppointmentStateMachine.IsTerminal(AppointmentStatus.Confirmed));
        Assert.Equal(Expected.Count, AppointmentStateMachine.AllowedPairs.Count);
    }

    [Fact]
    public void Time_holding_and_doctor_required_sets_match_the_database_checks()
    {
        Assert.Equal([AppointmentStatus.Pending, AppointmentStatus.Confirmed], AppointmentStateMachine.HoldsTime.Order());
        Assert.Equal([AppointmentStatus.Confirmed, AppointmentStatus.Completed, AppointmentStatus.NoShow], AppointmentStateMachine.RequiresDoctor.Order());
    }

    [Fact]
    public void Factories_validate_their_inputs()
    {
        var now = DateTimeOffset.UtcNow;
        var start = now.AddDays(1);
        var price = Domain.Common.Money.Uzs(100);
        var pending = Appointment.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), price, Domain.Catalog.PriceKind.Fixed, " Ali ", "+998901234567", now, now.AddMinutes(30));
        Assert.Equal((AppointmentStatus.Pending, AppointmentSource.Patient, "Ali", (Guid?)null), (pending.Status, pending.Source, pending.ContactName, pending.DoctorId));
        var walkIn = Appointment.CreateWalkIn(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), price, Domain.Catalog.PriceKind.Fixed, "Ali", "+998901234567", Guid.NewGuid(), now);
        Assert.Equal((AppointmentStatus.Confirmed, AppointmentSource.WalkIn, (Guid?)null, (DateTimeOffset?)null), (walkIn.Status, walkIn.Source, walkIn.PatientUserId, walkIn.ExpiresAt));
        Assert.Throws<ArgumentException>(() => Appointment.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start, price, Domain.Catalog.PriceKind.Fixed, "Ali", "+998901234567", now, now.AddMinutes(30)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Appointment.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), price, Domain.Catalog.PriceKind.Fixed, "Ali", "+998901234567", now, now));
        Assert.Throws<ArgumentException>(() => Domain.Scheduling.DoctorTimeBlock.ForAppointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), Domain.Scheduling.TimeBlockKind.TimeOff, now));
    }

    [Fact]
    public void Review_and_report_moderation_transitions()
    {
        var now = DateTimeOffset.UtcNow;
        var review = Domain.Reviews.Review.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, " ok ", now);
        Assert.Equal(("ok", Domain.Reviews.ReviewModerationStatus.Pending), (review.Text, review.ModerationStatus));
        Assert.True(review.Publish(Guid.NewGuid(), now));
        Assert.False(review.Publish(Guid.NewGuid(), now));
        Assert.True(review.Hide("spam", Guid.NewGuid(), now));
        Assert.False(review.Hide("spam", Guid.NewGuid(), now));
        Assert.True(review.Publish(Guid.NewGuid(), now));
        Assert.Throws<ArgumentOutOfRangeException>(() => Domain.Reviews.Review.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 6, null, now));
        var report = Domain.Reviews.AbuseReport.Create(Domain.Reviews.ReportTargetType.Clinic, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "r", now);
        Assert.True(report.Resolve("done", Guid.NewGuid(), now));
        Assert.False(report.Resolve("again", Guid.NewGuid(), now));
    }
}
