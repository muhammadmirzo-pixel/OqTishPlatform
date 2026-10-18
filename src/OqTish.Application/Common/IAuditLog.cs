namespace OqTish.Application.Common;

/// <summary>What happened, to what, in which tenant. Details must be scrubbed (ids/codes only).</summary>
public sealed record AuditEvent(
    string Action,
    Guid? ClinicId = null,
    Guid? BranchId = null,
    string? TargetType = null,
    string? TargetId = null,
    object? Details = null,
    Guid? ActorUserIdOverride = null);

/// <summary>
/// Appends to <c>audit.audit_log</c>. The row is added to the unit of work and is written by the next
/// SaveChanges, i.e. in the same transaction as the business change. The actor, correlation id and IP
/// hash come from <see cref="IRequestContext"/>. Never put phones, names, codes or tokens in Details.
/// </summary>
public interface IAuditLog
{
    void Record(AuditEvent auditEvent);
}
