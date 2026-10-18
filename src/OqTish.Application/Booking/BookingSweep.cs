using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Domain.Booking;

namespace OqTish.Application.Booking;

/// <summary>
/// Expires overdue Pending requests (deadline passed by database time) inside the caller's transaction:
/// lock rows → conditional UPDATE (status still Pending, deadline still passed) → release their blocks →
/// history (actor System) → outbox Expired notices. Used by the Worker sweeper (SKIP LOCKED batches) and by
/// every booking write before it inserts or moves a block ("sweep-on-write", ADR-006), so an overdue hold
/// never blocks a new booking even while the Worker is down.
/// </summary>
public sealed class BookingSweep(IOqTishDb db, IBookingStore store, IDbClock clock, BookingNotifier notifier)
{
    /// <returns>The number of requests expired.</returns>
    public async Task<int> ExpireOverdueAsync(Guid? branchId, IReadOnlyCollection<Guid> doctorIds, bool skipLocked, int limit, CancellationToken ct, Guid? exceptAppointmentId = null)
    {
        var ids = await store.LockOverduePendingAsync(branchId, doctorIds, skipLocked, limit, ct, exceptAppointmentId);
        if (ids.Count == 0)
        {
            return 0;
        }

        var now = await clock.NowAsync(ct);
        var idList = ids.ToList();
        var updated = await db.Appointments
            .Where(a => idList.Contains(a.Id) && a.Status == AppointmentStatus.Pending && a.ExpiresAt <= DbTime.StatementTimestamp())
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AppointmentStatus.Expired)
                .SetProperty(a => a.UpdatedAt, now)
                .SetProperty(a => a.StatusChangedAt, now), ct);
        if (updated != idList.Count)
        {
            // The rows are locked and were selected with the same predicate, so this cannot happen.
            throw new InvalidOperationException($"Expiry sweep updated {updated} of {idList.Count} locked rows.");
        }

        var clinics = await db.Appointments.AsNoTracking().Where(a => idList.Contains(a.Id)).Select(a => new { a.Id, a.ClinicId }).ToListAsync(ct);
        await store.ReleaseBlocksAsync(idList, now, ct);
        foreach (var a in clinics)
        {
            db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(a.ClinicId, a.Id, AppointmentStatus.Pending, AppointmentStatus.Expired, HistoryActorKind.System, null, null, now));
        }

        await notifier.NotifyAsync(idList, BookingEvent.Expired, now, ct);
        await db.SaveChangesAsync(ct);
        return idList.Count;
    }
}

/// <summary>
/// Worker job (every <c>Booking:SweeperIntervalSeconds</c>): expires overdue Pending requests in batches of
/// <c>Booking:SweeperBatchSize</c>, one transaction per batch, rows claimed with <c>FOR UPDATE SKIP LOCKED</c>
/// so it never waits for (or blocks) a confirm/cancel in progress — a row being confirmed is skipped and the
/// conditional UPDATE of whichever transaction comes second affects 0 rows.
/// </summary>
public sealed class ExpirePendingAppointmentsHandler(ITransactionRunner transactions, BookingSweep sweep, IOptions<BookingOptions> options)
{
    /// <returns>Total requests expired in this run.</returns>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var batch = Math.Max(1, options.Value.SweeperBatchSize);
        var total = 0;
        while (true)
        {
            var result = await transactions.RunAsync<int>(async token => await sweep.ExpireOverdueAsync(null, [], skipLocked: true, batch, token), ct);
            var count = result.IsSuccess ? result.Value : 0;
            total += count;
            if (count < batch)
            {
                return total;
            }
        }
    }
}
