using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Finance;

/// <summary>
/// Monthly settlements (FINANCIAL_RULES §6, OD-F10). Admin actions need <c>platform.finance.settle</c>; the clinic
/// list needs <c>finance.settlement.view</c> (clinic-wide). Obligation = Σ of the clinic's <b>unsettled</b>
/// PlatformCommissionReceivable lines of Commission / CommissionReversal entries posted in the month (no rule →
/// 0, flagged). Concurrency: creation and finalization lock the clinic row (<c>FOR UPDATE</c>) and then take the
/// clinic's period advisory lock exclusively (postings hold it shared, <see cref="PeriodLock"/>); the partial
/// unique index <c>UNIQUE(clinic_id, period_start) WHERE status &lt;&gt; 'Voided'</c> and
/// <c>settlement_items.ledger_line_id UNIQUE</c> are the database backstops (→ 409 <c>finance.settlement_exists</c>).
/// Finalizing re-collects late lines of the month and then locks it. Audit: <c>finance.settlement.*</c>.
/// </summary>
public sealed class SettlementHandlers(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IDbClock clock,
    IAccessService access,
    FinanceAccess financeAccess,
    PeriodLock periods,
    LedgerPoster poster,
    ICurrentUser currentUser,
    IAuditLog audit,
    IDbErrorClassifier dbErrors)
{
    public const string ActivePeriodIndex = "ux_settlements_clinic_period_active";
    public const string LedgerLineUniqueIndex = "ix_settlement_items_ledger_line_id";
    public const int MaxListed = 500;

    public async Task<Result<IReadOnlyList<SettlementDto>>> AdminListAsync(Guid? clinicId, SettlementStatus? status, CancellationToken ct)
    {
        var check = await financeAccess.CheckPlatformAsync(ct);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var q = db.Settlements.AsNoTracking().AsQueryable();
        if (clinicId is { } c)
        {
            q = q.Where(s => s.ClinicId == c);
        }

        if (status is { } st)
        {
            q = q.Where(s => s.Status == st);
        }

        return await ToDtosAsync(q, ct);
    }

    /// <summary><c>GET /api/clinics/{clinicId}/settlements</c> (finance.settlement.view, or platform.finance.settle).</summary>
    public async Task<Result<IReadOnlyList<SettlementDto>>> ClinicListAsync(Guid clinicId, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        var check = scope.Check(Permissions.FinanceSettlementView, clinicId);
        if (!check.IsSuccess && !(scope.CheckPlatform(PlatformPermissions.FinanceSettle).IsSuccess && await db.Clinics.AnyAsync(c => c.Id == clinicId, ct)))
        {
            return check.Error;
        }

        return await ToDtosAsync(db.Settlements.AsNoTracking().Where(s => s.ClinicId == clinicId), ct);
    }

    public async Task<Result<SettlementDto>> CreateAsync(CreateSettlementCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var check = await financeAccess.CheckPlatformAsync(ct);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var errors = new FieldErrors();
        if (command.ClinicId is null)
        {
            errors.Add("clinicId", "Required.");
        }

        if (command.Year is not (>= 2020 and <= 2100))
        {
            errors.Add("year", "Must be between 2020 and 2100.");
        }

        if (command.Month is not (>= 1 and <= 12))
        {
            errors.Add("month", "Must be between 1 and 12.");
        }

        var terms = errors.Text(command.PaymentTerms, "paymentTerms", Settlement.MaxPaymentTermsLength, required: false);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var clinicId = command.ClinicId!.Value;
        var (year, month) = (command.Year!.Value, command.Month!.Value);
        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct))
        {
            return CommonErrors.NotFound;
        }

        var start = new DateOnly(year, month, 1);
        try
        {
            return await transactions.RunAsync<SettlementDto>(async t =>
            {
                await locks.LockAsync<Clinic>(clinicId, t);
                await periods.LockExclusiveAsync(clinicId, t);
                var now = await clock.NowAsync(t);
                if (FinanceTime.MonthRange(year, month).End > now)
                {
                    return Error.InvalidField("month", "The month has not ended yet (Asia/Tashkent).");
                }

                if (await db.Settlements.AnyAsync(s => s.ClinicId == clinicId && s.PeriodStart == start && s.Status != SettlementStatus.Voided, t))
                {
                    return FinanceErrors.SettlementExists;
                }

                var settlement = Settlement.CreateDraft(clinicId, year, month, Money.Zero(Currency.UZS), terms, currentUser.RequiredUserId, now);
                db.Settlements.Add(settlement);
                var count = await CollectAsync(settlement, t);
                audit.Record(new AuditEvent("finance.settlement.created", clinicId, TargetType: "settlement", TargetId: settlement.Id.ToString(),
                    Details: new { year, month, amountDue = settlement.AmountDueMoney.ToAmountString(), items = count }));
                await db.SaveChangesAsync(t);
                return await GetDtoAsync(settlement.Id, t);
            }, ct);
        }
        catch (Exception ex) when (dbErrors.Classify(ex) is { Kind: DbErrorKind.UniqueViolation, ConstraintName: ActivePeriodIndex or LedgerLineUniqueIndex })
        {
            return FinanceErrors.SettlementExists;
        }
    }

    /// <summary>Draft → Finalized: adds commission lines of the month posted since the draft, then locks the month.</summary>
    public Task<Result<SettlementDto>> FinalizeAsync(Guid settlementId, CancellationToken ct) =>
        TransitionAsync(settlementId, exclusivePeriodLock: true, async (s, now, t) =>
        {
            if (s.Status != SettlementStatus.Draft)
            {
                return FinanceErrors.InvalidSettlementState;
            }

            var added = await CollectAsync(s, t);
            s.Finalize(currentUser.RequiredUserId, now);
            audit.Record(new AuditEvent("finance.settlement.finalized", s.ClinicId, TargetType: "settlement", TargetId: s.Id.ToString(),
                Details: new { amountDue = s.AmountDueMoney.ToAmountString(), lateItems = added }));
            return Result.Success;
        }, ct);

    /// <summary>Finalized → Paid; posts the clinic's payment (Dr SettlementClearing / Cr PlatformCommissionReceivable).</summary>
    public Task<Result<SettlementDto>> MarkPaidAsync(Guid settlementId, MarkSettlementPaidCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new FieldErrors();
        var reference = errors.Text(command.Reference, "reference", Settlement.MaxReferenceLength, required: true);
        if (command.PaidAt is null)
        {
            errors.Add("paidAt", "Required.");
        }

        if (errors.HasErrors)
        {
            return Task.FromResult<Result<SettlementDto>>(errors.ToError());
        }

        var paidAt = command.PaidAt!.Value.ToUniversalTime();
        return TransitionAsync(settlementId, exclusivePeriodLock: false, async (s, now, t) =>
        {
            if (s.Status != SettlementStatus.Finalized)
            {
                return FinanceErrors.InvalidSettlementState;
            }

            if (paidAt > now.AddDays(1))
            {
                return Error.InvalidField("paidAt", "Must not be in the future.");
            }

            s.MarkPaid(paidAt, reference!, currentUser.RequiredUserId, now);
            await db.SaveChangesAsync(t);
            await poster.PostSettlementPaidAsync(s, currentUser.RequiredUserId, t);
            audit.Record(new AuditEvent("finance.settlement.paid", s.ClinicId, TargetType: "settlement", TargetId: s.Id.ToString(),
                Details: new { paidAt, amountDue = s.AmountDueMoney.ToAmountString() }));
            return Result.Success;
        }, ct);
    }

    /// <summary>Draft → Voided: the items are released (deleted) so the lines can be settled again. Finalized/Paid → 409 <c>finance.period_locked</c>.</summary>
    public Task<Result<SettlementDto>> VoidAsync(Guid settlementId, SettlementReasonCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new FieldErrors();
        var reason = errors.Text(command.Reason, "reason", Settlement.MaxReasonLength, required: true);
        if (errors.HasErrors)
        {
            return Task.FromResult<Result<SettlementDto>>(errors.ToError());
        }

        return TransitionAsync(settlementId, exclusivePeriodLock: false, async (s, now, t) =>
        {
            if (s.LocksPeriod)
            {
                return FinanceErrors.PeriodLocked;
            }

            if (s.Status != SettlementStatus.Draft)
            {
                return FinanceErrors.InvalidSettlementState;
            }

            var released = await db.SettlementItems.Where(i => i.SettlementId == s.Id).ExecuteDeleteAsync(t);
            s.Void(reason!, currentUser.RequiredUserId, now);
            audit.Record(new AuditEvent("finance.settlement.voided", s.ClinicId, TargetType: "settlement", TargetId: s.Id.ToString(), Details: new { releasedItems = released }));
            return Result.Success;
        }, ct);
    }

    private async Task<Result<SettlementDto>> TransitionAsync(
        Guid settlementId,
        bool exclusivePeriodLock,
        Func<Settlement, DateTimeOffset, CancellationToken, Task<Result>> action,
        CancellationToken ct)
    {
        var check = await financeAccess.CheckPlatformAsync(ct);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var clinicId = await db.Settlements.AsNoTracking().Where(s => s.Id == settlementId).Select(s => (Guid?)s.ClinicId).FirstOrDefaultAsync(ct);
        if (clinicId is not { } clinic)
        {
            return CommonErrors.NotFound;
        }

        return await transactions.RunAsync<SettlementDto>(async t =>
        {
            await locks.LockAsync<Clinic>(clinic, t);
            if (exclusivePeriodLock)
            {
                await periods.LockExclusiveAsync(clinic, t);
            }

            var settlement = (await locks.LoadForUpdateAsync<Settlement>(settlementId, t))!;
            var now = await clock.NowAsync(t);
            var result = await action(settlement, now, t);
            if (!result.IsSuccess)
            {
                return result.Error;
            }

            await db.SaveChangesAsync(t);
            return await GetDtoAsync(settlement.Id, t);
        }, ct);
    }

    /// <summary>Adds every unsettled commission receivable line of the settlement's month; returns the number added.</summary>
    private async Task<int> CollectAsync(Settlement settlement, CancellationToken ct)
    {
        var (clinicId, start, end) = (settlement.ClinicId, settlement.PeriodStart, settlement.PeriodEnd);
        var lines = await (
                from l in db.LedgerLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                where e.ClinicId == clinicId && l.Account == LedgerAccount.PlatformCommissionReceivable
                      && (e.EntryType == JournalEntryType.Commission || e.EntryType == JournalEntryType.CommissionReversal)
                      && e.PostingDateLocal >= start && e.PostingDateLocal < end
                      && !db.SettlementItems.Any(i => i.LedgerLineId == l.Id)
                orderby e.Sequence, l.Id
                select new { l.Id, l.Amount, l.Currency })
            .ToListAsync(ct);
        foreach (var line in lines)
        {
            var amount = new Money(line.Amount, line.Currency);
            db.SettlementItems.Add(SettlementItem.Create(settlement, line.Id, amount));
            settlement.AddToAmount(amount);
        }

        return lines.Count;
    }

    private async Task<SettlementDto> GetDtoAsync(Guid id, CancellationToken ct) =>
        (await ToDtosAsync(db.Settlements.AsNoTracking().Where(s => s.Id == id), ct)).Single();

    private async Task<List<SettlementDto>> ToDtosAsync(IQueryable<Settlement> query, CancellationToken ct)
    {
        var rows = await (
                from s in query
                join c in db.Clinics.AsNoTracking() on s.ClinicId equals c.Id
                orderby s.PeriodStart descending, s.CreatedAt descending
                select new { Settlement = s, ClinicName = c.Name, Items = db.SettlementItems.Count(i => i.SettlementId == s.Id) })
            .Take(MaxListed)
            .ToListAsync(ct);
        var clinicIds = rows.Select(r => r.Settlement.ClinicId).Distinct().ToList();
        var rules = await db.CommissionRules.AsNoTracking().Where(r => clinicIds.Contains(r.ClinicId))
            .Select(r => new { r.ClinicId, r.EffectiveFrom, r.EffectiveTo }).ToListAsync(ct);
        return rows.Select(r =>
        {
            var s = r.Settlement;
            var (from, to) = FinanceTime.MonthRange(s.Year, s.Month);
            var configured = rules.Any(x => x.ClinicId == s.ClinicId && x.EffectiveFrom < to && (x.EffectiveTo == null || x.EffectiveTo > from));
            return new SettlementDto(s.Id, s.ClinicId, r.ClinicName, s.Year, s.Month, s.Status, s.AmountDueMoney, s.CreatedAt, s.FinalizedAt, s.PaidAt, s.PaidReference,
                s.VoidReason, LocalFormats.Date(s.PeriodStart), LocalFormats.Date(s.PeriodEnd), r.Items, configured, s.PaymentTerms);
        }).ToList();
    }
}
