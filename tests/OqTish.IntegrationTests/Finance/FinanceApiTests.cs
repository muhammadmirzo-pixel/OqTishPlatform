using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OqTish.Application.Finance;
using OqTish.Application.Staff;
using OqTish.Application.Tenancy;
using OqTish.Domain.Finance;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Booking;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Finance.FinanceCalls;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Finance;

/// <summary>
/// Finance over HTTP (real sign-in, real pipeline): contract shapes and positive/negative authorization for every
/// finance endpoint — Receptionist records but cannot refund or view reports, Doctor/ClinicAdmin have no finance,
/// cross-clinic → 404, out-of-branch → 403, platform endpoints need <c>platform.finance.settle</c>.
/// </summary>
public class FinanceApiTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> PostKeyAsync(HttpClient client, string url, object body, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(request, Ct);
    }

    private static object PaymentBody(string amount, string method = "Cash") => new { amount = new { amount, currency = "UZS" }, method };

    [Fact]
    public async Task Payment_endpoints_authorization_matrix_and_contract()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        var b1 = await s.ConfirmedAsync(s.P1, s.B1, s.At(10), s.D1);
        var b2 = await s.ConfirmedAsync(s.P2, s.B2, s.At(11), s.D3);
        var admin = await SignInAsStaffAsync(s.Factory, s.ClinicA, StaffRole.ClinicAdmin);
        string Pay(Guid id) => $"{s.A}/appointments/{id}/payments";

        // record: payment.record on the appointment's branch
        var recorded = await (await PostKeyAsync(s.Reception.Client, Pay(b1.Id), PaymentBody("150000.00"), "r-1")).ReadAsync<PaymentDto>(HttpStatusCode.Created);
        Assert.Equal((PaymentDirection.In, PaymentMethod.Cash, PaymentStatus.Recorded, "150000.00", "Aziza Karimova"),
            (recorded.Direction, recorded.Method, recorded.Status, recorded.Amount.ToAmountString(), recorded.ContactName));
        var replay = await (await PostKeyAsync(s.Reception.Client, Pay(b1.Id), PaymentBody("150000.00"), "r-1")).ReadAsync<PaymentDto>(HttpStatusCode.Created);
        Assert.Equal(recorded.Id, replay.Id);
        using (var raw = JsonDocument.Parse(await (await PostKeyAsync(s.Finance.Client, Pay(b1.Id), PaymentBody("10000.00", "Card"), "f-1")).Content.ReadAsStringAsync(Ct)))
        {
            Assert.Equal("10000.00", raw.RootElement.GetProperty("amount").GetProperty("amount").GetString());
            Assert.Equal("Card", raw.RootElement.GetProperty("method").GetString());
            Assert.Equal("In", raw.RootElement.GetProperty("direction").GetString());
        }

        await AssertProblemAsync(await PostKeyAsync(s.Reception.Client, Pay(b2.Id), PaymentBody("1.00"), "r-2"), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await PostKeyAsync(s.Doctor.Client, Pay(b1.Id), PaymentBody("1.00"), "d-1"), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await PostKeyAsync(admin.Client, Pay(b1.Id), PaymentBody("1.00"), "a-1"), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await PostKeyAsync(s.OwnerB.Client, Pay(b1.Id), PaymentBody("1.00"), "b-1"), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await PostKeyAsync(s.OwnerB.Client, $"/api/clinics/{s.ClinicB}/appointments/{b1.Id}/payments", PaymentBody("1.00"), "b-2"),
            HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await PostKeyAsync(s.P1.Client, Pay(b1.Id), PaymentBody("1.00"), "p-1"), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await PostKeyAsync(s.Owner.Client, Pay(b1.Id), PaymentBody("1.00"), null), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await PostKeyAsync(s.Owner.Client, Pay(b1.Id), PaymentBody("1.005"), "o-1"), HttpStatusCode.BadRequest, "finance.amount_invalid");
        await AssertProblemAsync(await PostKeyAsync(s.Owner.Client, Pay(b1.Id), PaymentBody("0.00"), "o-2"), HttpStatusCode.BadRequest, "finance.amount_invalid");
        var ownerOnB2 = await (await PostKeyAsync(s.Owner.Client, Pay(b2.Id), PaymentBody("50000.00", "Transfer"), "o-3")).ReadAsync<PaymentDto>(HttpStatusCode.Created);

        // list: payment.record or finance.report.view, filtered to the caller's branches in SQL
        var receptionList = await s.Reception.Client.GetOkAsync<OqTish.Application.Common.Page<PaymentDto>>($"{s.A}/payments");
        Assert.All(receptionList.Items, p => Assert.Equal(s.B1, p.BranchId));
        Assert.Equal(2, receptionList.TotalCount);
        Assert.Equal(3, (await s.Finance.Client.GetOkAsync<OqTish.Application.Common.Page<PaymentDto>>($"{s.A}/payments")).TotalCount);
        Assert.Single((await s.Owner.Client.GetOkAsync<OqTish.Application.Common.Page<PaymentDto>>($"{s.A}/payments?appointmentId={b2.Id}")).Items);
        await AssertProblemAsync(await s.Reception.Client.GetAsync($"{s.A}/payments?branchId={s.B2}", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Reception.Client.GetAsync($"{s.A}/payments?appointmentId={b2.Id}", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Doctor.Client.GetAsync($"{s.A}/payments", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await admin.Client.GetAsync($"{s.A}/payments", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.OwnerB.Client.GetAsync($"{s.A}/payments", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.Owner.Client.GetAsync($"{s.A}/payments?branchId={s.ClinicBBranch}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.Owner.Client.GetAsync($"{s.A}/payments?from=2026-05-02T00:00:00Z&to=2026-05-01T00:00:00Z", Ct), HttpStatusCode.BadRequest, "common.validation");

        // balance
        var balance = await s.Reception.Client.GetOkAsync<BalanceDto>($"{s.A}/appointments/{b1.Id}/balance");
        Assert.Equal(("0.00", "160000.00", "-160000.00"), (balance.Charged.ToAmountString(), balance.Paid.ToAmountString(), balance.Outstanding.ToAmountString()));
        await AssertProblemAsync(await s.Reception.Client.GetAsync($"{s.A}/appointments/{b2.Id}/balance", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Doctor.Client.GetAsync($"{s.A}/appointments/{b1.Id}/balance", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.OwnerB.Client.GetAsync($"{s.A}/appointments/{b1.Id}/balance", Ct), HttpStatusCode.NotFound, "common.not_found");

        // refund: payment.refund (Owner, FinanceManager) — Receptionist cannot
        string Refund(Guid id) => $"{s.A}/payments/{id}/refund";
        object RefundBody(string amount) => new { amount = new { amount, currency = "UZS" }, reason = "Bemor iltimosi" };
        await AssertProblemAsync(await PostKeyAsync(s.Reception.Client, Refund(recorded.Id), RefundBody("1.00"), "rr-1"), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await PostKeyAsync(admin.Client, Refund(recorded.Id), RefundBody("1.00"), "ar-1"), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await PostKeyAsync(s.OwnerB.Client, Refund(recorded.Id), RefundBody("1.00"), "br-1"), HttpStatusCode.NotFound, "common.not_found");
        var refund = await (await PostKeyAsync(s.Finance.Client, Refund(recorded.Id), RefundBody("50000.00"), "fr-1")).ReadAsync<PaymentDto>(HttpStatusCode.Created);
        Assert.Equal((PaymentDirection.Refund, recorded.Id, "Bemor iltimosi"), (refund.Direction, refund.RefundOfPaymentId, refund.Reason));
        Assert.True((await PostKeyAsync(s.Owner.Client, Refund(recorded.Id), RefundBody("50000.00"), "or-1")).StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(await PostKeyAsync(s.Owner.Client, Refund(recorded.Id), RefundBody("50000.01"), "or-2"), HttpStatusCode.UnprocessableEntity, "finance.refund_exceeds_paid");
        await AssertProblemAsync(await PostKeyAsync(s.Owner.Client, Refund(refund.Id), RefundBody("1.00"), "or-3"), HttpStatusCode.Conflict, "finance.invalid_payment_state");
        var afterRefunds = await s.Owner.Client.GetOkAsync<OqTish.Application.Common.Page<PaymentDto>>($"{s.A}/payments?appointmentId={b1.Id}&pageSize=100");
        Assert.Equal(("100000.00", "50000.00"), (afterRefunds.Items.Single(p => p.Id == recorded.Id).Refunded.ToAmountString(),
            afterRefunds.Items.Single(p => p.Id == recorded.Id).Refundable.ToAmountString()));

        // fail: payment.record; refused once refunds exist
        await AssertProblemAsync(await s.Doctor.Client.PostAsync($"{s.A}/payments/{ownerOnB2.Id}/fail", new { reason = "Karta qaytdi" }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{s.A}/payments/{ownerOnB2.Id}/fail", new { reason = "Karta qaytdi" }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Owner.Client.PostAsync($"{s.A}/payments/{ownerOnB2.Id}/fail", new { reason = "" }), HttpStatusCode.BadRequest, "common.validation");
        var failed = await (await s.Owner.Client.PostAsync($"{s.A}/payments/{ownerOnB2.Id}/fail", new { reason = "Karta qaytdi" })).ReadAsync<PaymentDto>();
        Assert.Equal(PaymentStatus.Failed, failed.Status);
        await AssertProblemAsync(await s.Owner.Client.PostAsync($"{s.A}/payments/{ownerOnB2.Id}/fail", new { reason = "Yana" }), HttpStatusCode.Conflict, "finance.invalid_payment_state");
        await AssertProblemAsync(await s.Reception.Client.PostAsync($"{s.A}/payments/{recorded.Id}/fail", new { reason = "Qaytdi" }), HttpStatusCode.Conflict, "finance.invalid_payment_state");
        await AssertProblemAsync(await s.OwnerB.Client.PostAsync($"{s.A}/payments/{recorded.Id}/fail", new { reason = "Qaytdi" }), HttpStatusCode.NotFound, "common.not_found");

        Assert.True(await ConsoleKit.AuditCountAsync(s.Factory, "finance.payment.recorded", s.ClinicA) >= 3);
        await FinanceInvariants.AssertAsync(s.Factory.ConnectionString);
    }

    [Fact]
    public async Task Reports_settlement_view_and_dashboard_authorization()
    {
        var s = await BookingScenario.CreateAsync(postgres);
        await using var _ = s.Factory;
        var admin = await SignInAsStaffAsync(s.Factory, s.ClinicA, StaffRole.ClinicAdmin);
        var branchFinance = await SignInAsStaffAsync(s.Factory, s.ClinicA, StaffRole.FinanceManager, [s.B1]);
        var platform = await ConsoleKit.SignInAsPlatformAdminAsync(s.Factory, PlatformPermissions.FinanceSettle);
        var verifier = await ConsoleKit.SignInAsPlatformAdminAsync(s.Factory, PlatformPermissions.ClinicVerify);
        var b1 = await s.ConfirmedAsync(s.P1, s.B1, s.At(10), s.D1);
        await (await PostKeyAsync(s.Owner.Client, $"{s.A}/appointments/{b1.Id}/payments", PaymentBody("70000.00"), "k-1")).ReadAsync<PaymentDto>(HttpStatusCode.Created);
        var today = Today();
        var (from, to) = (OqTish.Application.Common.LocalFormats.Date(today.AddDays(-1)), OqTish.Application.Common.LocalFormats.Date(today.AddDays(1)));
        var daily = $"{s.A}/reports/daily?from={from}&to={to}";
        var monthly = $"{s.A}/reports/monthly?year={today.Year}&month={today.Month}";

        var rows = await s.Owner.Client.GetOkAsync<List<DailyReportRowDto>>(daily);
        Assert.Equal(3, rows.Count);
        Assert.Equal("70000.00", rows.Single(r => r.Date == OqTish.Application.Common.LocalFormats.Date(today)).Collected.ToAmountString());
        Assert.Equal(3, (await s.Finance.Client.GetOkAsync<List<DailyReportRowDto>>(daily)).Count);
        Assert.Equal(3, (await branchFinance.Client.GetOkAsync<List<DailyReportRowDto>>($"{daily}&branchId={s.B1}")).Count);
        Assert.Equal(3, (await platform.Client.GetOkAsync<List<DailyReportRowDto>>(daily)).Count);
        await AssertProblemAsync(await branchFinance.Client.GetAsync($"{daily}&branchId={s.B2}", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        foreach (var denied in new[] { s.Reception, s.Doctor, admin })
        {
            await AssertProblemAsync(await denied.Client.GetAsync(daily, Ct), HttpStatusCode.Forbidden, "common.forbidden");
            await AssertProblemAsync(await denied.Client.GetAsync(monthly, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        }

        await AssertProblemAsync(await s.OwnerB.Client.GetAsync(daily, Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await verifier.Client.GetAsync(daily, Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await s.Owner.Client.GetAsync($"{s.A}/reports/daily?from={to}&to={from}", Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await s.Owner.Client.GetAsync($"{s.A}/reports/daily?from=2026-01-01&to=2026-12-31", Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await s.Owner.Client.GetAsync($"{s.A}/reports/daily", Ct), HttpStatusCode.BadRequest, "common.validation");

        var month = await s.Owner.Client.GetOkAsync<MonthlyReportDto>(monthly);
        Assert.Equal(("70000.00", false, false), (month.Collected.ToAmountString(), month.CommissionRuleConfigured, month.Locked));
        Assert.Equal(month.Collected, (await s.Finance.Client.GetOkAsync<MonthlyReportDto>(monthly)).Collected);
        await AssertProblemAsync(await s.Owner.Client.GetAsync($"{s.A}/reports/monthly?year={today.Year}&month=13", Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await s.OwnerB.Client.GetAsync(monthly, Ct), HttpStatusCode.NotFound, "common.not_found");

        // settlements view: finance.settlement.view is clinic-wide only
        var settlements = $"{s.A}/settlements";
        Assert.Empty(await s.Owner.Client.GetOkAsync<List<SettlementDto>>(settlements));
        Assert.Empty(await s.Finance.Client.GetOkAsync<List<SettlementDto>>(settlements));
        Assert.Empty(await platform.Client.GetOkAsync<List<SettlementDto>>(settlements));
        await AssertProblemAsync(await branchFinance.Client.GetAsync(settlements, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.Reception.Client.GetAsync(settlements, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await s.OwnerB.Client.GetAsync(settlements, Ct), HttpStatusCode.NotFound, "common.not_found");

        // dashboard: collected (net, today) only with finance.report.view
        Assert.Equal("70000.00", (await s.Owner.Client.GetOkAsync<DashboardDto>($"{s.A}/dashboard")).Collected!.Value.ToAmountString());
        Assert.Null((await s.Reception.Client.GetOkAsync<DashboardDto>($"{s.A}/dashboard")).Collected);
    }

    [Fact]
    public async Task Admin_commission_rules_and_settlements()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var clinic = await CreateClinicAsync(factory, branches: 1);
        var admin = await ConsoleKit.SignInAsPlatformAdminAsync(factory, PlatformPermissions.FinanceSettle);
        var noGrant = await ConsoleKit.SignInAsPlatformAdminAsync(factory, PlatformPermissions.ClinicVerify);
        var owner = await SignInAsStaffAsync(factory, clinic.ClinicId, StaffRole.ClinicOwner);
        var rules = $"/api/admin/clinics/{clinic.ClinicId}/commission-rules";
        var start = Local(2026, 1, 1, 0, 0);

        await AssertProblemAsync(await noGrant.Client.GetAsync(rules, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await owner.Client.GetAsync(rules, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await admin.Client.GetAsync($"/api/admin/clinics/{Guid.NewGuid()}/commission-rules", Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.Empty(await admin.Client.GetOkAsync<List<CommissionRuleDto>>(rules));

        var rule = await (await admin.Client.PostAsync(rules, new { basis = "CollectedPayment", rate = "0.075", effectiveFrom = start, contractReference = "SH-2026/01" }))
            .ReadAsync<CommissionRuleDto>(HttpStatusCode.Created);
        Assert.Equal(("0.0750", CommissionBasis.CollectedPayment, (DateTimeOffset?)null, admin.Me.Id), (rule.Rate, rule.Basis, rule.EffectiveTo, rule.ApprovedByUserId));
        await AssertProblemAsync(await admin.Client.PostAsync(rules, new { basis = "CollectedPayment", rate = "0.0800", effectiveFrom = start.AddDays(40), contractReference = "SH-2" }),
            HttpStatusCode.Conflict, "finance.rule_overlap");
        await AssertProblemAsync(await noGrant.Client.PostAsync(rules, new { basis = "CollectedPayment", rate = "0.0800", effectiveFrom = start, contractReference = "X" }),
            HttpStatusCode.Forbidden, "common.forbidden");
        foreach (var bad in new object[]
                 {
                     new { basis = "CollectedPayment", rate = "7.5", effectiveFrom = start, contractReference = "X" },
                     new { basis = "CollectedPayment", rate = "0.12345", effectiveFrom = start, contractReference = "X" },
                     new { basis = "CollectedPayment", rate = "-0.1", effectiveFrom = start, contractReference = "X" },
                     new { basis = "CompletedService", rate = "0.1", fixedFee = new { amount = "100.00", currency = "UZS" }, effectiveFrom = start, contractReference = "X" },
                     new { basis = "NewPatient", effectiveFrom = start, contractReference = "X" },
                     new { basis = "NewPatient", fixedFee = new { amount = "100.00", currency = "UZS" }, effectiveFrom = start, contractReference = "" },
                     new { basis = "NewPatient", fixedFee = new { amount = "100.00", currency = "UZS" }, effectiveFrom = start, effectiveTo = start.AddDays(-1), contractReference = "X" },
                 })
        {
            await AssertProblemAsync(await admin.Client.PostAsync(rules, bad), HttpStatusCode.BadRequest, "common.validation");
        }

        var fee = await (await admin.Client.PostAsync(rules, new { basis = "ConfirmedAppointment", fixedFee = new { amount = "5000.00", currency = "UZS" }, effectiveFrom = start, contractReference = "SH-3" }))
            .ReadAsync<CommissionRuleDto>(HttpStatusCode.Created);
        Assert.Equal(("5000.00", (string?)null), (fee.FixedFee!.Value.ToAmountString(), fee.Rate));

        var end = DateTimeOffset.UtcNow.AddDays(10);
        await AssertProblemAsync(await admin.Client.PostAsync($"{rules}/{rule.Id}/end", new { effectiveTo = DateTimeOffset.UtcNow.AddDays(-1) }), HttpStatusCode.BadRequest, "common.validation");
        var ended = await (await admin.Client.PostAsync($"{rules}/{rule.Id}/end", new { effectiveTo = end })).ReadAsync<CommissionRuleDto>();
        Assert.Equal(end, ended.EffectiveTo!.Value, TimeSpan.FromMilliseconds(1));
        await AssertProblemAsync(await admin.Client.PostAsync($"{rules}/{rule.Id}/end", new { effectiveTo = end.AddDays(1) }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await admin.Client.PostAsync($"{rules}/{Guid.NewGuid()}/end", new { effectiveTo = end }), HttpStatusCode.NotFound, "common.not_found");
        await (await admin.Client.PostAsync(rules, new { basis = "CollectedPayment", rate = "0.0800", effectiveFrom = ended.EffectiveTo, contractReference = "SH-4" }))
            .ReadAsync<CommissionRuleDto>(HttpStatusCode.Created); // adjacent ranges are allowed
        Assert.Equal(3, await ConsoleKit.AuditCountAsync(factory, "finance.commission_rule.created", clinic.ClinicId));

        // settlements
        var lastMonth = Today().AddMonths(-1);
        var body = new { clinicId = clinic.ClinicId, year = lastMonth.Year, month = lastMonth.Month };
        await AssertProblemAsync(await noGrant.Client.PostAsync("/api/admin/settlements", body), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await admin.Client.PostAsync("/api/admin/settlements", new { clinicId = Guid.NewGuid(), year = lastMonth.Year, month = lastMonth.Month }),
            HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await admin.Client.PostAsync("/api/admin/settlements", new { clinicId = clinic.ClinicId, year = Today().Year, month = Today().Month }),
            HttpStatusCode.BadRequest, "common.validation");
        var draft = await (await admin.Client.PostAsync("/api/admin/settlements", body)).ReadAsync<SettlementDto>(HttpStatusCode.Created);
        Assert.Equal((SettlementStatus.Draft, "0.00", lastMonth.Month, true), (draft.Status, draft.AmountDue.ToAmountString(), draft.Month, draft.CommissionRuleConfigured));
        await AssertProblemAsync(await admin.Client.PostAsync("/api/admin/settlements", body), HttpStatusCode.Conflict, "finance.settlement_exists");
        Assert.Single(await admin.Client.GetOkAsync<List<SettlementDto>>($"/api/admin/settlements?clinicId={clinic.ClinicId}"));
        Assert.Single(await admin.Client.GetOkAsync<List<SettlementDto>>($"/api/admin/settlements?clinicId={clinic.ClinicId}&status=Draft"));
        await AssertProblemAsync(await noGrant.Client.GetAsync("/api/admin/settlements", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        Assert.Single(await owner.Client.GetOkAsync<List<SettlementDto>>($"/api/clinics/{clinic.ClinicId}/settlements"));

        await AssertProblemAsync(await noGrant.Client.PostAsync($"/api/admin/settlements/{draft.Id}/finalize", (object?)null), HttpStatusCode.Forbidden, "common.forbidden");
        Assert.Equal(SettlementStatus.Finalized, (await (await admin.Client.PostAsync($"/api/admin/settlements/{draft.Id}/finalize", (object?)null)).ReadAsync<SettlementDto>()).Status);
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/settlements/{draft.Id}/void", new { reason = "Xato" }), HttpStatusCode.Conflict, "finance.period_locked");
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/settlements/{draft.Id}/mark-paid", new { reference = "PP-1" }), HttpStatusCode.BadRequest, "common.validation");
        var paid = await (await admin.Client.PostAsync($"/api/admin/settlements/{draft.Id}/mark-paid", new { paidAt = DateTimeOffset.UtcNow, reference = "PP-1" }))
            .ReadAsync<SettlementDto>();
        Assert.Equal((SettlementStatus.Paid, "PP-1"), (paid.Status, paid.PaidReference));
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/settlements/{draft.Id}/finalize", (object?)null), HttpStatusCode.Conflict, "finance.invalid_settlement_state");
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/settlements/{Guid.NewGuid()}/finalize", (object?)null), HttpStatusCode.NotFound, "common.not_found");
        Assert.True((await owner.Client.GetOkAsync<MonthlyReportDto>($"/api/clinics/{clinic.ClinicId}/reports/monthly?year={lastMonth.Year}&month={lastMonth.Month}")).Locked);
    }
}
