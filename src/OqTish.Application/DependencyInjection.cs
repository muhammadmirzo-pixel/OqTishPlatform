using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OqTish.Application.Admin;
using OqTish.Application.Audit;
using OqTish.Application.Booking;
using OqTish.Application.Catalog;
using OqTish.Application.Common.Images;
using OqTish.Application.Common.Seeding;
using OqTish.Application.Finance;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Notifications;
using OqTish.Application.Notifications.Telegram;
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
        services.AddScoped<ClinicLogoHandler>();
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
        services.TryAddScoped<IAppointmentConfirmedHandler, NoOpAppointmentConfirmedHandler>();
        services.AddScoped<IClinicDashboardContributor, BookingDashboardContributor>();
        services.AddScoped<IAdminDashboardContributor, BookingAdminDashboardContributor>();
        services.AddScoped<IAccountDeletionContributor, BookingAccountDeletionContributor>();

        // Finance (BE3): replaces the booking no-op hooks.
        services.AddScoped<PeriodLock>();
        services.AddScoped<LedgerPoster>();
        services.AddScoped<LedgerTotals>();
        services.AddScoped<FinanceAccess>();
        services.Replace(ServiceDescriptor.Scoped<IAppointmentCompletedHandler, FinanceCompletionHandler>());
        services.Replace(ServiceDescriptor.Scoped<IAppointmentConfirmedHandler, FinanceConfirmationHandler>());
        services.AddScoped<PaymentHandlers>();
        services.AddScoped<FinanceReportQueries>();
        services.AddScoped<CommissionRuleHandlers>();
        services.AddScoped<SettlementHandlers>();
        services.AddScoped<RebuildDailySummariesHandler>();
        services.AddScoped<IClinicDashboardContributor, FinanceDashboardContributor>();

        // Notifications + Telegram bot (BE4)
        services.AddScoped<OutboxDispatcher>();
        services.AddScoped<TelegramBotService>();
        services.AddScoped<TelegramPoller>();
        services.AddSingleton<TelegramChatRateLimiter>();
        services.AddScoped<NotificationAdminHandlers>();
        services.AddScoped<RetentionHandler>();

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
        services.AddScoped<AvatarHandler>();

        // Stored images (logos, avatars)
        services.AddScoped<StoredImageQuery>();

        // Seeding
        services.AddScoped<SeedRunner>();
        services.AddScoped<ISeedContributor, IdentitySeedContributor>();
        services.AddScoped<ISeedContributor, TenancyDemoSeedContributor>();
        services.AddScoped<ISeedContributor, CatalogSchedulingDemoSeedContributor>();
        services.AddScoped<ISeedContributor, BookingDemoSeedContributor>();
        services.AddScoped<ISeedContributor, FinanceDemoSeedContributor>();

        return services;
    }
}
