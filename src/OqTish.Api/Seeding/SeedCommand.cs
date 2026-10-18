using OqTish.Application.Common.Seeding;

namespace OqTish.Api.Seeding;

/// <summary>
/// <c>dotnet OqTish.Api.dll seed</c>: runs every <see cref="ISeedContributor"/> and exits. Allowed only in
/// Development and Staging; Production always refuses (exit code 2) and there is no override.
/// </summary>
public static partial class SeedCommand
{
    public const string Verb = "seed";

    public static bool IsRequested(string[] args) =>
        args is { Length: > 0 } && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowed(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsStaging();

    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment, ILogger logger, CancellationToken ct)
    {
        if (!IsAllowed(environment))
        {
            LogRefused(logger, environment.EnvironmentName);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var results = await scope.ServiceProvider.GetRequiredService<SeedRunner>().RunAsync(ct);
        LogDone(logger, results.Count);
        return 0;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Seeding refused in environment {Environment}: only Development and Staging may be seeded")]
    private static partial void LogRefused(ILogger logger, string environment);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding finished ({Count} contributors)")]
    private static partial void LogDone(ILogger logger, int count);
}
