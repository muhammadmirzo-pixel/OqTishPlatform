using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OqTish.Api.Auth;
using OqTish.Api.Common;
using OqTish.Api.Endpoints;
using OqTish.Api.Health;
using OqTish.Api.Seeding;
using OqTish.Application.Common;
using OqTish.Application.Common.Json;
using OqTish.Application.Common.Security;
using OqTish.Infrastructure;
using OqTish.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Request context from HttpContext (replaces the system default registered by Infrastructure).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpRequestContext>();
builder.Services.Replace(ServiceDescriptor.Scoped<IRequestContext>(sp => sp.GetRequiredService<HttpRequestContext>()));
builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(sp => sp.GetRequiredService<HttpRequestContext>()));

// Errors: RFC 7807 ProblemDetails everywhere, always with `code` and `traceId`. Stack traces never leave the process.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        if (!ctx.ProblemDetails.Extensions.ContainsKey("code"))
        {
            ctx.ProblemDetails.Extensions["code"] = ProblemResults.DefaultCode(ctx.ProblemDetails.Status ?? 500);
        }
    });
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.Configure<JsonOptions>(options => JsonDefaults.Configure(options.SerializerOptions));
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1024 * 1024);

builder.Services.AddOpenApi();

// Authentication: server-side session cookie validated against the DB on every request (ADR-016).
builder.Services.AddSingleton<SessionCookie>();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OqTishDbContext>("database", tags: ["ready"])
    .AddCheck<PendingMigrationsHealthCheck>("migrations", tags: ["ready"]);

// TLS terminates at the reverse proxy; trust X-Forwarded-* only from configured proxy networks.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var cidr in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    }
});

builder.Services.AddOqTishRateLimiting(builder.Configuration);

// Telegram webhook mode: setWebhook after startup (no-op in Polling mode / when Telegram is disabled).
builder.Services.AddHostedService<TelegramWebhookRegistrar>();

var app = builder.Build();

if (SeedCommand.IsRequested(args))
{
    return await SeedCommand.RunAsync(app.Services, app.Environment, app.Logger, CancellationToken.None);
}

if (PlatformAdminBootstrapCommand.IsRequested(args))
{
    return await PlatformAdminBootstrapCommand.RunAsync(app.Services, args, PlatformAdminBootstrapCommand.ReadPasswordFromEnvironmentOrStdin, app.Logger, CancellationToken.None);
}

if (!app.Environment.IsDevelopment() && !app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.RequireTotpForPrivilegedRoles)
{
    // Review R10: Production must never run privileged roles without a second factor.
    if (app.Environment.IsProduction())
    {
        throw new InvalidOperationException("Security:RequireTotpForPrivilegedRoles must be true in Production.");
    }

    StartupLog.TotpRuleDisabled(app.Logger, app.Environment.EnvironmentName);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<ClientHeaderMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "OqTish API v1"));
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).DisableRateLimiting().AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).DisableRateLimiting().AllowAnonymous();

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapTelegramEndpoints();
app.MapMyInvitationEndpoints();
app.MapClinicEndpoints();
app.MapBookingEndpoints();
app.MapPublicEndpoints();
app.MapAdminEndpoints();
app.MapFinanceEndpoints();
app.MapFileEndpoints();
app.MapDevEndpoints(app.Environment);

await app.RunAsync();
return 0;

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program
{
}

internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Security:RequireTotpForPrivilegedRoles is false in {Environment}: owners, finance managers and platform admins can act without TOTP")]
    public static partial void TotpRuleDisabled(ILogger logger, string environment);
}
