using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Booking;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;

namespace OqTish.Application.Finance;

/// <summary>
/// Clinic payments (docs/API.md §4.3). Every handler loads the appointment/payment <b>within the route clinic</b>
/// (other clinic → 404) and checks the permission on <b>its</b> branch (no membership → 404, role or branch scope
/// lacking → 403):
/// <list type="bullet">
/// <item>record / fail — <c>payment.record</c> (Owner, Receptionist, FinanceManager);</item>
/// <item>refund — <c>payment.refund</c> (Owner, FinanceManager);</item>
/// <item>list / balance — <c>payment.record</c> or <c>finance.report.view</c>.</item>
/// </list>
/// Writes run in one transaction: (idempotency key row →) appointment row lock → payment rows → ledger
/// (<see cref="LedgerPoster"/>, shared period lock) → audit. Record and refund require <c>Idempotency-Key</c>: a
/// replay returns the stored response, the same key with another body → 422 <c>common.idempotency_mismatch</c>;
/// <c>finance.payments.idempotency_key</c> is also UNIQUE per clinic as a database backstop.
/// </summary>
public sealed class PaymentHandlers(
    IOqTishDb db,
    ITransactionRunner transactions,
    IIdempotencyStore idempotency,
    IRowLocks locks,
    IDbClock clock,
    IAccessService access,
    ICurrentUser currentUser,
    IAuditLog audit,
    IDbErrorClassifier dbErrors,
    LedgerPoster poster)
{
    public const string IdempotencyKeyConstraint = "ux_payments_clinic_idempotency_key";

    private static readonly string[] ReadPermissions = [Permissions.PaymentRecord, Permissions.FinanceReportView];

    public async Task<Result<PaymentDto>> RecordAsync(Guid clinicId, Guid appointmentId, RecordPaymentCommand command, string? idempotencyKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new FieldErrors();
        var externalRef = errors.Text(command.ExternalRef, "externalRef", Payment.MaxExternalRefLength, required: false);
        if (command.Method is null)
        {
            errors.Add("method", "Use one of: Cash, Card, Transfer.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            errors.Add("Idempotency-Key", "Required for payments.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        if (command.Amount is not { IsPositive: true } amount)
        {
            return FinanceErrors.AmountInvalid("amount", "The amount must be greater than zero.");
        }

        var target = await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId && a.ClinicId == clinicId)
            .Select(a => new { a.Id, a.ClinicId, a.BranchId }).FirstOrDefaultAsync(ct);
        if (target is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        var check = scope.Check(Permissions.PaymentRecord, target.ClinicId, target.BranchId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var userId = currentUser.RequiredUserId;
        var status = command.Status ?? PaymentStatus.Recorded;
        return await WithIdempotencyConflictAsync(() => transactions.RunAsync<PaymentDto>(token =>
            idempotency.RunAsync<PaymentDto>(userId, idempotencyKey!, new { op = "payment.record", clinicId, appointmentId, command }, async t =>
            {
                await locks.LockAsync<Appointment>(target.Id, t);
                var a = await db.Appointments.AsNoTracking().FirstAsync(x => x.Id == target.Id, t);
                if (a.Status is not (AppointmentStatus.Confirmed or AppointmentStatus.Completed))
                {
                    return FinanceErrors.AppointmentNotPayable;
                }

                if (amount.Currency != a.Currency)
                {
                    return FinanceErrors.AmountInvalid("amount", "Currency does not match the appointment.");
                }

                var now = await clock.NowAsync(t);
                var payment = Payment.RecordIn(a.ClinicId, a.BranchId, a.Id, amount, command.Method!.Value, status, idempotencyKey, externalRef, userId, now);
                db.Payments.Add(payment);
                if (status == PaymentStatus.Recorded)
                {
                    var completionId = await db.ServiceCompletions.AsNoTracking().Where(c => c.AppointmentId == a.Id).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(t);
                    db.PaymentAllocations.Add(PaymentAllocation.For(payment, completionId, now));
                }

                audit.Record(new AuditEvent("finance.payment.recorded", a.ClinicId, a.BranchId, "payment", payment.Id.ToString(),
                    new { appointmentId = a.Id, amount = amount.ToAmountString(), method = payment.Method.ToString(), status = status.ToString() }));
                await db.SaveChangesAsync(t);
                if (status == PaymentStatus.Recorded)
                {
                    await poster.PostPaymentAsync(payment, t);
                }

                return await GetDtoAsync(payment.Id, t);
            }, token), ct));
    }

    /// <summary>
    /// Recorded incoming payment → Failed (bounced card, or an entry mistake corrected by a new payment, F11): the
    /// payment's entries are reversed (original entries untouched). Refused when refunds exist (refund them first).
    /// </summary>
    public async Task<Result<PaymentDto>> FailAsync(Guid clinicId, Guid paymentId, FailPaymentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new FieldErrors();
        var reason = errors.Text(command.Reason, "reason", Payment.MaxReasonLength, required: true);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var target = await LoadTargetAsync(clinicId, paymentId, ct);
        if (target is null)
        {
            return CommonErrors.NotFound;
        }

        var check = (await access.GetScopeAsync(ct)).Check(Permissions.PaymentRecord, target.ClinicId, target.BranchId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var userId = currentUser.RequiredUserId;
        return await transactions.RunAsync<PaymentDto>(async t =>
        {
            await locks.LockAsync<Appointment>(target.AppointmentId, t);
            var payment = await locks.LoadForUpdateAsync<Payment>(target.Id, t);
            if (payment is null || payment.Direction != PaymentDirection.In || payment.Status != PaymentStatus.Recorded
                || await db.Payments.AnyAsync(r => r.RefundOfPaymentId == payment.Id && r.Status == PaymentStatus.Recorded, t))
            {
                return FinanceErrors.InvalidPaymentState;
            }

            var now = await clock.NowAsync(t);
            payment.MarkFailed(reason!, userId, now);
            audit.Record(new AuditEvent("finance.payment.failed", payment.ClinicId, payment.BranchId, "payment", payment.Id.ToString(),
                new { appointmentId = payment.AppointmentId, amount = payment.AmountMoney.ToAmountString() }));
            await db.SaveChangesAsync(t);
            await poster.PostPaymentFailedAsync(payment, t);
            return await GetDtoAsync(payment.Id, t);
        }, ct);
    }

    /// <summary>Partial/full refund of a recorded incoming payment, ≤ its net paid amount (F15 → 422, nothing written).</summary>
    public async Task<Result<PaymentDto>> RefundAsync(Guid clinicId, Guid paymentId, RefundCommand command, string? idempotencyKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new FieldErrors();
        var reason = errors.Text(command.Reason, "reason", Payment.MaxReasonLength, required: true);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            errors.Add("Idempotency-Key", "Required for refunds.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        if (command.Amount is not { IsPositive: true } amount)
        {
            return FinanceErrors.AmountInvalid("amount", "The amount must be greater than zero.");
        }

        var target = await LoadTargetAsync(clinicId, paymentId, ct);
        if (target is null)
        {
            return CommonErrors.NotFound;
        }

        var check = (await access.GetScopeAsync(ct)).Check(Permissions.PaymentRefund, target.ClinicId, target.BranchId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var userId = currentUser.RequiredUserId;
        return await WithIdempotencyConflictAsync(() => transactions.RunAsync<PaymentDto>(token =>
            idempotency.RunAsync<PaymentDto>(userId, idempotencyKey!, new { op = "payment.refund", clinicId, paymentId, command }, async t =>
            {
                await locks.LockAsync<Appointment>(target.AppointmentId, t);
                var original = await db.Payments.AsNoTracking().FirstAsync(p => p.Id == target.Id, t);
                if (original.Direction != PaymentDirection.In || original.Status != PaymentStatus.Recorded)
                {
                    return FinanceErrors.InvalidPaymentState;
                }

                if (amount.Currency != original.Currency)
                {
                    return FinanceErrors.AmountInvalid("amount", "Currency does not match the payment.");
                }

                var refunded = await db.Payments.AsNoTracking()
                    .Where(r => r.RefundOfPaymentId == original.Id && r.Status == PaymentStatus.Recorded)
                    .SumAsync(r => (decimal?)r.Amount, t) ?? 0m;
                if (amount.Amount > original.Amount - refunded)
                {
                    return FinanceErrors.RefundExceedsPaid;
                }

                var now = await clock.NowAsync(t);
                var refund = Payment.RecordRefund(original, amount, reason!, idempotencyKey, userId, now);
                db.Payments.Add(refund);
                audit.Record(new AuditEvent("finance.payment.refunded", original.ClinicId, original.BranchId, "payment", refund.Id.ToString(),
                    new { appointmentId = original.AppointmentId, refundOfPaymentId = original.Id, amount = amount.ToAmountString() }));
                await db.SaveChangesAsync(t);
                await poster.PostRefundAsync(refund, original, t);
                return await GetDtoAsync(refund.Id, t);
            }, token), ct));
    }

    /// <summary>
    /// <c>GET …/payments?branchId&amp;appointmentId&amp;from&amp;to&amp;page&amp;pageSize</c>: payments and refunds in the caller's
    /// branches (filtered in SQL), newest first. <c>from</c>/<c>to</c> = UTC [from, to) on <c>recordedAt</c>.
    /// </summary>
    public async Task<Result<Page<PaymentDto>>> ListAsync(Guid clinicId, PaymentListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = PageRequest.Create(query.Page, query.PageSize);
        if (!page.IsSuccess)
        {
            return page.Error;
        }

        if (query.From is { } f && query.To is { } to && f >= to)
        {
            return Error.InvalidField("from", "'from' must be before 'to'.");
        }

        var scope = await access.GetScopeAsync(ct);
        var membership = FinanceAccess.CheckAny(scope, clinicId, null, ReadPermissions);
        if (!membership.IsSuccess)
        {
            return membership.Error;
        }

        IReadOnlyList<Guid> branches = FinanceAccess.BranchesWithAny(scope, clinicId, ReadPermissions);
        if (query.BranchId is { } branchId)
        {
            if (!await db.Branches.AnyAsync(b => b.Id == branchId && b.ClinicId == clinicId, ct))
            {
                return CommonErrors.NotFound;
            }

            if (!branches.Contains(branchId))
            {
                return CommonErrors.Forbidden;
            }

            branches = [branchId];
        }

        if (query.AppointmentId is { } appointmentId)
        {
            var branch = await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId && a.ClinicId == clinicId).Select(a => (Guid?)a.BranchId).FirstOrDefaultAsync(ct);
            if (branch is null)
            {
                return CommonErrors.NotFound;
            }

            if (!branches.Contains(branch.Value))
            {
                return CommonErrors.Forbidden;
            }
        }

        var ids = branches.ToList();
        var q = db.Payments.AsNoTracking().Where(p => p.ClinicId == clinicId && ids.Contains(p.BranchId));
        if (query.AppointmentId is { } aid)
        {
            q = q.Where(p => p.AppointmentId == aid);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            q = q.Where(p => p.RecordedAt >= fromUtc);
        }

        if (query.To is { } toValue)
        {
            var toUtc = toValue.ToUniversalTime();
            q = q.Where(p => p.RecordedAt < toUtc);
        }

        var rows = await Project(q.OrderByDescending(p => p.RecordedAt).ThenByDescending(p => p.Id)).ToPageAsync(page.Value, ct);
        return new Page<PaymentDto>([.. rows.Items.Select(ToDto)], rows.PageNumber, rows.PageSize, rows.TotalCount);
    }

    /// <summary><c>GET …/appointments/{id}/balance</c> — see <see cref="BalanceDto"/>.</summary>
    public async Task<Result<BalanceDto>> BalanceAsync(Guid clinicId, Guid appointmentId, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().Where(x => x.Id == appointmentId && x.ClinicId == clinicId)
            .Select(x => new { x.Id, x.ClinicId, x.BranchId, x.Currency }).FirstOrDefaultAsync(ct);
        if (a is null)
        {
            return CommonErrors.NotFound;
        }

        var check = FinanceAccess.CheckAny(await access.GetScopeAsync(ct), a.ClinicId, a.BranchId, ReadPermissions);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var charged = await db.ServiceCompletions.AsNoTracking().Where(c => c.AppointmentId == a.Id).Select(c => (decimal?)c.ChargedAmount).FirstOrDefaultAsync(ct) ?? 0m;
        var payments = await db.Payments.AsNoTracking().Where(p => p.AppointmentId == a.Id && p.Status == PaymentStatus.Recorded)
            .GroupBy(p => p.Direction).Select(g => new { g.Key, Sum = g.Sum(p => p.Amount) }).ToListAsync(ct);
        var receivable = await (
                from l in db.LedgerLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                where e.AppointmentId == a.Id && l.Account == LedgerAccount.PatientReceivable
                select (decimal?)l.Amount)
            .SumAsync(ct) ?? 0m;
        Money M(decimal v) => new(v, a.Currency);
        return new BalanceDto(
            M(charged),
            M(payments.FirstOrDefault(p => p.Key == PaymentDirection.In)?.Sum ?? 0m),
            M(payments.FirstOrDefault(p => p.Key == PaymentDirection.Refund)?.Sum ?? 0m),
            M(receivable));
    }

    private sealed record PaymentTarget(Guid Id, Guid ClinicId, Guid BranchId, Guid AppointmentId);

    private sealed record PaymentRow(Payment Payment, string? ContactName, string? ServiceName, decimal Refunded);

    private Task<PaymentTarget?> LoadTargetAsync(Guid clinicId, Guid paymentId, CancellationToken ct) =>
        db.Payments.AsNoTracking().Where(p => p.Id == paymentId && p.ClinicId == clinicId)
            .Select(p => new PaymentTarget(p.Id, p.ClinicId, p.BranchId, p.AppointmentId)).FirstOrDefaultAsync(ct);

    private IQueryable<PaymentRow> Project(IQueryable<Payment> payments) =>
        from p in payments
        join a in db.Appointments.AsNoTracking() on p.AppointmentId equals a.Id
        join s in db.Services.AsNoTracking() on a.ServiceId equals s.Id
        select new PaymentRow(p, a.ContactName, s.Name,
            db.Payments.Where(r => r.RefundOfPaymentId == p.Id && r.Status == PaymentStatus.Recorded).Sum(r => (decimal?)r.Amount) ?? 0m);

    private async Task<PaymentDto> GetDtoAsync(Guid paymentId, CancellationToken ct) =>
        ToDto(await Project(db.Payments.AsNoTracking().Where(p => p.Id == paymentId)).FirstAsync(ct));

    private static PaymentDto ToDto(PaymentRow row)
    {
        var p = row.Payment;
        var refunded = new Money(row.Refunded, p.Currency);
        var refundable = p.Direction == PaymentDirection.In && p.Status == PaymentStatus.Recorded ? p.AmountMoney - refunded : Money.Zero(p.Currency);
        return new PaymentDto(p.Id, p.AppointmentId, p.BranchId, p.Direction, p.Method, p.AmountMoney, p.Status, p.ExternalRef, p.RefundOfPaymentId, p.RecordedAt,
            row.ContactName, row.ServiceName, refunded, refundable, p.Reason, p.FailureReason, p.FailedAt);
    }

    /// <summary>The per-clinic UNIQUE idempotency key used by another user (23505) → 422 <c>common.idempotency_mismatch</c>.</summary>
    private async Task<Result<PaymentDto>> WithIdempotencyConflictAsync(Func<Task<Result<PaymentDto>>> work)
    {
        try
        {
            return await work();
        }
        catch (Exception ex) when (dbErrors.Classify(ex) is { Kind: DbErrorKind.UniqueViolation, ConstraintName: IdempotencyKeyConstraint })
        {
            return CommonErrors.IdempotencyMismatch;
        }
    }
}
