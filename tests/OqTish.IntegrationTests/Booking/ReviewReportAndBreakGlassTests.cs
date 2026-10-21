using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Public;
using OqTish.Application.Reviews;
using OqTish.Application.Staff;
using OqTish.Domain.Booking;
using OqTish.Domain.Reviews;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Booking;

/// <summary>Reviews (eligibility, moderation, rating recompute), abuse reports, and the break-glass tenant read.</summary>
public class ReviewReportAndBreakGlassTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<AppointmentDto> CompletedAsync(BookingScenario s, SignedInClient patient, int day)
    {
        var appt = await s.ConfirmedAsync(patient, s.B1, s.At(10), s.D1);
        await BookingCalls.ShiftAsync(s.Factory.ConnectionString, appt.Id, s.At(10 + day, days: -5));
        return await (await s.Owner.Client.PostAsync($"{s.A}/appointments/{appt.Id}/complete", new { chargedAmount = new { amount = "150000.00", currency = "UZS" } }))
            .ReadAsync<AppointmentDto>();
    }

    [Fact]
    public async Task Review_eligibility_moderation_and_clinic_rating()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        (await s.P1.Client.PutAsync("/api/me/profile", new { fullName = "Aziza Karimova" })).AssertStatus(HttpStatusCode.OK);
        var pending = await s.BookAsync(s.P1, s.B1, s.At(16));
        await AssertProblemAsync(await s.P1.Client.PostAsync($"/api/me/appointments/{pending.Id}/review", new { rating = 5, text = "a" }), HttpStatusCode.UnprocessableEntity, "review.not_eligible");

        var done = await CompletedAsync(s, s.P1, 0);
        var reviewUrl = $"/api/me/appointments/{done.Id}/review";
        Assert.True((await s.P1.Client.GetOkAsync<AppointmentDto>($"/api/me/appointments/{done.Id}")).CanReview);
        await AssertProblemAsync(await s.P2.Client.PostAsync(reviewUrl, new { rating = 5, text = "x" }), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.P1.Client.PostAsync(reviewUrl, new { rating = 0, text = "x" }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await s.P1.Client.PostAsync(reviewUrl, new { rating = 4, text = new string('a', 1001) }), HttpStatusCode.BadRequest, "common.validation");
        var review = await (await s.P1.Client.PostAsync(reviewUrl, new { rating = 4, text = "  Yaxshi xizmat  " })).ReadAsync<MyReviewDto>(HttpStatusCode.Created);
        Assert.Equal((4, "Yaxshi xizmat", ReviewModerationStatus.Pending), (review.Rating, review.Text, review.Status));
        await AssertProblemAsync(await s.P1.Client.PostAsync(reviewUrl, new { rating = 5, text = "again" }), HttpStatusCode.Conflict, "review.already_exists");
        var detail = await s.P1.Client.GetOkAsync<AppointmentDto>($"/api/me/appointments/{done.Id}");
        Assert.Equal((true, false), (detail.HasReview, detail.CanReview));
        Assert.True((await s.Owner.Client.GetOkAsync<AppointmentDto>($"{s.A}/appointments/{done.Id}")).HasReview);

        // Review window: 30 days after completion (database time).
        var old = await CompletedAsync(s, s.P2, 1);
        await using (var c = new NpgsqlConnection(s.Factory.ConnectionString))
        {
            await c.OpenAsync(Ct);
            await using var cmd = new NpgsqlCommand($"UPDATE booking.appointments SET status_changed_at = now() - interval '31 days' WHERE id = '{old.Id}'", c);
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        await AssertProblemAsync(await s.P2.Client.PostAsync($"/api/me/appointments/{old.Id}/review", new { rating = 5, text = "" }), HttpStatusCode.UnprocessableEntity, "review.not_eligible");

        // Moderation (platform.reports.handle) and rating recompute.
        var anon = s.Factory.CreateApiClient();
        Assert.Empty((await anon.GetOkAsync<Page<ReviewDto>>($"/api/public/clinics/{s.ClinicA}/reviews")).Items);
        var auditor = await ConsoleKit.SignInAsPlatformAdminAsync(s.Factory, PlatformPermissions.AuditView);
        await AssertProblemAsync(await auditor.Client.GetAsync("/api/admin/reviews", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await auditor.Client.PostAsync($"/api/admin/reviews/{review.Id}/publish", null, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Owner.Client.PostAsync($"/api/admin/reviews/{review.Id}/publish", null, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        var moderator = await ConsoleKit.SignInAsPlatformAdminAsync(s.Factory, PlatformPermissions.ReportsHandle);
        var queue = await moderator.Client.GetOkAsync<Page<AdminReviewDto>>("/api/admin/reviews?status=Pending");
        Assert.Equal(("Aziza K.", s.ClinicA), (queue.Items.Single().AuthorName, queue.Items.Single().ClinicId));
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.Client.PostAsync($"/api/admin/reviews/{review.Id}/publish", null, Ct)).StatusCode);
        await AssertProblemAsync(await moderator.Client.PostAsync($"/api/admin/reviews/{review.Id}/publish", null, Ct), HttpStatusCode.Conflict, "admin.invalid_status_transition");
        await AssertProblemAsync(await moderator.Client.PostAsync($"/api/admin/reviews/{Guid.NewGuid()}/publish", null, Ct), HttpStatusCode.NotFound, "common.not_found");
        var published = await anon.GetOkAsync<Page<ReviewDto>>($"/api/public/clinics/{s.ClinicA}/reviews");
        Assert.Equal(("Aziza K.", 4, "Yaxshi xizmat", "Filial 1"), (published.Items.Single().AuthorName, published.Items.Single().Rating, published.Items.Single().Text, published.Items.Single().BranchName));
        var profile = await anon.GetOkAsync<ClinicDetailDto>($"/api/public/clinics/{s.ClinicA}");
        Assert.Equal((4.00m as decimal?, 1), (profile.Rating, profile.ReviewCount));
        await AssertProblemAsync(await anon.GetAsync($"/api/public/clinics/{Guid.NewGuid()}/reviews", Ct), HttpStatusCode.NotFound, "common.not_found");

        await AssertProblemAsync(await moderator.Client.PostAsync($"/api/admin/reviews/{review.Id}/hide", new { reason = "" }), HttpStatusCode.BadRequest, "common.validation");
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.Client.PostAsync($"/api/admin/reviews/{review.Id}/hide", new { reason = "Haqoratli so'zlar" })).StatusCode);
        profile = await anon.GetOkAsync<ClinicDetailDto>($"/api/public/clinics/{s.ClinicA}");
        Assert.Equal((null as decimal?, 0), (profile.Rating, profile.ReviewCount));
        Assert.Empty((await anon.GetOkAsync<Page<ReviewDto>>($"/api/public/clinics/{s.ClinicA}/reviews")).Items);
        Assert.Equal(1, await ConsoleKit.AuditCountAsync(s.Factory, "admin.review.hidden", s.ClinicA));
        Assert.Equal(1, await ConsoleKit.AuditCountAsync(s.Factory, "review.created", s.ClinicA));
    }

    [Fact]
    public async Task Abuse_reports_are_created_once_per_target_and_resolved_by_platform_admins()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        var first = await (await s.P1.Client.PostAsync("/api/reports", new { targetType = "Clinic", targetId = s.ClinicA, reason = "Telefon ishlamaydi" })).ReadAsync<ReportCreatedDto>(HttpStatusCode.Created);
        var again = await (await s.P1.Client.PostAsync("/api/reports", new { targetType = "Clinic", targetId = s.ClinicA, reason = "Yana" })).ReadAsync<ReportCreatedDto>(HttpStatusCode.Created);
        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, (await (await s.P2.Client.PostAsync("/api/reports", new { targetType = "Clinic", targetId = s.ClinicA, reason = "Boshqa" })).ReadAsync<ReportCreatedDto>(HttpStatusCode.Created)).Id);
        await AssertProblemAsync(await s.P1.Client.PostAsync("/api/reports", new { targetType = "Clinic", targetId = Guid.NewGuid(), reason = "x" }), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.P1.Client.PostAsync("/api/reports", new { targetType = "Review", targetId = Guid.NewGuid(), reason = "x" }), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.P1.Client.PostAsync("/api/reports", new { targetType = "Clinic", targetId = s.ClinicA, reason = " " }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await s.P1.Client.PostAsync("/api/reports", new { targetType = "Clinic", targetId = s.ClinicA, reason = new string('r', 501) }), HttpStatusCode.BadRequest, "common.validation");

        await AssertProblemAsync(await s.P1.Client.GetAsync("/api/admin/reports", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        var admin = await ConsoleKit.SignInAsPlatformAdminAsync(s.Factory, PlatformPermissions.ReportsHandle);
        var open = await admin.Client.GetOkAsync<Page<AbuseReportDto>>("/api/admin/reports?status=Open");
        Assert.Equal(2, open.TotalCount);
        Assert.All(open.Items, r => Assert.Equal((s.ClinicA, ReportTargetType.Clinic), (r.ClinicId, r.TargetType)));
        Assert.Equal(2, (await admin.Client.GetOkAsync<Application.Admin.AdminDashboardDto>("/api/admin/dashboard")).OpenReports);
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/reports/{first.Id}/resolve", new { resolution = "" }), HttpStatusCode.BadRequest, "common.validation");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"/api/admin/reports/{first.Id}/resolve", new { resolution = "Klinika bilan bog'lanildi" })).StatusCode);
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/reports/{first.Id}/resolve", new { resolution = "x" }), HttpStatusCode.Conflict, "admin.invalid_status_transition");
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/reports/{Guid.NewGuid()}/resolve", new { resolution = "x" }), HttpStatusCode.NotFound, "common.not_found");
        Assert.Equal(1, (await admin.Client.GetOkAsync<Page<AbuseReportDto>>("/api/admin/reports?status=Open")).TotalCount);
        Assert.Equal(1, (await admin.Client.GetOkAsync<Application.Admin.AdminDashboardDto>("/api/admin/dashboard")).OpenReports);

        // After resolution the reporter may report again (a new open report).
        Assert.NotEqual(first.Id, (await (await s.P1.Client.PostAsync("/api/reports", new { targetType = "Clinic", targetId = s.ClinicA, reason = "Yana muammo" })).ReadAsync<ReportCreatedDto>(HttpStatusCode.Created)).Id);
        Assert.Equal(1, await ConsoleKit.AuditCountAsync(s.Factory, "admin.report.resolved", s.ClinicA));
    }

    [Fact]
    public async Task Break_glass_read_requires_the_grant_and_a_percent_encoded_reason_and_is_audited()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        var appt = await s.BookAsync(s.P1, s.B1, s.At(10));
        var url = $"/api/admin/clinics/{s.ClinicA}/appointments";
        const string reason = "Bemor shikoyati bo‘yicha tekshiruv №12 (Ярослав)";
        HttpRequestMessage Get(string path, string? header)
        {
            var m = new HttpRequestMessage(HttpMethod.Get, path);
            if (header is not null)
            {
                m.Headers.Add("X-Access-Reason", header);
            }

            return m;
        }

        var other = await ConsoleKit.SignInAsPlatformAdminAsync(s.Factory, PlatformPermissions.ReportsHandle, PlatformPermissions.AuditView);
        await AssertProblemAsync(await other.Client.SendAsync(Get(url, Uri.EscapeDataString(reason)), Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Owner.Client.SendAsync(Get(url, Uri.EscapeDataString(reason)), Ct), HttpStatusCode.Forbidden, "common.forbidden");

        var admin = await ConsoleKit.SignInAsPlatformAdminAsync(s.Factory, PlatformPermissions.TenantDataRead);
        await AssertProblemAsync(await admin.Client.SendAsync(Get(url, null), Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await admin.Client.SendAsync(Get(url, "short"), Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await admin.Client.SendAsync(Get($"/api/admin/clinics/{Guid.NewGuid()}/appointments", Uri.EscapeDataString(reason)), Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.Equal(0, await ConsoleKit.AuditCountAsync(s.Factory, "platform.tenant_data.read"));

        var page = await (await admin.Client.SendAsync(Get($"{url}?branchId={s.B1}", Uri.EscapeDataString(reason)), Ct)).ReadAsync<Page<AppointmentDto>>();
        var item = Assert.Single(page.Items);
        Assert.Equal((appt.Id, "+998901234567", false), (item.Id, item.ContactPhone, item.CanCancel));
        await using var db = s.Factory.CreateDbContext();
        var audit = await db.AuditLog.SingleAsync(a => a.Action == "platform.tenant_data.read", Ct);
        Assert.Equal((admin.Me.Id, s.ClinicA as Guid?), (audit.ActorUserId!.Value, audit.ClinicId));
        Assert.Contains(reason, System.Text.Json.JsonElement.Parse(audit.Details!).GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task Concurrent_moderation_of_two_reviews_of_one_clinic_keeps_the_rating_exact()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var setup = host.CreateDbContext();
        var moderator = Domain.Identity.User.Create("Moderator", DateTimeOffset.UtcNow);
        setup.Users.Add(moderator);
        await setup.SaveChangesAsync(Ct);
        setup.PlatformAdminGrants.Add(Domain.Staff.PlatformAdminGrant.Create(moderator.Id, PlatformPermissions.ReportsHandle, null, DateTimeOffset.UtcNow));
        await setup.SaveChangesAsync(Ct);
        for (var i = 0; i < 20; i++)
        {
            await using var db = host.CreateDbContext();
            var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 2);
            var reviews = new List<Guid>();
            for (var p = 0; p < 2; p++)
            {
                var appt = (await host.BookAsync(w, w.Patients[p], w.At(10 + p))).Value;
                Assert.True((await host.ConfirmAsync(w, appt.Id, w.Doctors[0])).IsSuccess);
                await BookingCalls.ShiftAsync(host.ConnectionString, appt.Id, w.At(10 + p, days: -10));
                Assert.True((await host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>()
                    .CompleteAsync(w.ClinicId, appt.Id, new CompleteCommand(Domain.Common.Money.Uzs(1)), Ct))).IsSuccess);
                var created = await host.RunAsync(w.Patients[p], sp => sp.GetRequiredService<ReviewHandlers>().CreateAsync(appt.Id, new ReviewCommand(p == 0 ? 5 : 2, "t"), Ct));
                reviews.Add(created.Value.Id);
            }

            var results = await Race.RunAsync<Result>(host, [.. reviews.Select(r => ((Guid?)moderator.Id,
                (Func<IServiceProvider, Task<Result>>)(sp => sp.GetRequiredService<ReviewHandlers>().PublishAsync(r, Ct))))]);
            Assert.All(results, r => Assert.True(r.IsSuccess));
            var clinic = await db.Clinics.AsNoTracking().SingleAsync(c => c.Id == w.ClinicId, Ct);
            Assert.Equal((3.50m as decimal?, 2), (clinic.RatingAverage, clinic.ReviewCount));
        }
    }
}
