using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OqTish.Application;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Sms;
using OqTish.Application.Identity.Telegram;
using OqTish.Infrastructure.Booking;
using OqTish.Infrastructure.Common;
using OqTish.Infrastructure.Persistence;
using OqTish.Infrastructure.Security;
using OqTish.Infrastructure.Sms;
using OqTish.Infrastructure.Telegram;

namespace OqTish.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Main";

    /// <summary>
    /// Registers persistence, common ports, security, SMS, Telegram and the Application layer. Validates
    /// configuration first (<see cref="StartupGuards"/>) and throws on unsafe settings. Hosts replace
    /// <see cref="IRequestContext"/>/<see cref="ICurrentUser"/> (defaults: system context).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured. " +
                "Set the environment variable ConnectionStrings__Main (see .env.example).");
        }

        var (pepper, smsProvider) = StartupGuards.Validate(configuration, environment);

        services.AddDbContext<OqTishDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddScoped<IOqTishDb>(sp => sp.GetRequiredService<OqTishDbContext>());
        services.TryAddSingleton(TimeProvider.System);

        // Options
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.PostConfigure<SecurityOptions>(o => o.OtpPepper = pepper);
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<OqTish.Application.Staff.StaffOptions>(configuration.GetSection(OqTish.Application.Staff.StaffOptions.SectionName));
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));
        services.Configure<OqTish.Application.Booking.BookingOptions>(configuration.GetSection(OqTish.Application.Booking.BookingOptions.SectionName));

        // Common ports
        services.AddSingleton<IDbErrorClassifier, DbErrorClassifier>();
        services.AddScoped<ITransactionRunner, TransactionRunner>();
        services.AddScoped<IRowLocks, RowLocks>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IDbClock, DbClock>();
        services.AddScoped<OqTish.Application.Booking.IBookingStore, BookingStore>();
        services.TryAddScoped<SystemRequestContext>();
        services.TryAddScoped<IRequestContext>(sp => sp.GetRequiredService<SystemRequestContext>());
        services.TryAddScoped<ICurrentUser>(sp => sp.GetRequiredService<SystemRequestContext>());

        // Security
        services.AddDataProtection()
            .SetApplicationName("OqTish")
            .PersistKeysToDbContext<OqTishDbContext>();
        services.AddSingleton<IKeyedHasher, HmacKeyedHasher>();
        services.AddSingleton<IPasswordHashService, PasswordHashService>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // SMS
        services.AddSingleton<FakeSmsMailbox>();
        if (smsProvider == SmsProviders.Fake)
        {
            services.AddSingleton<ISmsSender, FakeSmsSender>();
        }
        else
        {
            services.AddSingleton<ISmsSender>(sp => new EskizSmsSender(
                new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
                {
                    Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<SmsOptions>>().Value.Eskiz.TimeoutSeconds),
                },
                sp.GetRequiredService<IOptions<SmsOptions>>(),
                sp.GetRequiredService<ILogger<EskizSmsSender>>()));
        }

        // Telegram
        services.AddSingleton<ITelegramLoginVerifier, TelegramLoginVerifier>();

        services.AddApplication();
        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString)
    {
        options
            .UseNpgsql(WithDefaults(connectionString), npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
                // No EnableRetryOnFailure: an execution strategy would reject explicit
                // BeginTransaction calls and blindly replay lock-taking transactions. Booking and
                // finance transactions use their own bounded retry policy instead (ADR-005).
            })
            .UseSnakeCaseNamingConvention();
    }

    /// <summary>
    /// Kerberos/GSS transport encryption is not used (TLS is); disabling it unless explicitly set
    /// avoids a native libgssapi probe on every new connection.
    /// </summary>
    private static string WithDefaults(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!connectionString.Contains("GSS", StringComparison.OrdinalIgnoreCase))
        {
            builder.GssEncryptionMode = GssEncryptionMode.Disable;
        }

        return builder.ConnectionString;
    }
}
