using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OqTish.Application.Common.Seeding;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Staff;

namespace OqTish.Application;

public static class DependencyInjection
{
    /// <summary>Registers Application services and handlers (scoped). Options are bound by the host/Infrastructure.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Staff / access
        services.AddScoped<IAccessService, AccessService>();
        services.TryAddScoped<IDoctorIdentityResolver, NoDoctorIdentityResolver>();

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

        return services;
    }
}
