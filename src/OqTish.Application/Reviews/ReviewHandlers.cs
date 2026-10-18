using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Admin;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Booking;
using OqTish.Domain.Reviews;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Reviews;

public sealed record ReviewCommand(int? Rating, string? Text);

public sealed record MyReviewDto(Guid Id, Guid AppointmentId, int Rating, string Text, ReviewModerationStatus Status, DateTimeOffset CreatedAt);

/// <summary>Public review (published only).</summary>
public sealed record ReviewDto(Guid Id, int Rating, string Text, string AuthorName, DateTimeOffset CreatedAt, string BranchName);

public sealed record AdminReviewDto(
    Guid Id,
    Guid ClinicId,
    string ClinicName,
    string BranchName,
    Guid AppointmentId,
    int Rating,
    string Text,
    string AuthorName,
    ReviewModerationStatus Status,
    string? ModerationReason,
    DateTimeOffset CreatedAt);

public static class ReviewErrors
{
    public static readonly Error NotEligible = Error.Rule("review.not_eligible", "Only your own Completed appointment can be reviewed, within 30 days.");

    public static readonly Error AlreadyExists = Error.Conflict("review.already_exists", "This appointment already has a review.");
}

/// <summary>Public author label: first name + initial of the last name ("Aziza K."); "Bemor" when unknown/anonymised.</summary>
public static class ReviewAuthor
{
    public const string Unknown = "Bemor";

    public static string Display(string? fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || fullName == Domain.Booking.Appointment.AnonymisedName)
        {
            return Unknown;
        }

        return parts.Length == 1 ? parts[0] : $"{parts[0]} {char.ToUpperInvariant(parts[1][0])}.";
    }
}

