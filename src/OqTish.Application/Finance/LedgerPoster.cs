using Microsoft.EntityFrameworkCore;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Finance;

/// <summary>
/// The only writer of <c>finance.journal_entries</c> / <c>finance.ledger_lines</c> (docs/SERVICES.md §4). Every
/// method runs inside the caller's transaction (after the caller's row locks), is idempotent through the UNIQUE
/// <c>source_event_key</c> (an existing entry is returned / skipped), resolves the reporting day through
/// <see cref="PeriodLock"/>, rounds once per line and saves its rows so that later queries in the same
/// transaction see them. Sign convention: debit &gt; 0, credit &lt; 0.
/// <list type="table">
/// <item><term>Completion C</term><description>Dr PatientReceivable +C / Cr ClinicServiceRevenue −C (only when C &gt; 0)</description></item>
/// <item><term>Payment P</term><description>Dr ClinicCollected{method} +P / Cr PatientReceivable −P</description></item>
/// <item><term>Refund R</term><description>Cr ClinicRefundsPaid −R / Dr PatientReceivable +min(R, credit) / Dr ClinicServiceRevenue +rest</description></item>
/// <item><term>Failed payment</term><description>exact reversal of the payment entry (+ remaining commission)</description></item>
/// <item><term>Commission X</term><description>Dr PlatformCommissionReceivable +X / Cr PlatformCommissionRevenue −X (rule snapshot on both lines)</description></item>
/// <item><term>Settlement paid A</term><description>Dr SettlementClearing +A / Cr PlatformCommissionReceivable −A</description></item>
/// </list>
/// </summary>
public sealed class LedgerPoster(IOqTishDb db, PeriodLock periods, IRowLocks locks, TimeProvider clock)
{
    public static string CompletionRevenueKey(Guid completionId) => $"completion:{completionId}:revenue";

    public static string CompletionCommissionKey(Guid completionId) => $"completion:{completionId}:commission";

    public static string NewPatientKey(Guid clinicId, Guid patientUserId) => $"clinic:{clinicId}:new-patient:{patientUserId}";

    public static string ConfirmedCommissionKey(Guid appointmentId) => $"appointment:{appointmentId}:confirmed:commission";

    public static string PaymentKey(Guid paymentId) => $"payment:{paymentId}:recorded";

    public static string PaymentCommissionKey(Guid paymentId) => $"payment:{paymentId}:commission";

    public static string RefundKey(Guid paymentId, Guid refundId) => $"payment:{paymentId}:refund:{refundId}";

    public static string FailedKey(Guid paymentId) => $"payment:{paymentId}:failed";

    public static string SettlementPaidKey(Guid settlementId) => $"settlement:{settlementId}:paid";

    // ------------------------------------------------------------------ completion / confirmation hooks

