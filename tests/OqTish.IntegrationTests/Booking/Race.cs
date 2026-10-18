using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Infrastructure.Persistence;

namespace OqTish.IntegrationTests.Booking;

/// <summary>
/// Releases N operations at the same instant (CONVENTIONS §7 "Barrier"): every participant first opens its
/// own DI scope and database connection, then signals the barrier and waits on a shared gate; the gate opens
/// only when all N are ready. Returns the results in participant order.
/// </summary>
public static class Race
{
    public static async Task<T[]> RunAsync<T>(BookingHost host, IReadOnlyList<(Guid? User, Func<IServiceProvider, Task<T>> Work)> participants)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(participants);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var barrier = new CountdownEvent(participants.Count);
        var tasks = participants.Select(p => Task.Run(async () =>
        {
            await using var scope = host.Scope(p.User);
            await scope.ServiceProvider.GetRequiredService<OqTishDbContext>().Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            barrier.Signal();
            await gate.Task;
            return await p.Work(scope.ServiceProvider);
        })).ToArray();
        await Task.Run(() => barrier.Wait(TimeSpan.FromSeconds(30)), TestContext.Current.CancellationToken);
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
}

/// <summary>TESTING.md B18: database invariants that must hold after every booking test.</summary>
public static class BookingInvariants
{
    private const string Sql = """
        SELECT
          (SELECT count(*) FROM scheduling.doctor_time_blocks a JOIN scheduling.doctor_time_blocks b
             ON a.doctor_id = b.doctor_id AND a.id < b.id AND a.is_active AND b.is_active AND a.period && b.period) AS overlapping,
          (SELECT count(*) FROM scheduling.doctor_time_blocks t LEFT JOIN booking.appointments a ON a.id = t.appointment_id
             WHERE t.is_active AND NOT (
                  (t.kind = 'TimeOff' AND t.appointment_id IS NULL)
               OR (t.kind = 'Hold' AND a.status = 'Pending' AND a.starts_at = t.starts_at AND a.ends_at = t.ends_at AND a.branch_id = t.branch_id)
               OR (t.kind = 'Appointment' AND a.status = 'Confirmed' AND a.doctor_id = t.doctor_id AND a.starts_at = t.starts_at
                   AND a.ends_at = t.ends_at AND a.branch_id = t.branch_id))) AS orphan_blocks,
          (SELECT count(*) FROM booking.appointments a WHERE a.status IN ('Pending', 'Confirmed')
             AND (SELECT count(*) FROM scheduling.doctor_time_blocks t WHERE t.appointment_id = a.id AND t.is_active) <> 1) AS unheld,
          (SELECT count(*) FROM booking.appointments a WHERE a.status IS DISTINCT FROM
             (SELECT h.to_status FROM booking.appointment_status_history h WHERE h.appointment_id = a.id ORDER BY h.occurred_at DESC, h.id DESC LIMIT 1)) AS history_mismatch
        """;

    /// <summary>0 overlapping active blocks per doctor; every active block ↔ Pending (Hold) / Confirmed (Appointment) / TimeOff; every Pending/Confirmed has exactly one active block; last history row = status.</summary>
    public static async Task AssertAsync(string connectionString)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(Sql, c);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await r.ReadAsync(TestContext.Current.CancellationToken);
        var values = (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3));
        Assert.True(values == (0, 0, 0, 0), $"B18 invariants violated: overlapping={values.Item1}, orphanBlocks={values.Item2}, unheld={values.Item3}, historyMismatch={values.Item4}");
    }
}
