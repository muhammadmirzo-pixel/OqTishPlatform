using OqTish.Application.Finance;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;

namespace OqTish.UnitTests.Finance;

/// <summary>Commission arithmetic (F5, F6, OD-F3), reporting day (F8), rate wire format and the ledger domain guards.</summary>
public class FinanceRulesTests
{
    [Fact]
    public void F5_seven_and_a_half_percent_of_333333_33_rounds_once_to_25000_00() =>
        Assert.Equal(Money.Uzs(25_000.00m), CommissionMath.PercentageLine(Money.Uzs(333_333.33m), 0.0750m));

    [Fact]
    public void F6_lines_are_rounded_individually_and_summed()
    {
        var lines = Enumerable.Range(0, 3).Select(_ => CommissionMath.PercentageLine(Money.Uzs(0.10m), 0.1500m)).ToList();
        Assert.All(lines, l => Assert.Equal(0.02m, l.Amount));
        Assert.Equal(0.06m, lines.Sum(l => l.Amount));
        Assert.Equal(0.05m, Money.Uzs(0.30m).Multiply(0.15m).Round().Amount); // what the rule forbids
    }

    [Theory]
    [InlineData(30_000, 300_000, 100_000, 0, 10_000)]   // F3
    [InlineData(30_000, 300_000, 300_000, 10_000, 20_000)] // full refund reverses exactly the rest
    [InlineData(0.02, 0.10, 0.03, 0, 0.01)]
    [InlineData(0.02, 0.10, 0.06, 0.01, 0.00)]
    [InlineData(0.02, 0.10, 0.10, 0.01, 0.01)]
    [InlineData(5_000, 100_000, 150_000, 0, 5_000)]    // capped at the basis
    public void Proportional_reversal_is_cumulative_and_never_exceeds_the_original(double commission, double basis, double reduced, double already, double expected) =>
        Assert.Equal(Money.Uzs((decimal)expected), CommissionMath.ProportionalReversal(Money.Uzs((decimal)commission), Money.Uzs((decimal)basis),
            Money.Uzs((decimal)reduced), Money.Uzs((decimal)already)));

    [Fact]
    public void F8_reporting_day_is_the_tashkent_date()
    {
        Assert.Equal(new DateOnly(2026, 3, 31), FinanceTime.LocalDate(new DateTimeOffset(2026, 3, 31, 18, 30, 0, TimeSpan.Zero), "Asia/Tashkent"));
        Assert.Equal(new DateOnly(2026, 4, 1), FinanceTime.LocalDate(new DateTimeOffset(2026, 3, 31, 19, 30, 0, TimeSpan.Zero), "Asia/Tashkent"));
        var (start, end) = FinanceTime.MonthRange(2026, 3);
        Assert.Equal((new DateTimeOffset(2026, 2, 28, 19, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 31, 19, 0, 0, TimeSpan.Zero)), (start, end));
    }

    [Theory]
    [InlineData("0.0750", true, "0.0750")]
    [InlineData("0.075", true, "0.0750")]
    [InlineData("1", true, "1.0000")]
    [InlineData("7.5", true, "7.5000")]
    [InlineData("-0.1", false, null)]
    [InlineData("1e-2", false, null)]
    [InlineData(" 0.1", false, null)]
    [InlineData("", false, null)]
    public void Rate_strings_are_strict_invariant_decimals(string input, bool ok, string? formatted)
    {
        Assert.Equal(ok, FinanceTime.TryParseRate(input, out var rate));
        if (ok)
        {
            Assert.Equal(formatted, FinanceTime.FormatRate(rate));
        }
    }

    [Fact]
    public void Journal_entries_balance_and_lines_are_rounded_and_nonzero()
    {
        var e = JournalEntry.Create(Guid.NewGuid(), Guid.NewGuid(), JournalEntryType.PaymentRecorded, "k", new DateOnly(2026, 3, 1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        e.AddLine(LedgerAccount.ClinicCollectedCash, LedgerParty.Clinic, Money.Uzs(10m));
        Assert.False(e.IsBalanced());
        e.AddLine(LedgerAccount.PatientReceivable, LedgerParty.Patient, Money.Uzs(-10m));
        Assert.True(e.IsBalanced());
        Assert.Throws<ArgumentException>(() => e.AddLine(LedgerAccount.ClinicCollectedCash, LedgerParty.Clinic, Money.Uzs(0.001m)));
        Assert.Throws<ArgumentException>(() => e.AddLine(LedgerAccount.ClinicCollectedCash, LedgerParty.Clinic, Money.Uzs(0m)));
    }

    [Fact]
    public void Commission_rules_validate_basis_amounts_and_only_shorten()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(Guid.NewGuid(), CommissionBasis.CollectedPayment, null, null, from, null, "X", Guid.NewGuid(), from));
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(Guid.NewGuid(), CommissionBasis.CollectedPayment, 0.12345m, null, from, null, "X", Guid.NewGuid(), from));
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(Guid.NewGuid(), CommissionBasis.NewPatient, 0.1m, Money.Uzs(1m), from, null, "X", Guid.NewGuid(), from));
        var rule = CommissionRule.Create(Guid.NewGuid(), CommissionBasis.NewPatient, null, Money.Uzs(20_000m), from, from.AddDays(30), "X", Guid.NewGuid(), from);
        Assert.Throws<InvalidOperationException>(() => rule.End(from.AddDays(31), Guid.NewGuid(), from));
        rule.End(from.AddDays(10), Guid.NewGuid(), from);
        Assert.True(rule.CoversInstant(from.AddDays(9)));
        Assert.False(rule.CoversInstant(from.AddDays(10)));
    }

    [Fact]
    public void Refunds_require_a_recorded_incoming_payment()
    {
        var now = DateTimeOffset.UtcNow;
        var p = Payment.RecordIn(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Money.Uzs(100m), PaymentMethod.Card, PaymentStatus.Recorded, "k", null, Guid.NewGuid(), now);
        var r = Payment.RecordRefund(p, Money.Uzs(40m), "reason", "k2", Guid.NewGuid(), now);
        Assert.Equal((PaymentDirection.Refund, PaymentMethod.Card, p.Id), (r.Direction, r.Method, r.RefundOfPaymentId));
        Assert.Throws<InvalidOperationException>(() => Payment.RecordRefund(r, Money.Uzs(1m), "x", null, Guid.NewGuid(), now));
        p.MarkFailed("bounced", Guid.NewGuid(), now);
        Assert.Throws<InvalidOperationException>(() => p.MarkFailed("again", Guid.NewGuid(), now));
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.RecordIn(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Money.Uzs(0m), PaymentMethod.Cash, PaymentStatus.Recorded, null, null, Guid.NewGuid(), now));
    }
}
