using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Api.Health;

/// <summary>
/// Readiness fails while the database schema is behind the code. The API never applies migrations
/// itself; they are applied by the separate migrate step (see docs/DATABASE.md §5).
/// </summary>
internal sealed class PendingMigrationsHealthCheck(OqTishDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        return pending.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"{pending.Count} pending migration(s).");
    }
}
