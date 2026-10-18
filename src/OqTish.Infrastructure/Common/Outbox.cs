using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Common.Json;
using OqTish.Domain.Common;
using OqTish.Domain.Notifications;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Infrastructure.Common;

/// <summary><c>INSERT … ON CONFLICT (dedup_key) DO NOTHING</c> in the current transaction.</summary>
internal sealed class Outbox(OqTishDbContext db, TimeProvider clock) : IOutbox
{
    public async Task<bool> EnqueueAsync(string type, string dedupKey, object payload, DateTimeOffset? notBefore, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKey);
        ArgumentNullException.ThrowIfNull(payload);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("IOutbox.EnqueueAsync must run inside ITransactionRunner.");
        }

        var now = clock.GetUtcNow();
        var due = notBefore ?? now;
        var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        var pending = nameof(OutboxStatus.Pending);
        var id = IdGenerator.New();
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO notify.outbox_messages (id, type, dedup_key, payload, created_at, not_before, status, attempts, next_attempt_at)
            VALUES ({id}, {type}, {dedupKey}, CAST({json} AS jsonb), {now}, {due}, {pending}, 0, {due})
            ON CONFLICT (dedup_key) DO NOTHING
            """, cancellationToken);
        return inserted == 1;
    }
}
