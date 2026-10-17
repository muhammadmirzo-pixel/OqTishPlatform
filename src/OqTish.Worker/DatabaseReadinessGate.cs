using Microsoft.EntityFrameworkCore;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Worker;

/// <summary>
/// Blocks worker startup until the database is reachable and fully migrated, so background jobs
/// never run against an outdated schema. Jobs (outbox dispatch, expiry sweeper, reminders,
/// summaries) are registered after this gate in later phases.
/// </summary>
internal sealed partial class DatabaseReadinessGate(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseReadinessGate> logger,
    TimeProvider clock) : IHostedService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private const int MaxAttempts = 24;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<OqTishDbContext>();
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
                if (pending == 0)
                {
                    LogReady(logger);
                    return;
                }

                LogPendingMigrations(logger, pending, attempt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxAttempts)
            {
                LogDatabaseUnavailable(logger, attempt, ex.GetType().Name);
            }

            if (attempt >= MaxAttempts)
            {
                throw new InvalidOperationException("Database is not ready (unreachable or pending migrations); worker cannot start.");
            }

            await Task.Delay(RetryDelay, clock, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Database reachable and schema up to date")]
    private static partial void LogReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database has {PendingCount} pending migration(s); waiting (attempt {Attempt})")]
    private static partial void LogPendingMigrations(ILogger logger, int pendingCount, int attempt);

    // Only the exception type is logged: connection exceptions can echo host/user details.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database unavailable ({ExceptionType}); retrying (attempt {Attempt})")]
    private static partial void LogDatabaseUnavailable(ILogger logger, int attempt, string exceptionType);
}
