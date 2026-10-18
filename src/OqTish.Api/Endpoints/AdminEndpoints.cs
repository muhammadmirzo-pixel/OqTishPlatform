using OqTish.Api.Common;
using OqTish.Application.Admin;
using OqTish.Application.Audit;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Reviews;
using OqTish.Domain.Booking;
using OqTish.Domain.Reviews;
using OqTish.Domain.Tenancy;

namespace OqTish.Api.Endpoints;

/// <summary>/api/admin — platform admin (docs/API.md §5). Platform permission checks are in the handlers.</summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization();

        admin.MapGet("/dashboard", async (AdminHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.DashboardAsync(ct)).ToHttp(http, TypedResults.Ok))
            .WithName("AdminDashboard").WithSummary("Platform counts (any platform permission)")
            .Produces<AdminDashboardDto>().ProducesProblem(401).ProducesProblem(403);

        admin.MapGet("/clinics", async (ClinicVerificationStatus? status, string? q, int? page, int? pageSize, AdminHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ClinicsAsync(status, q, page, pageSize, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("AdminClinics").WithSummary("Clinics for review (platform.clinic.verify)")
            .Produces<Page<AdminClinicDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);

        admin.MapPost("/clinics/{clinicId:guid}/approve", async (Guid clinicId, AdminHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ApproveAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ApproveClinic").WithSummary("Pending/Rejected → Approved (platform.clinic.verify)")
            .Produces<AdminClinicDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapPost("/clinics/{clinicId:guid}/reject", async (Guid clinicId, AdminReasonCommand body, AdminHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.RejectAsync(clinicId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("RejectClinic").WithSummary("Pending → Rejected with a reason (platform.clinic.verify)")
            .Produces<AdminClinicDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapPost("/clinics/{clinicId:guid}/suspend", async (Guid clinicId, AdminReasonCommand body, AdminHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.SuspendAsync(clinicId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("SuspendClinic").WithSummary("Approved → Suspended: hidden publicly, memberships disabled (platform.account.suspend)")
            .Produces<AdminClinicDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapPost("/clinics/{clinicId:guid}/reinstate", async (Guid clinicId, AdminHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ReinstateAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ReinstateClinic").WithSummary("Suspended → Approved (platform.account.suspend)")
            .Produces<AdminClinicDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapGet("/users", async (string? q, int? page, int? pageSize, AdminHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.UsersAsync(q, page, pageSize, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("AdminUsers").WithSummary("Accounts (platform.account.suspend)")
            .Produces<Page<AdminUserDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);

        admin.MapPost("/users/{userId:guid}/suspend", async (Guid userId, AdminReasonCommand body, AdminHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.SuspendUserAsync(userId, body, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("SuspendUser").WithSummary("Suspend an account and revoke its sessions (platform.account.suspend)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        admin.MapPost("/users/{userId:guid}/reinstate", async (Guid userId, AdminHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ReinstateUserAsync(userId, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("ReinstateUser").WithSummary("Reinstate a suspended account (platform.account.suspend)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapGet("/audit", async (Guid? clinicId, string? action, int? page, int? pageSize, AuditQueries query, HttpContext http, CancellationToken ct) =>
                (await query.PlatformAsync(clinicId, action, page, pageSize, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("AdminAudit").WithSummary("Platform audit log (platform.audit.view)")
            .Produces<Page<AuditEntryDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);

        admin.MapGet("/reports", async (ReportStatus? status, int? page, int? pageSize, ReportHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.AdminListAsync(status, page, pageSize, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("AdminReports").WithSummary("Abuse reports, open first (platform.reports.handle)")
            .Produces<Page<AbuseReportDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);

        admin.MapPost("/reports/{reportId:guid}/resolve", async (Guid reportId, ResolveReportCommand body, ReportHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ResolveAsync(reportId, body, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("ResolveReport").WithSummary("Resolve an abuse report with a resolution note (platform.reports.handle)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapGet("/reviews", async (ReviewModerationStatus? status, Guid? clinicId, int? page, int? pageSize, ReviewHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.AdminListAsync(status, clinicId, page, pageSize, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("AdminReviews").WithSummary("Reviews for moderation, Pending first (platform.reports.handle)")
            .Produces<Page<AdminReviewDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);

        admin.MapPost("/reviews/{reviewId:guid}/publish", async (Guid reviewId, ReviewHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.PublishAsync(reviewId, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("PublishReview").WithSummary("Publish a review and recompute the clinic rating (platform.reports.handle)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapPost("/reviews/{reviewId:guid}/hide", async (Guid reviewId, AdminReasonCommand body, ReviewHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.HideAsync(reviewId, body, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("HideReview").WithSummary("Hide a review with a reason and recompute the clinic rating (platform.reports.handle)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapGet("/clinics/{clinicId:guid}/appointments", async (Guid clinicId, Guid? branchId, AppointmentStatus? status, DateTimeOffset? from, DateTimeOffset? to, Guid? doctorId,
                    int? page, int? pageSize, [Microsoft.AspNetCore.Mvc.FromHeader(Name = TenantDataQueries.ReasonHeader)] string? reason,
                    TenantDataQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.AppointmentsAsync(clinicId, new AppointmentListQuery(branchId, status, from, to, doctorId, page, pageSize), reason, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("BreakGlassAppointments").WithSummary("Break-glass read of a clinic's appointments; X-Access-Reason (percent-encoded) required, audited (platform.tenant-data.read)")
            .Produces<Page<AppointmentDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        return app;
    }
}
