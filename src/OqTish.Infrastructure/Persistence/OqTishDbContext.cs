using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Public;
using OqTish.Domain.Audit;
using OqTish.Domain.Booking;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;
using OqTish.Domain.Identity;
using OqTish.Domain.Notifications;
using OqTish.Domain.Reviews;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Infrastructure.Persistence;

/// <summary>
/// Single DbContext for the modular monolith (ADR-002). Each module maps its tables into its own
/// PostgreSQL schema; cross-module writes share one transaction (booking + outbox + ledger).
/// Also stores the ASP.NET Data Protection key ring (<c>identity.data_protection_keys</c>).
/// </summary>
public sealed class OqTishDbContext(DbContextOptions<OqTishDbContext> options) : DbContext(options), IOqTishDb, IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<AuthIdentity> AuthIdentities => Set<AuthIdentity>();

    public DbSet<PasswordCredential> PasswordCredentials => Set<PasswordCredential>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<TotpCredential> TotpCredentials => Set<TotpCredential>();

    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();

    public DbSet<Clinic> Clinics => Set<Clinic>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<BranchOpeningHours> BranchOpeningHours => Set<BranchOpeningHours>();

    public DbSet<BranchBookingLock> BranchBookingLocks => Set<BranchBookingLock>();

    public DbSet<ClinicMembership> ClinicMemberships => Set<ClinicMembership>();

    public DbSet<BranchAccess> BranchAccess => Set<BranchAccess>();

    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();

    public DbSet<PlatformAdminGrant> PlatformAdminGrants => Set<PlatformAdminGrant>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<BranchService> BranchServices => Set<BranchService>();

    public DbSet<Doctor> Doctors => Set<Doctor>();

    public DbSet<DoctorBranch> DoctorBranches => Set<DoctorBranch>();

    public DbSet<DoctorService> DoctorServices => Set<DoctorService>();

    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();

    public DbSet<DoctorTimeBlock> DoctorTimeBlocks => Set<DoctorTimeBlock>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<AppointmentStatusHistory> AppointmentStatusHistory => Set<AppointmentStatusHistory>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<AbuseReport> AbuseReports => Set<AbuseReport>();

    public DbSet<ServiceCompletion> ServiceCompletions => Set<ServiceCompletion>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<LedgerLine> LedgerLines => Set<LedgerLine>();

    public DbSet<Settlement> Settlements => Set<Settlement>();

    public DbSet<SettlementItem> SettlementItems => Set<SettlementItem>();

    public DbSet<DailyFinancialSummary> DailyFinancialSummaries => Set<DailyFinancialSummary>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<TelegramLinkToken> TelegramLinkTokens => Set<TelegramLinkToken>();

    public DbSet<TelegramChatLink> TelegramChatLinks => Set<TelegramChatLink>();

    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    public DbSet<TelegramBotState> TelegramBotStates => Set<TelegramBotState>();

    public DbSet<DevCapturedMessage> DevCapturedMessages => Set<DevCapturedMessage>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    public DbSet<StoredImage> StoredImages => Set<StoredImage>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // btree_gist: exclusion constraints on (doctor_id, period) for booking (ADR-004).
        // citext: case-insensitive email identifiers.
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OqTishDbContext).Assembly);

        // tenancy.distance_km(lat1, lng1, lat2, lng2): Haversine great-circle distance in km (OD-16),
        // created by migration CatalogScheduling. Mapped so public search stays in LINQ and decimal.
        modelBuilder.HasDbFunction(typeof(GeoFunctions).GetMethod(nameof(GeoFunctions.DistanceKm))!)
            .HasName("distance_km")
            .HasSchema("tenancy");

        // statement_timestamp(): database time for deadline predicates in conditional UPDATEs (ADR-006, BE2).
        modelBuilder.HasDbFunction(typeof(DbTime).GetMethod(nameof(DbTime.StatementTimestamp))!)
            .HasName("statement_timestamp")
            .IsBuiltIn()
            .HasStoreType("timestamp with time zone");
    }
}
