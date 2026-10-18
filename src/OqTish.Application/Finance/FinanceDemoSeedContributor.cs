using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Common.Seeding;
using OqTish.Application.Identity;
using OqTish.Application.Tenancy;
using OqTish.Domain.Booking;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;
using OqTish.Domain.Identity;

namespace OqTish.Application.Finance;

/// <summary>
/// Development demo finance data (order 500, only with <c>Seed:DemoFinance=true</c> — appsettings.Development.json):
/// one <b>demo</b> commission rule for the first demo clinic (CollectedPayment 10 %, from 2026-01-01 Asia/Tashkent,
/// contract reference <c>DEMO-0001</c>, approved by the seed admin — fictional, not a contract term), the
/// ServiceCompletion + ledger entries of its seeded Completed appointments (through <see cref="LedgerPoster"/>, as
/// the completion hook would have done), and demo payments: the first visit paid in full in cash, the second half
/// by card (the rest stays outstanding). Idempotent: every step is skipped when its rows exist.
/// </summary>
public sealed class FinanceDemoSeedContributor(IOqTishDb db, ITransactionRunner transactions, LedgerPoster poster, TimeProvider clock, IOptions<SeedOptions> seed)
    : ISeedContributor
{
    public const string DemoContractReference = "DEMO-0001 — fiktiv demo shartnoma (haqiqiy emas, faqat Development)";

    public int Order => 500;

    public string Name => "finance-demo";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!seed.Value.DemoFinance)
        {
            return;
        }

        var result = await transactions.RunAsync(async ct =>
        {
            var clinicName = DemoData.Clinics[0].Name;
            var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Name == clinicName, ct);
            var adminEmail = seed.Value.AdminEmail.Trim().ToLowerInvariant();
            var adminId = await db.AuthIdentities.AsNoTracking().Where(i => i.Kind == AuthIdentityKind.EmailPassword && i.NormalizedIdentifier == adminEmail)
                .Select(i => (Guid?)i.UserId).FirstOrDefaultAsync(ct);
            if (clinic is null || adminId is null)
            {
                return Result.Success;
            }

            var now = clock.GetUtcNow();
            if (!await db.CommissionRules.AnyAsync(r => r.ClinicId == clinic.Id, ct))
            {
                var from = BookingTime.ToUtc(new DateOnly(2026, 1, 1), TimeSpan.Zero, BookingTime.Zone(Domain.Tenancy.Branch.DefaultTimeZone))!.Value;
                db.CommissionRules.Add(CommissionRule.Create(clinic.Id, CommissionBasis.CollectedPayment, 0.1000m, null, from, null, DemoContractReference, adminId.Value, now));
                await db.SaveChangesAsync(ct);
            }

            var staffUser = await db.ClinicMemberships.AsNoTracking().Where(m => m.ClinicId == clinic.Id).OrderBy(m => m.CreatedAt)
                .Select(m => (Guid?)m.UserId).FirstOrDefaultAsync(ct) ?? adminId.Value;
            var completed = await db.Appointments.AsNoTracking()
                .Where(a => a.ClinicId == clinic.Id && a.Status == AppointmentStatus.Completed && a.ChargedAmount != null && a.DoctorId != null)
                .OrderBy(a => a.StartsAt).ThenBy(a => a.Id)
                .ToListAsync(ct);
            foreach (var a in completed)
            {
                await poster.PostCompletionAsync(new AppointmentCompletedContext(a.Id, a.ClinicId, a.BranchId, a.ServiceId, a.DoctorId!.Value, a.PatientUserId,
                    a.ChargedMoney!.Value, a.QuotedPriceMoney, a.StartsAt, a.EndsAt, staffUser), ct);
            }

            if (await db.Payments.AnyAsync(p => p.ClinicId == clinic.Id, ct))
            {
                return Result.Success;
            }

            for (var i = 0; i < completed.Count && i < 2; i++)
            {
                var a = completed[i];
                var charged = a.ChargedMoney!.Value;
                var amount = i == 0 ? charged : charged.Multiply(0.5m).Round();
                if (!amount.IsPositive)
                {
                    continue;
                }

                var completionId = await db.ServiceCompletions.AsNoTracking().Where(c => c.AppointmentId == a.Id).Select(c => c.Id).FirstAsync(ct);
                var payment = Payment.RecordIn(a.ClinicId, a.BranchId, a.Id, amount, i == 0 ? PaymentMethod.Cash : PaymentMethod.Card, PaymentStatus.Recorded,
                    $"demo-seed:{a.Id}", null, staffUser, a.EndsAt.AddMinutes(5 + (5 * i)));
                db.Payments.Add(payment);
                db.PaymentAllocations.Add(PaymentAllocation.For(payment, completionId, now));
                await db.SaveChangesAsync(ct);
                await poster.PostPaymentAsync(payment, ct);
            }

            return Result.Success;
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Finance demo seed failed: {result.Error.Code}");
        }
    }
}