/// <summary>
/// Reviews (A9, docs/API.md §2/§3/§5).
/// <list type="bullet">
/// <item>POST /me/appointments/{id}/review <c>{ rating 1..5, text ≤ 1000 }</c> — own appointment (else 404),
/// Completed and at most <c>Booking:ReviewWindowDays</c> (30) days since completion by database time (else 422
/// <c>review.not_eligible</c>), once (409 <c>review.already_exists</c>, also backed by UNIQUE(appointment_id)).
/// Starts as Pending moderation. Audit <c>review.created</c>.</item>
/// <item>GET /public/clinics/{clinicId}/reviews — approved clinic (else 404), Published only, newest first.</item>
/// <item>GET /admin/reviews?status&amp;clinicId, POST /admin/reviews/{id}/publish, /hide {reason} —
/// <c>platform.reports.handle</c>. Publish: Pending/Hidden → Published; hide: Pending/Published → Hidden;
/// same status → 409 <c>admin.invalid_status_transition</c>. Every moderation recomputes the clinic rating
/// (average and count of Published reviews) under a lock on the clinic row, via <c>Clinic.SetRating</c>.
/// Audit <c>admin.review.published</c> / <c>admin.review.hidden</c>.</item>
/// </list>
/// </summary>
public sealed class ReviewHandlers(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IAccessService access,
    ICurrentUser currentUser,
    IAuditLog audit,
    IDbClock clock,
    IDbErrorClassifier dbErrors,
    TimeProvider timeProvider,
    IOptions<BookingOptions> options)
{
    public async Task<Result<MyReviewDto>> CreateAsync(Guid appointmentId, ReviewCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = currentUser.RequiredUserId;
        var appointment = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientUserId == userId, ct);
        if (appointment is null)
        {
            return CommonErrors.NotFound;
        }

        var errors = new FieldErrors();
        if (command.Rating is not (>= 1 and <= 5))
        {
            errors.Add("rating", "Must be 1..5.");
        }

        var text = command.Text?.Trim() ?? string.Empty;
        if (text.Length > Review.MaxTextLength)
        {
            errors.Add("text", $"Must be at most {Review.MaxTextLength} characters.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var now = await clock.NowAsync(ct);
        if (appointment.Status != AppointmentStatus.Completed || now > appointment.StatusChangedAt.AddDays(options.Value.ReviewWindowDays))
        {
            return ReviewErrors.NotEligible;
        }

        if (await db.Reviews.AnyAsync(r => r.AppointmentId == appointmentId, ct))
        {
            return ReviewErrors.AlreadyExists;
        }

        try
        {
            return await transactions.RunAsync<MyReviewDto>(async token =>
            {
                var review = Review.Create(appointment.ClinicId, appointment.BranchId, appointment.Id, userId, command.Rating!.Value, text, now);
                db.Reviews.Add(review);
                audit.Record(new AuditEvent("review.created", appointment.ClinicId, appointment.BranchId, "review", review.Id.ToString(),
                    new { appointmentId, rating = review.Rating }));
                await db.SaveChangesAsync(token);
                return new MyReviewDto(review.Id, review.AppointmentId, review.Rating, review.Text, review.ModerationStatus, review.CreatedAt);
            }, ct);
        }
        catch (DbUpdateException ex) when (dbErrors.Classify(ex) is { Kind: DbErrorKind.UniqueViolation })
        {
            return ReviewErrors.AlreadyExists;
        }
    }

    public async Task<Result<Page<ReviewDto>>> PublicListAsync(Guid clinicId, int? page, int? pageSize, CancellationToken ct)
    {
        var paging = PageRequest.Create(page, pageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId && c.VerificationStatus == ClinicVerificationStatus.Approved, ct))
        {
            return CommonErrors.NotFound;
        }

        var rows = await (
                from r in db.Reviews.AsNoTracking()
                join b in db.Branches.AsNoTracking() on r.BranchId equals b.Id
                where r.ClinicId == clinicId && r.ModerationStatus == ReviewModerationStatus.Published
                orderby r.CreatedAt descending, r.Id descending
                select new { r.Id, r.Rating, r.Text, r.CreatedAt, BranchName = b.Name, Author = db.PatientProfiles.Where(p => p.UserId == r.PatientUserId).Select(p => p.FullName).FirstOrDefault() })
            .ToPageAsync(paging.Value, ct);
        return new Page<ReviewDto>([.. rows.Items.Select(r => new ReviewDto(r.Id, r.Rating, r.Text, ReviewAuthor.Display(r.Author), r.CreatedAt, r.BranchName))],
            rows.PageNumber, rows.PageSize, rows.TotalCount);
    }

    public async Task<Result<Page<AdminReviewDto>>> AdminListAsync(ReviewModerationStatus? status, Guid? clinicId, int? page, int? pageSize, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.ReportsHandle);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var paging = PageRequest.Create(page, pageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        var q = db.Reviews.AsNoTracking().AsQueryable();
        if (status is { } s)
        {
            q = q.Where(r => r.ModerationStatus == s);
        }

        if (clinicId is { } cid)
        {
            q = q.Where(r => r.ClinicId == cid);
        }

        var rows = await (
                from r in q
                join c in db.Clinics.AsNoTracking() on r.ClinicId equals c.Id
                join b in db.Branches.AsNoTracking() on r.BranchId equals b.Id
                orderby r.ModerationStatus == ReviewModerationStatus.Pending descending, r.CreatedAt descending, r.Id descending
                select new
                {
                    r.Id, r.ClinicId, ClinicName = c.Name, BranchName = b.Name, r.AppointmentId, r.Rating, r.Text, r.ModerationStatus, r.ModerationReason, r.CreatedAt,
                    Author = db.PatientProfiles.Where(p => p.UserId == r.PatientUserId).Select(p => p.FullName).FirstOrDefault(),
                })
            .ToPageAsync(paging.Value, ct);
        return new Page<AdminReviewDto>(
            [.. rows.Items.Select(r => new AdminReviewDto(r.Id, r.ClinicId, r.ClinicName, r.BranchName, r.AppointmentId, r.Rating, r.Text,
                ReviewAuthor.Display(r.Author), r.ModerationStatus, r.ModerationReason, r.CreatedAt))],
            rows.PageNumber, rows.PageSize, rows.TotalCount);
    }

    public Task<Result> PublishAsync(Guid reviewId, CancellationToken ct) => ModerateAsync(reviewId, null, ct);

    public Task<Result> HideAsync(Guid reviewId, AdminReasonCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > Review.MaxModerationReasonLength)
        {
            return Task.FromResult<Result>(Error.InvalidField("reason", $"Required, at most {Review.MaxModerationReasonLength} characters."));
        }

        return ModerateAsync(reviewId, reason, ct);
    }

    /// <summary>Recomputes and stores the Published-review rating of the clinic (caller holds the clinic row lock).</summary>
    internal async Task RecomputeRatingAsync(Guid clinicId, CancellationToken ct)
    {
        var stats = await db.Reviews.Where(r => r.ClinicId == clinicId && r.ModerationStatus == ReviewModerationStatus.Published)
            .GroupBy(r => r.ClinicId)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (decimal)r.Rating) })
            .FirstOrDefaultAsync(ct);
        var clinic = await db.Clinics.FirstAsync(c => c.Id == clinicId, ct);
        clinic.SetRating(stats?.Average, stats?.Count ?? 0, timeProvider.GetUtcNow());
    }

    private async Task<Result> ModerateAsync(Guid reviewId, string? hideReason, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.ReportsHandle);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var adminId = currentUser.RequiredUserId;
        return await transactions.RunAsync(async token =>
        {
            var review = await locks.LoadForUpdateAsync<Review>(reviewId, token);
            if (review is null)
            {
                return CommonErrors.NotFound;
            }

            var now = timeProvider.GetUtcNow();
            var changed = hideReason is null ? review.Publish(adminId, now) : review.Hide(hideReason, adminId, now);
            if (!changed)
            {
                return AdminErrors.InvalidStatusTransition;
            }

            await db.SaveChangesAsync(token);

            // Serialise rating maintenance per clinic: a concurrent moderation waits here and then aggregates
            // over the committed state (READ COMMITTED re-reads per statement).
            await locks.LockAsync<Clinic>(review.ClinicId, token);
            await RecomputeRatingAsync(review.ClinicId, token);
            audit.Record(new AuditEvent(hideReason is null ? "admin.review.published" : "admin.review.hidden", review.ClinicId, review.BranchId, "review", review.Id.ToString(),
                hideReason is null ? null : new { reason = hideReason }));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }
}
