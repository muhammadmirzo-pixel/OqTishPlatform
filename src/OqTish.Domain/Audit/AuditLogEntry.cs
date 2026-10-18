using OqTish.Domain.Common;

namespace OqTish.Domain.Audit;

public enum AuditActorKind
{
    User = 1,
    System = 2,
    Anonymous = 3,
}

/// <summary>
/// Append-only audit row (UPDATE/DELETE revoked from the app role and blocked by a trigger).
/// <see cref="Details"/> is scrubbed JSON: ids and codes only, never phones, names or tokens.
/// </summary>
public sealed class AuditLogEntry
{
    private AuditLogEntry()
    {
        Action = string.Empty;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public AuditActorKind ActorKind { get; private set; }

    public Guid? ActorUserId { get; private set; }

    /// <summary>Dotted action name, e.g. <c>auth.session.revoked</c>.</summary>
    public string Action { get; private set; }

    public Guid? ClinicId { get; private set; }

    public Guid? BranchId { get; private set; }

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? IpHash { get; private set; }

    public string? Details { get; private set; }

    public static AuditLogEntry Create(
        DateTimeOffset now,
        AuditActorKind actorKind,
        Guid? actorUserId,
        string action,
        Guid? clinicId,
        Guid? branchId,
        string? targetType,
        string? targetId,
        string? correlationId,
        string? ipHash,
        string? detailsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return new AuditLogEntry
        {
            Id = IdGenerator.New(),
            OccurredAt = now,
            ActorKind = actorKind,
            ActorUserId = actorUserId,
            Action = action,
            ClinicId = clinicId,
            BranchId = branchId,
            TargetType = targetType,
            TargetId = targetId,
            CorrelationId = correlationId,
            IpHash = ipHash,
            Details = detailsJson,
        };
    }
}
