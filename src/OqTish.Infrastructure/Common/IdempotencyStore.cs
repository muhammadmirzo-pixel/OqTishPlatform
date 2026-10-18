using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Common.Json;
using OqTish.Domain.Common;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Infrastructure.Common;

/// <summary>See <see cref="IIdempotencyStore"/>. The request hash is SHA-256 of the camelCase JSON of the request object.</summary>
internal sealed class IdempotencyStore(OqTishDbContext db, TimeProvider clock) : IIdempotencyStore
{
    private sealed record Row(string RequestHash, string Status, string? ResponseJson);

    public async Task<Result<T>> RunAsync<T>(
        Guid userId,
        string key,
        object request,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(key) || key.Length > IIdempotencyStore.MaxKeyLength || key.Any(c => c < 0x21 || c > 0x7E))
        {
            return Error.InvalidField("Idempotency-Key", $"Must be 1–{IIdempotencyStore.MaxKeyLength} printable ASCII characters.");
        }

        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("IIdempotencyStore.RunAsync must run inside ITransactionRunner.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, request.GetType(), JsonDefaults.Options)));
        var now = clock.GetUtcNow();
        var inProgress = nameof(IdempotencyStatus.InProgress);

        // A concurrent duplicate blocks here on the primary key until the first transaction ends.
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO common.idempotency_keys (user_id, key, request_hash, status, created_at)
            VALUES ({userId}, {key}, {hash}, {inProgress}, {now})
            ON CONFLICT (user_id, key) DO NOTHING
            """, cancellationToken);

        if (inserted == 0)
        {
            var existing = await db.Database.SqlQuery<Row>($"""
                SELECT request_hash, status, response_json::text AS response_json
                FROM common.idempotency_keys WHERE user_id = {userId} AND key = {key} FOR UPDATE
                """).ToListAsync(cancellationToken);
            var row = existing.Single();
            if (!string.Equals(row.RequestHash, hash, StringComparison.Ordinal))
            {
                return CommonErrors.IdempotencyMismatch;
            }

            if (row.Status == nameof(IdempotencyStatus.Completed) && row.ResponseJson is not null)
            {
                return JsonSerializer.Deserialize<T>(row.ResponseJson, JsonDefaults.Options)!;
            }

            // Same transaction already holds the key (nested use) — run normally.
        }

        var result = await work(cancellationToken);
        if (result.IsSuccess)
        {
            var json = JsonSerializer.Serialize(result.Value, JsonDefaults.Options);
            var completed = nameof(IdempotencyStatus.Completed);
            var done = clock.GetUtcNow();
            await db.Database.ExecuteSqlAsync($"""
                UPDATE common.idempotency_keys SET status = {completed}, response_json = CAST({json} AS jsonb), completed_at = {done}
                WHERE user_id = {userId} AND key = {key}
                """, cancellationToken);
        }

        return result;
    }
}
