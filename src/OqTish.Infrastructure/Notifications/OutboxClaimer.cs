using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using OqTish.Application.Notifications;
using OqTish.Domain.Common;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Infrastructure.Notifications;

/// <summary>
/// One statement, database time, no long-held locks: the inner SELECT locks due rows with <c>FOR UPDATE SKIP LOCKED</c>
/// (concurrent dispatchers take disjoint rows), the UPDATE marks them Processing with a fresh claim token and a lease,
/// and increments <c>attempts</c>. Rows still Processing after their lease (dispatcher crash) are due again.
/// </summary>
internal sealed class OutboxClaimer(OqTishDbContext db) : IOutboxClaimer
{
    private const string Sql = """
        WITH due AS (
            SELECT id FROM notify.outbox_messages
            WHERE (status = 'Pending' AND next_attempt_at <= clock_timestamp())
               OR (status = 'Processing' AND locked_until < clock_timestamp())
            ORDER BY next_attempt_at, id
            LIMIT @batch
            FOR UPDATE SKIP LOCKED)
        UPDATE notify.outbox_messages m
        SET status = 'Processing', claim_token = @token, locked_until = clock_timestamp() + @lease, attempts = m.attempts + 1
        FROM due
        WHERE m.id = due.id
        RETURNING m.id, m.attempts
        """;

    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimDueAsync(int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        var token = IdGenerator.New();
        var claimed = new List<ClaimedOutboxMessage>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var command = new NpgsqlCommand(Sql, connection);
            if (db.Database.CurrentTransaction is { } transaction)
            {
                command.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            }

            command.Parameters.Add(new NpgsqlParameter("batch", NpgsqlDbType.Integer) { Value = Math.Max(1, batchSize) });
            command.Parameters.Add(new NpgsqlParameter("token", NpgsqlDbType.Uuid) { Value = token });
            command.Parameters.Add(new NpgsqlParameter("lease", NpgsqlDbType.Interval) { Value = lease });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(new ClaimedOutboxMessage(reader.GetGuid(0), token, reader.GetInt32(1)));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return claimed;
    }
}
