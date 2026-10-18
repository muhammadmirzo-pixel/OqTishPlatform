using System.Text.Json;
using OqTish.Application.Common;
using OqTish.Application.Common.Json;
using OqTish.Application.Common.Security;
using OqTish.Domain.Audit;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Infrastructure.Common;

/// <summary>Adds an <see cref="AuditLogEntry"/> to the unit of work; it is inserted by the caller's SaveChanges.</summary>
internal sealed class AuditLog(OqTishDbContext db, IRequestContext request, IKeyedHasher hasher, TimeProvider clock) : IAuditLog
{
    public void Record(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var actor = auditEvent.ActorUserIdOverride ?? request.UserId;
        db.AuditLog.Add(AuditLogEntry.Create(
            clock.GetUtcNow(),
            actor is null ? (request.CorrelationId is null ? AuditActorKind.System : AuditActorKind.Anonymous) : AuditActorKind.User,
            actor,
            auditEvent.Action,
            auditEvent.ClinicId,
            auditEvent.BranchId,
            auditEvent.TargetType,
            auditEvent.TargetId,
            request.CorrelationId,
            request.RemoteIp is { } ip ? hasher.Hash(HashPurposes.Ip, ip) : null,
            auditEvent.Details is null ? null : JsonSerializer.Serialize(auditEvent.Details, JsonDefaults.Options)));
    }
}
