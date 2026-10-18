using OqTish.Domain.Common;

namespace OqTish.Domain.Reviews;

public enum ReviewModerationStatus
{
    Pending = 1,
    Published = 2,
    Hidden = 3,
}

/// <summary>
/// A patient's review of one Completed appointment (A9: one per appointment, <c>appointment_id</c> UNIQUE,
/// within 30 days, moderated). Only Published reviews are public and count towards the clinic rating.
/// </summary>
public sealed class Review
{
    public const int MaxTextLength = 1000;
    public const int MaxModerationReasonLength = 500;

    private Review()
    {
        Text = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public Guid PatientUserId { get; private set; }

    /// <summary>1..5.</summary>
    public short Rating { get; private set; }

    public string Text { get; private set; }

    public ReviewModerationStatus ModerationStatus { get; private set; }

    public string? ModerationReason { get; private set; }

    public Guid? ModeratedByUserId { get; private set; }

    public DateTimeOffset? ModeratedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Review Create(Guid clinicId, Guid branchId, Guid appointmentId, Guid patientUserId, int rating, string? text, DateTimeOffset now)
    {
        if (rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be 1..5.");
        }

        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length > MaxTextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(text), "Text is too long.");
        }

        return new Review
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            BranchId = branchId,
            AppointmentId = appointmentId,
            PatientUserId = patientUserId,
            Rating = (short)rating,
            Text = trimmed,
            ModerationStatus = ReviewModerationStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Pending/Hidden → Published. Returns false when already Published.</summary>
    public bool Publish(Guid moderatorUserId, DateTimeOffset now) => Moderate(ReviewModerationStatus.Published, null, moderatorUserId, now);

    /// <summary>Pending/Published → Hidden (reason required). Returns false when already Hidden.</summary>
    public bool Hide(string reason, Guid moderatorUserId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return Moderate(ReviewModerationStatus.Hidden, reason.Trim(), moderatorUserId, now);
    }

    private bool Moderate(ReviewModerationStatus status, string? reason, Guid moderator, DateTimeOffset now)
    {
        if (ModerationStatus == status)
        {
            return false;
        }

        ModerationStatus = status;
        ModerationReason = reason;
        ModeratedByUserId = moderator;
        ModeratedAt = now;
        UpdatedAt = now;
        return true;
    }
}
