using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OqTish.Infrastructure.Persistence;
using OqTish.Infrastructure.Sms;

namespace OqTish.IntegrationTests.Infrastructure;

/// <summary>
/// The real API (Program) on a dedicated test database. Defaults: Development, Fake SMS, relaxed IP-level
/// limits (per-phone OTP limits stay real). Clients use https://localhost so the real <c>__Host-oqtish</c>
/// Secure cookie round-trips, and send <c>X-OqTish-Client: web</c>.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TelegramBotToken = "123456:TEST-bot-token-not-real";

    private readonly Dictionary<string, string?> _settings;

    static ApiFactory()
    {
        // Each test host would otherwise create inotify watchers for appsettings*.json (limit 128 per user).
        System.Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
        System.Environment.SetEnvironmentVariable("ASPNETCORE_hostBuilder__reloadConfigOnChange", "false");
    }

    public ApiFactory(string connectionString, string environment = "Development", IDictionary<string, string?>? settings = null)
    {
        ConnectionString = connectionString;
        Environment = environment;
        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Main"] = connectionString,
            ["Auth:Otp:MaxPerIp"] = "100000",
            ["RateLimiting:GlobalPermitPerMinute"] = "100000",
            ["RateLimiting:OtpSendPermitPerMinute"] = "100000",
            ["RateLimiting:CredentialsPermitPerMinute"] = "100000",
            ["Telegram:Enabled"] = "true",
            ["Telegram:BotToken"] = TelegramBotToken,
            ["Telegram:BotUsername"] = "oqtish_test_bot",
            ["Telegram:MiniAppUrl"] = "https://example.test/tg",
        };
        if (environment != "Development")
        {
            _settings["Security:OtpPepper"] = "test-pepper-0123456789-0123456789-abcdef";
            _settings["Sms:Provider"] = "Eskiz";
            _settings["Sms:Eskiz:Email"] = "nobody@example.test";
            _settings["Sms:Eskiz:Password"] = "not-a-real-password";
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    public string ConnectionString { get; }

    public string Environment { get; }

    public CapturingLoggerProvider Logs { get; } = new();

    public FakeSmsMailbox Sms => Services.GetRequiredService<FakeSmsMailbox>();

    public static async Task<ApiFactory> CreateAsync(PostgresFixture postgres, string environment = "Development", IDictionary<string, string?>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        var cs = await postgres.CreateMigratedDatabaseAsync(TestContext.Current.CancellationToken);
        return new ApiFactory(cs, environment, settings);
    }

    /// <summary>A client with cookies, https base address and the CSRF header.</summary>
    public HttpClient CreateApiClient(bool withClientHeader = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        if (withClientHeader)
        {
            client.DefaultRequestHeaders.Add("X-OqTish-Client", "web");
        }

        return client;
    }

    public OqTishDbContext CreateDbContext() => PostgresFixture.CreateDbContext(ConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(Environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(Logs);
            logging.AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace);
        });
    }
}
