using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OqTish.Application.Admin;
using OqTish.Application.Audit;
using OqTish.Application.Booking;
using OqTish.Application.Catalog;
using OqTish.Application.Common.Seeding;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Public;
using OqTish.Application.Reviews;
using OqTish.Application.Scheduling;
using OqTish.Application.Staff;
using OqTish.Application.Tenancy;

namespace OqTish.Application;

public static class DependencyInjection
{
    /// <summary>Registers Application services and handlers (scoped). Options are bound by the host/Infrastructure.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Staff / access
        services.AddScoped<IAccessService, AccessService>();
        services.AddScoped<IDoctorIdentityResolver, DoctorIdentityResolver>();
        services.AddScoped<StaffHandlers>();
        services.AddScoped<ClinicInvitationHandlers>();
        services.AddScoped<MyInvitationHandlers>();

        // Tenancy / catalog / scheduling
        services.AddScoped<RegisterClinicHandler>();
        services.AddScoped<ClinicProfileHandler>();
        services.AddScoped<ClinicDashboardQuery>();
        services.AddScoped<BranchHandlers>();
        services.AddScoped<CatalogHandlers>();
        services.AddScoped<DoctorHandlers>();
        services.AddScoped<WorkingHoursHandlers>();

        // Booking (BE2)
        services.AddScoped<BookingRules>();
        services.AddScoped<BookingNotifier>();
        services.AddScoped<BookingSweep>();
        services.AddScoped<AvailabilityLoader>();
        services.AddScoped<AppointmentReader>();
        services.AddScoped<AvailabilityQuery>();
        services.AddScoped<PublicAvailability>();
        services.AddScoped<CreateAppointmentHandler>();
        services.AddScoped<WalkInAppointmentHandler>();
        services.AddScoped<StaffAppointmentHandlers>();
        services.AddScoped<PatientAppointmentHandlers>();
        services.AddScoped<ClinicAppointmentQueries>();
        services.AddScoped<ExpirePendingAppointmentsHandler>();
        services.AddScoped<TimeOffHandlers>();
        services.TryAddScoped<IAppointmentCompletedHandler, NoOpAppointmentCompletedHandler>();
        services.AddScoped<IClinicDashboardContributor, BookingDashboardContributor>();
        services.AddScoped<IAdminDashboardContributor, BookingAdminDashboardContributor>();
        services.AddScoped<IAccountDeletionContributor, BookingAccountDeletionContributor>();

        // Reviews / reports (BE2)
        services.AddScoped<ReviewHandlers>();
        services.AddScoped<ReportHandlers>();

        // Public, audit, admin
        services.AddScoped<PublicCatalogQueries>();
        services.AddScoped<TenantDataQueries>();
        services.AddScoped<AuditQueries>();
        services.AddScoped<AdminHandlers>();
        services.AddScoped<CreatePlatformAdminHandler>();

        // Identity services
        services.AddScoped<SessionService>();
        services.AddScoped<OtpService>();
        services.AddScoped<TotpService>();
        services.AddScoped<SignInService>();
        services.AddScoped<MeQuery>();

        // Identity handlers
        services.AddScoped<RequestOtpHandler>();
        services.AddScoped<VerifyOtpHandler>();
        services.AddScoped<PasswordLoginHandler>();
        services.AddScoped<RequestPasswordResetHandler>();
        services.AddScoped<ConfirmPasswordResetHandler>();
        services.AddScoped<TelegramSignInHandler>();
        services.AddScoped<TelegramConfigQuery>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<UpdateProfileHandler>();
        services.AddScoped<SetPasswordHandler>();
        services.AddScoped<RequestPhoneLinkHandler>();
        services.AddScoped<VerifyPhoneLinkHandler>();
        services.AddScoped<TotpHandler>();
        services.AddScoped<TelegramLinkService>();
        services.AddScoped<SessionsHandler>();
        services.AddScoped<DeleteAccountHandler>();

        // Seeding
        services.AddScoped<SeedRunner>();
        services.AddScoped<ISeedContributor, IdentitySeedContributor>();
        services.AddScoped<ISeedContributor, TenancyDemoSeedContributor>();
        services.AddScoped<ISeedContributor, CatalogSchedulingDemoSeedContributor>();
        services.AddScoped<ISeedContributor, BookingDemoSeedContributor>();

        return services;
    }
}
