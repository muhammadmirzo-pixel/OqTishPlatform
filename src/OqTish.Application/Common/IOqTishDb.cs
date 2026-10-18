using Microsoft.EntityFrameworkCore;
using OqTish.Domain.Audit;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;
using OqTish.Domain.Identity;
using OqTish.Domain.Notifications;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Common;

/// <summary>
/// The database as Application sees it: DbSets for LINQ (translated to SQL) and SaveChanges. Raw SQL,
/// locks, transactions and PostgreSQL error codes are behind the other ports (<see cref="ITransactionRunner"/>,
/// <see cref="IRowLocks"/>, <see cref="IDbErrorClassifier"/>). Implemented by <c>OqTishDbContext</c>.
/// Later modules add their DbSets here.
/// </summary>
public interface IOqTishDb
{
    // identity
    DbSet<User> Users { get; }

    DbSet<AuthIdentity> AuthIdentities { get; }

    DbSet<PasswordCredential> PasswordCredentials { get; }

    DbSet<OtpChallenge> OtpChallenges { get; }

    DbSet<Session> Sessions { get; }

    DbSet<TotpCredential> TotpCredentials { get; }

    DbSet<PatientProfile> PatientProfiles { get; }

    // tenancy
    DbSet<Clinic> Clinics { get; }

    DbSet<Branch> Branches { get; }

    DbSet<BranchOpeningHours> BranchOpeningHours { get; }

    DbSet<BranchBookingLock> BranchBookingLocks { get; }

    // staff
    DbSet<ClinicMembership> ClinicMemberships { get; }

    DbSet<BranchAccess> BranchAccess { get; }

    DbSet<StaffInvitation> StaffInvitations { get; }

    DbSet<PlatformAdminGrant> PlatformAdminGrants { get; }

    // catalog
    DbSet<Service> Services { get; }

    DbSet<BranchService> BranchServices { get; }

    // scheduling
    DbSet<Doctor> Doctors { get; }

    DbSet<DoctorBranch> DoctorBranches { get; }

    DbSet<DoctorService> DoctorServices { get; }

    DbSet<WorkingHours> WorkingHours { get; }

    // notify
    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<TelegramLinkToken> TelegramLinkTokens { get; }

    DbSet<TelegramChatLink> TelegramChatLinks { get; }

    // audit / common
    DbSet<AuditLogEntry> AuditLog { get; }

    DbSet<IdempotencyRecord> IdempotencyKeys { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
