using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OqTish.Application.Common.Seeding;

/// <summary>
/// Runs every registered <see cref="ISeedContributor"/> in order. The host decides whether seeding is
/// allowed at all (Development/Staging only; never Production).
/// </summary>
public sealed partial class SeedRunner(IEnumerable<ISeedContributor> contributors, ILogger<SeedRunner> logger)
{
    public async Task<IReadOnlyList<SeedStepResult>> RunAsync(CancellationToken cancellationToken)
    {
        var results = new List<SeedStepResult>();
        foreach (var contributor in contributors.OrderBy(c => c.Order).ThenBy(c => c.Name, StringComparer.Ordinal))
        {
            var sw = Stopwatch.StartNew();
            await contributor.SeedAsync(cancellationToken);
            sw.Stop();
            LogSeeded(logger, contributor.Name, contributor.Order, sw.ElapsedMilliseconds);
            results.Add(new SeedStepResult(contributor.Name, contributor.Order, sw.Elapsed));
        }

        return results;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seed contributor {Name} (order {Order}) finished in {ElapsedMs} ms")]
    private static partial void LogSeeded(ILogger logger, string name, int order, long elapsedMs);
}