    /// <summary>
    /// ServiceCompletion + revenue entry + CompletedService commission (% of the charged amount, rule at the
    /// completion time) + NewPatient fee (first completed visit of the patient account at the clinic, under a
    /// per-patient advisory lock). Idempotent per appointment.
    /// </summary>
    public async Task<ServiceCompletion> PostCompletionAsync(AppointmentCompletedContext c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        var existing = await db.ServiceCompletions.FirstOrDefaultAsync(x => x.AppointmentId == c.AppointmentId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var date = await PostingDateAsync(c.ClinicId, c.BranchId, c.CompletedAt, ct);
        var completion = ServiceCompletion.Create(c.ClinicId, c.BranchId, c.AppointmentId, c.ServiceId, c.DoctorId, c.PatientUserId, c.ChargedAmount,
            c.ActorUserId, c.CompletedAt, date);

        // NewPatient: decide before this completion is inserted (first completed visit of the account at the clinic).
        var firstVisit = false;
        if (c.PatientUserId is { } patient)
        {
            await locks.AdvisoryLockAsync($"finance-new-patient:{c.ClinicId}:{patient}", ct);
            firstVisit = !await db.ServiceCompletions.AnyAsync(x => x.ClinicId == c.ClinicId && x.PatientUserId == patient, ct);
        }

        db.ServiceCompletions.Add(completion);
        var charged = c.ChargedAmount;
        if (charged.IsPositive)
        {
            var revenue = NewEntry(c.ClinicId, c.BranchId, JournalEntryType.ServiceCompleted, CompletionRevenueKey(completion.Id), date, c.CompletedAt, c.ActorUserId,
                appointmentId: c.AppointmentId, serviceCompletionId: completion.Id);
            revenue.AddLine(LedgerAccount.PatientReceivable, LedgerParty.Patient, charged);
            revenue.AddLine(LedgerAccount.ClinicServiceRevenue, LedgerParty.Clinic, -charged);
            db.JournalEntries.Add(revenue);

            if (await RuleAsync(c.ClinicId, CommissionBasis.CompletedService, c.CompletedAt, ct) is { Rate: { } rate } rule)
            {
                var amount = CommissionMath.PercentageLine(charged, rate);
                AddCommission(c.ClinicId, c.BranchId, CompletionCommissionKey(completion.Id), date, c.CompletedAt, c.ActorUserId, amount, rule, rate, charged,
                    appointmentId: c.AppointmentId, serviceCompletionId: completion.Id);
            }
        }

        var newPatientKey = c.PatientUserId is { } p ? NewPatientKey(c.ClinicId, p) : null;
        if (firstVisit && newPatientKey is not null
            && await RuleAsync(c.ClinicId, CommissionBasis.NewPatient, c.CompletedAt, ct) is { FixedFeeMoney: { } fee } newPatientRule
            && !await db.JournalEntries.AnyAsync(e => e.SourceEventKey == newPatientKey, ct))
        {
            AddCommission(c.ClinicId, c.BranchId, newPatientKey, date, c.CompletedAt, c.ActorUserId, fee, newPatientRule, null, fee,
                appointmentId: c.AppointmentId, serviceCompletionId: completion.Id);
        }

        await db.SaveChangesAsync(ct);
        return completion;
    }

    /// <summary>ConfirmedAppointment fixed fee (rule at the confirmation time). Never reversed (OD-F3).</summary>
    public async Task PostConfirmationAsync(AppointmentConfirmedContext c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        var key = ConfirmedCommissionKey(c.AppointmentId);
        if (await db.JournalEntries.AnyAsync(e => e.SourceEventKey == key, ct)
            || await RuleAsync(c.ClinicId, CommissionBasis.ConfirmedAppointment, c.ConfirmedAt, ct) is not { FixedFeeMoney: { } fee } rule)
        {
            return;
        }

        var date = await PostingDateAsync(c.ClinicId, c.BranchId, c.ConfirmedAt, ct);
        AddCommission(c.ClinicId, c.BranchId, key, date, c.ConfirmedAt, c.ActorUserId, fee, rule, null, fee, appointmentId: c.AppointmentId);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ payments

    /// <summary>Recorded incoming payment: money entry + CollectedPayment commission (rule at the payment time).</summary>
    public async Task PostPaymentAsync(Payment payment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.Direction != PaymentDirection.In || payment.Status != PaymentStatus.Recorded)
        {
            throw new InvalidOperationException("Only recorded incoming payments are posted.");
        }

        await db.SaveChangesAsync(ct);
        var key = PaymentKey(payment.Id);
        if (await db.JournalEntries.AnyAsync(e => e.SourceEventKey == key, ct))
        {
            return;
        }

        var date = await PostingDateAsync(payment.ClinicId, payment.BranchId, payment.RecordedAt, ct);
        var amount = payment.AmountMoney;
        var entry = NewEntry(payment.ClinicId, payment.BranchId, JournalEntryType.PaymentRecorded, key, date, payment.RecordedAt, payment.RecordedByUserId,
            appointmentId: payment.AppointmentId, paymentId: payment.Id);
        entry.AddLine(LedgerAccounts.CollectedFor(payment.Method), LedgerParty.Clinic, amount);
        entry.AddLine(LedgerAccount.PatientReceivable, LedgerParty.Patient, -amount);
        db.JournalEntries.Add(entry);

        if (await RuleAsync(payment.ClinicId, CommissionBasis.CollectedPayment, payment.RecordedAt, ct) is { Rate: { } rate } rule)
        {
            AddCommission(payment.ClinicId, payment.BranchId, PaymentCommissionKey(payment.Id), date, payment.RecordedAt, payment.RecordedByUserId,
                CommissionMath.PercentageLine(amount, rate), rule, rate, amount, appointmentId: payment.AppointmentId, paymentId: payment.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Refund R of <paramref name="original"/>, in the refund's own period (F9). The refund first returns the
    /// patient's credit on the appointment (negative receivable); the rest is a reduction of the delivered
    /// service's revenue (contra-revenue). Commission: CollectedPayment is reversed in proportion to the
    /// cumulative refunds of this payment; CompletedService in proportion to the cumulative revenue
    /// reductions of the appointment (OD-F3). Fixed fees are not reversed.
    /// </summary>
    public async Task PostRefundAsync(Payment refund, Payment original, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(refund);
        ArgumentNullException.ThrowIfNull(original);
        await db.SaveChangesAsync(ct);
        var key = RefundKey(original.Id, refund.Id);
        if (await db.JournalEntries.AnyAsync(e => e.SourceEventKey == key, ct))
        {
            return;
        }

        var date = await PostingDateAsync(refund.ClinicId, refund.BranchId, refund.RecordedAt, ct);
        var amount = refund.AmountMoney;
        var receivable = await AccountBalanceAsync(refund.AppointmentId, LedgerAccount.PatientReceivable, amount.Currency, ct);
        var credit = receivable.IsNegative ? receivable.Negate() : Money.Zero(amount.Currency);
        var toReceivable = amount < credit ? amount : credit;
        var concession = amount - toReceivable;

        var entry = NewEntry(refund.ClinicId, refund.BranchId, JournalEntryType.PaymentRefunded, key, date, refund.RecordedAt, refund.RecordedByUserId,
            appointmentId: refund.AppointmentId, paymentId: refund.Id);
        entry.AddLine(LedgerAccount.ClinicRefundsPaid, LedgerParty.Clinic, -amount);
        if (toReceivable.IsPositive)
        {
            entry.AddLine(LedgerAccount.PatientReceivable, LedgerParty.Patient, toReceivable);
        }

        if (concession.IsPositive)
        {
            entry.AddLine(LedgerAccount.ClinicServiceRevenue, LedgerParty.Clinic, concession);
        }

        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        // CollectedPayment: cumulative refunds of this payment / payment amount.
        if (await CommissionEntryAsync(PaymentCommissionKey(original.Id), ct) is { } paymentCommission)
        {
            var refundedSoFar = await RefundedAsync(original.Id, amount.Currency, ct);
            await ReverseProportionallyAsync(paymentCommission, original.AmountMoney, refundedSoFar, $"{key}:commission", date, refund.RecordedAt,
                refund.RecordedByUserId, refund.Id, amount, ct);
        }

        // CompletedService: cumulative revenue reductions of the appointment / charged amount.
        if (concession.IsPositive
            && await db.ServiceCompletions.AsNoTracking().FirstOrDefaultAsync(x => x.AppointmentId == refund.AppointmentId, ct) is { } completion
            && await CommissionEntryAsync(CompletionCommissionKey(completion.Id), ct) is { } completionCommission)
        {
            var reducedSoFar = await RevenueReductionsAsync(refund.AppointmentId, amount.Currency, ct);
            await ReverseProportionallyAsync(completionCommission, completion.ChargedMoney, reducedSoFar, $"{key}:completion-commission", date, refund.RecordedAt,
                refund.RecordedByUserId, refund.Id, concession, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>A recorded payment marked failed: exact reversal of its money entry and of its remaining commission (F11).</summary>
    public async Task PostPaymentFailedAsync(Payment payment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.Status != PaymentStatus.Failed || payment.FailedAt is not { } failedAt)
        {
            throw new InvalidOperationException("The payment must be marked failed first.");
        }

        await db.SaveChangesAsync(ct);
        var key = FailedKey(payment.Id);
        var paymentKey = PaymentKey(payment.Id);
        if (await db.JournalEntries.AnyAsync(e => e.SourceEventKey == key, ct)
            || await db.JournalEntries.AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.SourceEventKey == paymentKey, ct) is not { } original)
        {
            return;
        }

        var actor = payment.FailedByUserId;
        var date = await PostingDateAsync(payment.ClinicId, payment.BranchId, failedAt, ct);
        var reversal = NewEntry(payment.ClinicId, payment.BranchId, JournalEntryType.PaymentReversed, key, date, failedAt, actor,
            appointmentId: payment.AppointmentId, paymentId: payment.Id, reversesEntryId: original.Id, description: "Payment marked failed");
        foreach (var line in original.Lines)
        {
            reversal.AddLine(line.Account, line.Party, line.AmountMoney.Negate());
        }

        db.JournalEntries.Add(reversal);
        await db.SaveChangesAsync(ct);

        if (await CommissionEntryAsync(PaymentCommissionKey(payment.Id), ct) is { } commission)
        {
            var amount = payment.AmountMoney;
            await ReverseProportionallyAsync(commission, amount, amount, $"{key}:commission", date, failedAt, actor, payment.Id, amount, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ settlements

    /// <summary>The clinic paid the settlement: Dr SettlementClearing / Cr PlatformCommissionReceivable (clinic level, Asia/Tashkent day).</summary>
    public async Task PostSettlementPaidAsync(Settlement settlement, Guid actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        var key = SettlementPaidKey(settlement.Id);
        if (settlement.PaidAt is not { } paidAt || settlement.AmountDueMoney.IsZero || await db.JournalEntries.AnyAsync(e => e.SourceEventKey == key, ct))
        {
            return;
        }

        var date = await PostingDateAsync(settlement.ClinicId, null, paidAt, ct);
        var entry = NewEntry(settlement.ClinicId, null, JournalEntryType.SettlementPaid, key, date, paidAt, actorUserId, settlementId: settlement.Id);
        entry.AddLine(LedgerAccount.SettlementClearing, LedgerParty.Platform, settlement.AmountDueMoney);
        entry.AddLine(LedgerAccount.PlatformCommissionReceivable, LedgerParty.Platform, settlement.AmountDueMoney.Negate());
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Branch-local day of the event (Asia/Tashkent without a branch), redirected out of locked months.</summary>
    public async Task<DateOnly> PostingDateAsync(Guid clinicId, Guid? branchId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var timeZone = branchId is { } b
            ? await db.Branches.AsNoTracking().Where(x => x.Id == b).Select(x => x.TimeZone).FirstOrDefaultAsync(ct)
            : null;
        return await periods.ResolveAsync(clinicId, FinanceTime.LocalDate(occurredAt, timeZone ?? Branch.DefaultTimeZone), ct);
    }

    /// <summary>The rule of the clinic and basis whose range contains the basis event time (OD-F2); none = no commission (OD-F1).</summary>
    public Task<CommissionRule?> RuleAsync(Guid clinicId, CommissionBasis basis, DateTimeOffset at, CancellationToken ct) =>
        db.CommissionRules.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.Basis == basis && r.EffectiveFrom <= at && (r.EffectiveTo == null || r.EffectiveTo > at))
            .FirstOrDefaultAsync(ct);

    private JournalEntry NewEntry(
        Guid clinicId,
        Guid? branchId,
        JournalEntryType type,
        string key,
        DateOnly date,
        DateTimeOffset occurredAt,
        Guid? actor,
        Guid? appointmentId = null,
        Guid? paymentId = null,
        Guid? serviceCompletionId = null,
        Guid? settlementId = null,
        Guid? reversesEntryId = null,
        string? description = null) =>
        JournalEntry.Create(clinicId, branchId, type, key, date, occurredAt, clock.GetUtcNow(), actor, appointmentId, paymentId, serviceCompletionId, settlementId,
            reversesEntryId, description);

    private void AddCommission(
        Guid clinicId,
        Guid branchId,
        string key,
        DateOnly date,
        DateTimeOffset occurredAt,
        Guid? actor,
        Money amount,
        CommissionRule rule,
        decimal? rate,
        Money basis,
        Guid? appointmentId = null,
        Guid? paymentId = null,
        Guid? serviceCompletionId = null)
    {
        if (!amount.IsPositive)
        {
            return;
        }

        var entry = NewEntry(clinicId, branchId, JournalEntryType.Commission, key, date, occurredAt, actor, appointmentId, paymentId, serviceCompletionId);
        entry.AddLine(LedgerAccount.PlatformCommissionReceivable, LedgerParty.Platform, amount, rule.Id, rate, basis);
        entry.AddLine(LedgerAccount.PlatformCommissionRevenue, LedgerParty.Platform, -amount, rule.Id, rate, basis);
        db.JournalEntries.Add(entry);
    }

    private Task<JournalEntry?> CommissionEntryAsync(string key, CancellationToken ct) =>
        db.JournalEntries.AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.SourceEventKey == key, ct);

    /// <summary>
    /// Posts <c>round(original × reduced / basis) − alreadyReversed</c> as a CommissionReversal of
    /// <paramref name="commission"/> with the original rule snapshot (the refund does not re-select a rule).
    /// </summary>
    private async Task ReverseProportionallyAsync(
        JournalEntry commission,
        Money basis,
        Money reducedSoFar,
        string key,
        DateOnly date,
        DateTimeOffset occurredAt,
        Guid? actor,
        Guid paymentId,
        Money reducedBy,
        CancellationToken ct)
    {
        if (await db.JournalEntries.AnyAsync(e => e.SourceEventKey == key, ct))
        {
            return;
        }

        var receivableLine = commission.Lines.Single(l => l.Account == LedgerAccount.PlatformCommissionReceivable);
        var original = receivableLine.AmountMoney;
        var commissionId = commission.Id;
        var reversedLines = await (
                from l in db.LedgerLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                where e.ReversesEntryId == commissionId && l.Account == LedgerAccount.PlatformCommissionReceivable
                select l.Amount)
            .ToListAsync(ct);
        var alreadyReversed = new Money(-reversedLines.Sum(), original.Currency);
        var delta = CommissionMath.ProportionalReversal(original, basis, reducedSoFar, alreadyReversed);
        if (!delta.IsPositive)
        {
            return;
        }

        var entry = NewEntry(commission.ClinicId, commission.BranchId, JournalEntryType.CommissionReversal, key, date, occurredAt, actor, commission.AppointmentId, paymentId,
            commission.ServiceCompletionId, reversesEntryId: commission.Id);
        entry.AddLine(LedgerAccount.PlatformCommissionReceivable, LedgerParty.Platform, -delta, receivableLine.CommissionRuleId, receivableLine.RateSnapshot, reducedBy);
        entry.AddLine(LedgerAccount.PlatformCommissionRevenue, LedgerParty.Platform, delta, receivableLine.CommissionRuleId, receivableLine.RateSnapshot, reducedBy);
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Money> AccountBalanceAsync(Guid appointmentId, LedgerAccount account, Currency currency, CancellationToken ct)
    {
        var sum = await (
                from l in db.LedgerLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                where e.AppointmentId == appointmentId && l.Account == account && l.Currency == currency
                select (decimal?)l.Amount)
            .SumAsync(ct);
        return new Money(sum ?? 0m, currency);
    }

    private async Task<Money> RefundedAsync(Guid paymentId, Currency currency, CancellationToken ct)
    {
        var sum = await db.Payments.AsNoTracking()
            .Where(p => p.RefundOfPaymentId == paymentId && p.Direction == PaymentDirection.Refund && p.Status == PaymentStatus.Recorded)
            .SumAsync(p => (decimal?)p.Amount, ct);
        return new Money(sum ?? 0m, currency);
    }

    private async Task<Money> RevenueReductionsAsync(Guid appointmentId, Currency currency, CancellationToken ct)
    {
        var sum = await (
                from l in db.LedgerLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                where e.AppointmentId == appointmentId && e.EntryType == JournalEntryType.PaymentRefunded && l.Account == LedgerAccount.ClinicServiceRevenue
                select (decimal?)l.Amount)
            .SumAsync(ct);
        return new Money(sum ?? 0m, currency);
    }
}
