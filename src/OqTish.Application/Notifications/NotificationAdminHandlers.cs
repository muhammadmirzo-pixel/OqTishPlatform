using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Notifications;

namespace OqTish.Application.Notifications;

/// <summary>
/// One outbox message for the platform admin. No payload or message text (personal data stays out of the admin
/// list): type, audience (<c>patient</c>/<c>staff</c>), last channel, status, attempts and the last error code.
/// <see cref="LastError"/> is the scrubbed provider description (codes/status only, never text or phones).
/// </summary>
public sealed record AdminNotificationDto(
    Guid Id,
    string Type,
    string Audience,
    string? Channel,
    OutboxStatus Status,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset NotBefore,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? SentAt,
    string? LastErrorCode,
    string? LastError);

/// <summary>
/// GET /api/admin/notifications?status&amp;page&amp;pageSize and POST /api/admin/notifications/{id}/retry —
/// <c>platform.audit.view</c> (docs/API.md §5.5). Retry: Failed → Pending with a fresh attempt budget, due now
/// (other statuses → 409 <c>admin.invalid_status_transition</c>), audited <c>admin.notification.retried</c>.
/// </summary>
public sealed class NotificationAdminHandlers(IOqTishDb db, IAccessService access, ITransactionRunner transactions, IRowLocks locks, IAuditLog audit, IDbClock clock)
{
    public static readonly Error NotRetryable = Error.Conflict("admin.invalid_status_transition", "Only Failed notifications can be retried.");

    public async Task<Result<Page<AdminNotificationDto>>> ListAsync(OutboxStatus? status, int? page, int? pageSize, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.AuditView);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var paging = PageRequest.Create(page, pageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        var query = db.OutboxMessages.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(m => m.Status == s);
        }

        var rows = await query
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => new { m.Id, m.Type, m.Channel, m.Status, m.Attempts, m.CreatedAt, m.NotBefore, m.NextAttemptAt, m.SentAt, m.LastErrorCode, m.LastError })
            .ToPageAsync(paging.Value, ct);
        return new Page<AdminNotificationDto>(
            [.. rows.Items.Select(m => new AdminNotificationDto(m.Id, m.Type, Audience(m.Type), m.Channel, m.Status, m.Attempts, m.CreatedAt, m.NotBefore,
                m.Status is OutboxStatus.Pending ? m.NextAttemptAt : null, m.SentAt, m.LastErrorCode, m.LastError))],
            rows.PageNumber, rows.PageSize, rows.TotalCount);
    }

    public async Task<Result> RetryAsync(Guid id, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.AuditView);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        return await transactions.RunAsync(async t =>
        {
            var message = await locks.LoadForUpdateAsync<OutboxMessage>(id, t);
            if (message is null)
            {
                return CommonErrors.NotFound;
            }

            if (!message.RetryManually(await clock.NowAsync(t)))
            {
                return NotRetryable;
            }

            audit.Record(new AuditEvent("admin.notification.retried", TargetType: "outbox_message", TargetId: id.ToString(), Details: new { type = message.Type }));
            await db.SaveChangesAsync(t);
            return Result.Success;
        }, ct);
    }

    public static string Audience(string type) => type.EndsWith(".staff", StringComparison.Ordinal) ? "staff" : "patient";
}
