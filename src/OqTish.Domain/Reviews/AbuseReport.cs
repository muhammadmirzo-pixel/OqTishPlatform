using OqTish.Domain.Common;

namespace OqTish.Domain.Reviews;

public enum ReportTargetType
{
    Clinic = 1,
    Review = 2,
}

public enum ReportStatus
{
    Open = 1,
    Resolved = 2,
}

/// <summary>
/// An abuse report about a clinic or a review (OD-24), resolved by platform admins. <see cref="ClinicId"/>
/// is the clinic the target belongs to (for filtering). One open report per (reporter, target).
/// </summary>
public sealed class AbuseReport
{
    public const int MaxReasonLength = 500;
    public const int MaxResolutionLength = 1000;

    private AbuseReport()
    {
        Reason = string.Empty;
    }

    public Guid Id { get; private set; }

    public ReportTargetType TargetType { get; private set; }

    public Guid TargetId { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid ReporterUserId { get; private set; }

    public string Reason { get; private set; }

    public ReportStatus Status { get; private set; }

    public string? Resolution { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AbuseReport Create(ReportTargetType targetType, Guid targetId, Guid clinicId, Guid reporterUserId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var trimmed = reason.Trim();
        if (trimmed.Length > MaxReasonLength)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "Reason is too long.");
        }

        return new AbuseReport
        {
            Id = IdGenerator.New(),
            TargetType = targetType,
            TargetId = targetId,
            ClinicId = clinicId,
            ReporterUserId = reporterUserId,
            Reason = trimmed,
            Status = ReportStatus.Open,
            CreatedAt = now,
        };
    }

    /// <summary>Open → Resolved. Returns false when already resolved.</summary>
    public bool Resolve(string resolution, Guid adminUserId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution);
        if (Status == ReportStatus.Resolved)
        {
            return false;
        }

        Status = ReportStatus.Resolved;
        Resolution = resolution.Trim();
        ResolvedByUserId = adminUserId;
        ResolvedAt = now;
        return true;
    }
}
