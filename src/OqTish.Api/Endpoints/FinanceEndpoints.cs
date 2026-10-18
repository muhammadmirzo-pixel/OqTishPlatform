using Microsoft.AspNetCore.Mvc;
using OqTish.Api.Common;
using OqTish.Application.Common;
using OqTish.Application.Finance;
using OqTish.Domain.Finance;

namespace OqTish.Api.Endpoints;

/// <summary>
/// Finance endpoints (docs/API.md §4.3 clinic console, §5.4 platform admin, BE3): payments, refunds, balance,
/// reports, settlements and commission rules. Handlers do every authorization check.
/// </summary>
public static class FinanceEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        MapClinic(app.MapGroup("/api/clinics/{clinicId:guid}").WithTags("Clinic finance").RequireAuthorization());
        MapAdmin(app.MapGroup("/api/admin").WithTags("Admin finance").RequireAuthorization());
        return app;
    }

    private static void MapClinic(RouteGroupBuilder clinic)
    {
        clinic.MapGet("/payments", async (Guid clinicId, Guid? branchId, Guid? appointmentId, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
                    PaymentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(clinicId, new PaymentListQuery(branchId, appointmentId, from, to, page, pageSize), ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ClinicPayments").WithSummary("Payments and refunds in scope, newest first (payment.record or finance.report.view); from/to = UTC [from, to) on recordedAt")
            .Produces<Page<PaymentDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapGet("/appointments/{appointmentId:guid}/balance", async (Guid clinicId, Guid appointmentId, PaymentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.BalanceAsync(clinicId, appointmentId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("AppointmentBalance").WithSummary("Charged, paid, refunded and outstanding (ledger receivable) of an appointment (payment.record or finance.report.view)")
            .Produces<BalanceDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPost("/appointments/{appointmentId:guid}/payments", async (Guid clinicId, Guid appointmentId, RecordPaymentCommand body,
                    [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, PaymentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.RecordAsync(clinicId, appointmentId, body, idempotencyKey, ct)).ToHttp(http, r => TypedResults.Created((string?)null, r)))
            .WithName("RecordPayment").WithSummary("Record a payment for a Confirmed/Completed appointment; posts ledger + commission (payment.record; Idempotency-Key required)")
            .Produces<PaymentDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        clinic.MapPost("/payments/{paymentId:guid}/fail", async (Guid clinicId, Guid paymentId, FailPaymentCommand body, PaymentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.FailAsync(clinicId, paymentId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("FailPayment").WithSummary("Mark a recorded payment Failed via reversal entries (payment.record)")
            .Produces<PaymentDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        clinic.MapPost("/payments/{paymentId:guid}/refund", async (Guid clinicId, Guid paymentId, RefundCommand body,
                    [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, PaymentHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.RefundAsync(clinicId, paymentId, body, idempotencyKey, ct)).ToHttp(http, r => TypedResults.Created((string?)null, r)))
            .WithName("RefundPayment").WithSummary("Partial/full refund ≤ net paid; proportional commission reversal (payment.refund; Idempotency-Key required)")
            .Produces<PaymentDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        clinic.MapGet("/reports/daily", async (Guid clinicId, Guid? branchId, string? from, string? to, FinanceReportQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.DailyAsync(clinicId, branchId, from, to, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("DailyFinanceReport").WithSummary("Per branch-local day (YYYY-MM-DD, inclusive): visits, charged, collected, refunded, commission — from the ledger (finance.report.view)")
            .Produces<IReadOnlyList<DailyReportRowDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapGet("/reports/monthly", async (Guid clinicId, int? year, int? month, Guid? branchId, FinanceReportQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.MonthlyAsync(clinicId, year, month, branchId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("MonthlyFinanceReport").WithSummary("Monthly totals with the reconciliation and contract flags (finance.report.view)")
            .Produces<MonthlyReportDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapGet("/settlements", async (Guid clinicId, SettlementHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ClinicListAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ClinicSettlements").WithSummary("The clinic's monthly settlements, newest first (finance.settlement.view)")
            .Produces<IReadOnlyList<SettlementDto>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }

    private static void MapAdmin(RouteGroupBuilder admin)
    {
        admin.MapGet("/clinics/{clinicId:guid}/commission-rules", async (Guid clinicId, CommissionRuleHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("CommissionRules").WithSummary("A clinic's commission rules (platform.finance.settle)")
            .Produces<IReadOnlyList<CommissionRuleDto>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        admin.MapPost("/clinics/{clinicId:guid}/commission-rules", async (Guid clinicId, CreateCommissionRuleCommand body, CommissionRuleHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateAsync(clinicId, body, ct)).ToHttp(http, r => TypedResults.Created($"/api/admin/clinics/{clinicId}/commission-rules", r)))
            .WithName("CreateCommissionRule").WithSummary("Add a commission contract term; overlapping range → 409 finance.rule_overlap (platform.finance.settle)")
            .Produces<CommissionRuleDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapPost("/clinics/{clinicId:guid}/commission-rules/{ruleId:guid}/end", async (Guid clinicId, Guid ruleId, EndCommissionRuleCommand body, CommissionRuleHandlers handler,
                    HttpContext http, CancellationToken ct) =>
                (await handler.EndAsync(clinicId, ruleId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("EndCommissionRule").WithSummary("End a rule (shorten effectiveTo; never edited otherwise) (platform.finance.settle)")
            .Produces<CommissionRuleDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        admin.MapGet("/settlements", async (Guid? clinicId, SettlementStatus? status, SettlementHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.AdminListAsync(clinicId, status, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("AdminSettlements").WithSummary("Settlements, newest period first, unpaginated (≤ 500) (platform.finance.settle)")
            .Produces<IReadOnlyList<SettlementDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);

        admin.MapPost("/settlements", async (CreateSettlementCommand body, SettlementHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateAsync(body, ct)).ToHttp(http, r => TypedResults.Created($"/api/admin/settlements/{r.Id}", r)))
            .WithName("CreateSettlement").WithSummary("Draft settlement of a clinic's ended month; duplicate → 409 finance.settlement_exists (platform.finance.settle)")
            .Produces<SettlementDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapPost("/settlements/{settlementId:guid}/finalize", async (Guid settlementId, SettlementHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.FinalizeAsync(settlementId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("FinalizeSettlement").WithSummary("Draft → Finalized; locks the month (platform.finance.settle)")
            .Produces<SettlementDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapPost("/settlements/{settlementId:guid}/mark-paid", async (Guid settlementId, MarkSettlementPaidCommand body, SettlementHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.MarkPaidAsync(settlementId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("MarkSettlementPaid").WithSummary("Finalized → Paid with paidAt and reference (platform.finance.settle)")
            .Produces<SettlementDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        admin.MapPost("/settlements/{settlementId:guid}/void", async (Guid settlementId, SettlementReasonCommand body, SettlementHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.VoidAsync(settlementId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("VoidSettlement").WithSummary("Draft → Voided, releases its items; Finalized/Paid → 409 finance.period_locked (platform.finance.settle)")
            .Produces<SettlementDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    }
}
