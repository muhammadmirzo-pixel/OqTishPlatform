using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Main";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured. " +
                "Set the environment variable ConnectionStrings__Main (see .env.example).");
        }

        services.AddDbContext<OqTishDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddSingleton(TimeProvider.System);

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
