using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using OqTish.Application.Common;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Infrastructure.Common;

/// <summary>See <see cref="ITransactionRunner"/>. Retries only 40001/40P01 with jittered exponential backoff.</summary>
internal sealed partial class TransactionRunner(
    OqTishDbContext db,
    IDbErrorClassifier classifier,
    TimeProvider clock,
    ILogger<TransactionRunner> logger) : ITransactionRunner
{
    public async Task<Result<T>> RunAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        int maxAttempts = 3)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        if (db.Database.CurrentTransaction is not null)
        {
            return await work(cancellationToken);
        }

        for (var attempt = 1; ; attempt++)
        {
            IDbContextTransaction? tx = null;
            try
            {
                tx = await db.Database.BeginTransactionAsync(isolation, cancellationToken);
                var result = await work(cancellationToken);
                if (result.IsSuccess)
                {
                    await tx.CommitAsync(cancellationToken);
                }
                else
                {
                    await tx.RollbackAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                }

                return result;
            }
            catch (Exception ex) when (classifier.Classify(ex) is { IsTransient: true } error)
            {
                await SafeRollbackAsync(tx);
                db.ChangeTracker.Clear();
                if (attempt >= maxAttempts)
                {
                    LogExhausted(logger, error.SqlState, attempt);
                    return CommonErrors.Concurrency;
                }

                LogRetry(logger, error.SqlState, attempt);
                await Task.Delay(Backoff(attempt), clock, cancellationToken);
            }
            catch
            {
                await SafeRollbackAsync(tx);
                db.ChangeTracker.Clear();
                throw;
            }
            finally
            {
                if (tx is not null)
                {
                    await tx.DisposeAsync();
                }
            }
        }
    }

    public async Task<Result> RunAsync(
        Func<CancellationToken, Task<Result>> work,
        CancellationToken cancellationToken,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        int maxAttempts = 3)
    {
        ArgumentNullException.ThrowIfNull(work);
        var result = await RunAsync<bool>(
            async ct =>
            {
                var r = await work(ct);
                return r.IsSuccess ? true : r.Error;
            },
            cancellationToken,
            isolation,
            maxAttempts);
        return result.IsSuccess ? Result.Success : result.Error;
    }

    /// <summary>~25 ms, 50 ms, 100 ms … plus up to 100% random jitter.</summary>
    internal static TimeSpan Backoff(int attempt)
    {
        var baseMs = 25 << Math.Min(attempt - 1, 6);
        return TimeSpan.FromMilliseconds(baseMs + RandomNumberGenerator.GetInt32(0, baseMs + 1));
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction? tx)
    {
        if (tx is null)
        {
            return;
        }

        try
        {
            await tx.RollbackAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Connection already broken / transaction completed: nothing to roll back.
        }
        catch (Npgsql.NpgsqlException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transaction aborted with {SqlState}; retrying (attempt {Attempt})")]
    private static partial void LogRetry(ILogger logger, string sqlState, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transaction aborted with {SqlState}; giving up after {Attempt} attempts")]
    private static partial void LogExhausted(ILogger logger, string sqlState, int attempt);
}
