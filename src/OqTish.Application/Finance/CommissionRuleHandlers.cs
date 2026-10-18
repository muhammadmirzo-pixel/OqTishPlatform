using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;

namespace OqTish.Application.Finance;

/// <summary>
/// Commission contracts (docs/API.md §5.4), <c>platform.finance.settle</c> only. A rule is never edited: create,
/// then (optionally) end. Ranges of the same clinic and basis never overlap (handler check under an advisory
/// lock + the GiST exclusion constraint → 409 <c>finance.rule_overlap</c>). A rule affects only postings made
/// after it exists (ledger lines are immutable); ending it in the past is refused so that lines already posted
/// with it stay inside its range. Audit: <c>finance.commission_rule.created</c> / <c>.ended</c>.
/// </summary>
public sealed class CommissionRuleHandlers(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IDbClock clock,
    FinanceAccess financeAccess,
    ICurrentUser currentUser,
    IAuditLog audit,
    IDbErrorClassifier dbErrors)
{
    public const string NoOverlapConstraint = "ex_commission_rules_no_overlap";

    public async Task<Result<IReadOnlyList<CommissionRuleDto>>> ListAsync(Guid clinicId, CancellationToken ct)
    {
        var check = await financeAccess.CheckPlatformAsync(ct);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct))
        {
            return CommonErrors.NotFound;
        }

        var rules = await db.CommissionRules.AsNoTracking().Where(r => r.ClinicId == clinicId)
            .OrderBy(r => r.Basis).ThenBy(r => r.EffectiveFrom).ToListAsync(ct);
        return rules.Select(ToDto).ToList();
    }

    public async Task<Result<CommissionRuleDto>> CreateAsync(Guid clinicId, CreateCommissionRuleCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var check = await financeAccess.CheckPlatformAsync(ct);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var errors = new FieldErrors();
        var reference = errors.Text(command.ContractReference, "contractReference", CommissionRule.MaxContractReferenceLength, required: true);
        decimal? rate = null;
        Money? fee = null;
        if (command.Basis is not { } basis || !Enum.IsDefined(basis))
        {
            errors.Add("basis", "Use one of: CollectedPayment, CompletedService, ConfirmedAppointment, NewPatient.");
            basis = default;
        }
        else if (LedgerAccounts.IsRateBasis(basis))
        {
            if (!FinanceTime.TryParseRate(command.Rate, out var parsed) || parsed <= 0m || parsed > 1m || decimal.Round(parsed, CommissionRule.RateScale) != parsed)
            {
                errors.Add("rate", "A fraction string in (0, 1] with at most 4 decimals, e.g. \"0.0750\" for 7.5 %.");
            }
            else
            {
                rate = parsed;
            }

            if (command.FixedFee is not null)
            {
                errors.Add("fixedFee", "Not allowed for a percentage basis.");
            }
        }
        else
        {
            if (command.FixedFee is not { IsPositive: true } f)
            {
                errors.Add("fixedFee", "Required: a positive amount.");
            }
            else
            {
                fee = f;
            }

            if (command.Rate is not null)
            {
                errors.Add("rate", "Not allowed for a fixed-fee basis.");
            }
        }

        if (command.EffectiveFrom is null)
        {
            errors.Add("effectiveFrom", "Required.");
        }
        else if (command.EffectiveTo is { } end && end <= command.EffectiveFrom)
        {
            errors.Add("effectiveTo", "Must be after effectiveFrom.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct))
        {
            return CommonErrors.NotFound;
        }

        var from = command.EffectiveFrom!.Value.ToUniversalTime();
        var to = command.EffectiveTo?.ToUniversalTime();
        try
        {
            return await transactions.RunAsync<CommissionRuleDto>(async t =>
            {
                await locks.AdvisoryLockAsync($"finance-rules:{clinicId}", t);
                var overlaps = await db.CommissionRules.AsNoTracking().AnyAsync(r => r.ClinicId == clinicId && r.Basis == basis
                    && (to == null || r.EffectiveFrom < to) && (r.EffectiveTo == null || r.EffectiveTo > from), t);
                if (overlaps)
                {
                    return FinanceErrors.RuleOverlap;
                }

                var rule = CommissionRule.Create(clinicId, basis, rate, fee, from, to, reference!, currentUser.RequiredUserId, await clock.NowAsync(t));
                db.CommissionRules.Add(rule);
                audit.Record(new AuditEvent("finance.commission_rule.created", clinicId, TargetType: "commission_rule", TargetId: rule.Id.ToString(),
                    Details: new { basis = basis.ToString(), rate = rate is { } r ? FinanceTime.FormatRate(r) : null, fixedFee = fee?.ToAmountString(), effectiveFrom = from, effectiveTo = to }));
                await db.SaveChangesAsync(t);
                return ToDto(rule);
            }, ct);
        }
        catch (Exception ex) when (dbErrors.Classify(ex) is { Kind: DbErrorKind.ExclusionViolation, ConstraintName: NoOverlapConstraint })
        {
            return FinanceErrors.RuleOverlap;
        }
    }

    /// <summary>Sets/shortens <c>effectiveTo</c>: after the start, not later than the current end, not in the past (database time).</summary>
    public async Task<Result<CommissionRuleDto>> EndAsync(Guid clinicId, Guid ruleId, EndCommissionRuleCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var check = await financeAccess.CheckPlatformAsync(ct);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        if (command.EffectiveTo is not { } requested)
        {
            return Error.InvalidField("effectiveTo", "Required.");
        }

        var end = requested.ToUniversalTime();
        return await transactions.RunAsync<CommissionRuleDto>(async t =>
        {
            await locks.AdvisoryLockAsync($"finance-rules:{clinicId}", t);
            var rule = await locks.LoadForUpdateAsync<CommissionRule>(ruleId, t);
            if (rule is null || rule.ClinicId != clinicId)
            {
                return CommonErrors.NotFound;
            }

            var now = await clock.NowAsync(t);
            if (end <= rule.EffectiveFrom || (rule.EffectiveTo is { } current && end > current))
            {
                return Error.InvalidField("effectiveTo", "Must be after the rule's start and not later than its current end.");
            }

            if (end < now)
            {
                return Error.InvalidField("effectiveTo", "A rule cannot be ended in the past (postings already made with it are immutable).");
            }

            rule.End(end, currentUser.RequiredUserId, now);
            audit.Record(new AuditEvent("finance.commission_rule.ended", clinicId, TargetType: "commission_rule", TargetId: rule.Id.ToString(), Details: new { effectiveTo = end }));
            await db.SaveChangesAsync(t);
            return ToDto(rule);
        }, ct);
    }

    internal static CommissionRuleDto ToDto(CommissionRule r) =>
        new(r.Id, r.ClinicId, r.Basis, r.Rate is { } rate ? FinanceTime.FormatRate(rate) : null, r.FixedFeeMoney, r.EffectiveFrom, r.EffectiveTo, r.ContractReference,
            r.Rounding, r.ApprovedByUserId, r.CreatedAt, r.EndedAt);
}
