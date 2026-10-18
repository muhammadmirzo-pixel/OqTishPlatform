using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OqTish.Application.Common;
using OqTish.Infrastructure;
using OqTish.Infrastructure.Persistence;
using OqTish.IntegrationTests.Infrastructure;

namespace OqTish.IntegrationTests.Booking;

/// <summary>
/// The real Application + Infrastructure DI graph (AddInfrastructure) on a dedicated test database, without
/// HTTP. Used by the concurrency catalogue (TESTING.md §3): handlers run exactly as in the API, the caller is
/// set per DI scope (<see cref="TestUserContext"/>), and authorization still comes from the real
/// <c>AccessService</c> reading memberships from the database. HTTP-level contracts and authorization are
/// covered separately through <see cref="ApiFactory"/>.
/// </summary>
public sealed class BookingHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private BookingHost(string connectionString, IDictionary<string, string?>? settings)
    {
        ConnectionString = connectionString;
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Main"] = connectionString,
            ["Sms:Provider"] = "Fake",
            ["Security:RequireTotpForPrivilegedRoles"] = "false",
        };
        foreach (var (k, v) in settings ?? new Dictionary<string, string?>())
        {
            values[k] = v;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<TestUserContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<TestUserContext>());
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<TestUserContext>());
        services.AddInfrastructure(configuration, new TestEnvironment());
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public string ConnectionString { get; }

    public IServiceProvider Services => _services;

    public static async Task<BookingHost> CreateAsync(PostgresFixture postgres, IDictionary<string, string?>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        var cs = await postgres.CreateMigratedDatabaseAsync(TestContext.Current.CancellationToken);
        return new BookingHost(cs, settings);
    }

    public OqTishDbContext CreateDbContext() => PostgresFixture.CreateDbContext(ConnectionString);

    /// <summary>A DI scope acting as <paramref name="userId"/> (null = system/anonymous).</summary>
    public AsyncServiceScope Scope(Guid? userId)
    {
        var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestUserContext>().UserId = userId;
        return scope;
    }

    public async Task<T> RunAsync<T>(Guid? userId, Func<IServiceProvider, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = Scope(userId);
        return await work(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "OqTish.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>Settable caller for a DI scope.</summary>
public sealed class TestUserContext : IRequestContext, ICurrentUser
{
    public Guid? UserId { get; set; }

    public Guid? SessionId => null;

    public string? RemoteIp => null;

    public string? UserAgent => null;

    public string? CorrelationId => "test";
}
