using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Domain.Scheduling;
using OqTish.Infrastructure.Persistence;
using OqTish.Infrastructure.Persistence.Configurations;

namespace OqTish.Infrastructure.Booking;

/// <summary>
/// See <see cref="IBookingStore"/>. Block writes run under a SAVEPOINT so that an exclusion violation
/// (23P01 on <c>ex_doctor_time_blocks_no_overlap</c>) can be rolled back without aborting the booking
/// transaction; anything else (including 40P01/40001) propagates to <c>ITransactionRunner</c>.
/// </summary>
internal sealed class BookingStore(OqTishDbContext db, IDbErrorClassifier classifier) : IBookingStore
{
    private const string Savepoint = "booking_block";

    public async Task<bool> TryInsertBlockAsync(DoctorTimeBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);
        return await UnderSavepointAsync(() => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO scheduling.doctor_time_blocks
                (id, clinic_id, branch_id, doctor_id, starts_at, ends_at, kind, appointment_id, is_active, reason, created_by_user_id, created_at, updated_at)
            VALUES (@id, @clinic, @branch, @doctor, @starts, @ends, @kind, @appointment, TRUE, @reason, @createdBy, @created, @created)
            """,
            [
                new NpgsqlParameter("id", block.Id),
                new NpgsqlParameter("clinic", block.ClinicId),
                Nullable("branch", block.BranchId),
                new NpgsqlParameter("doctor", block.DoctorId),
                new NpgsqlParameter("starts", NpgsqlDbType.TimestampTz) { Value = block.StartsAt.ToUniversalTime() },
                new NpgsqlParameter("ends", NpgsqlDbType.TimestampTz) { Value = block.EndsAt.ToUniversalTime() },
                new NpgsqlParameter("kind", block.Kind.ToString()),
                Nullable("appointment", block.AppointmentId),
                new NpgsqlParameter("reason", NpgsqlDbType.Varchar) { Value = (object?)block.Reason ?? DBNull.Value },
                Nullable("createdBy", block.CreatedByUserId),
                new NpgsqlParameter("created", NpgsqlDbType.TimestampTz) { Value = block.CreatedAt.ToUniversalTime() },
            ],
            cancellationToken)) is not null;
    }

    public async Task<BlockMoveResult> TryMoveBlockAsync(Guid appointmentId, Guid doctorId, DateTimeOffset startsAt, DateTimeOffset endsAt, TimeBlockKind kind, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await UnderSavepointAsync(() => db.Database.ExecuteSqlRawAsync(
            """
            UPDATE scheduling.doctor_time_blocks
               SET doctor_id = @doctor, starts_at = @starts, ends_at = @ends, kind = @kind, is_active = TRUE, updated_at = @now
             WHERE appointment_id = @appointment
            """,
            [
                new NpgsqlParameter("doctor", doctorId),
                new NpgsqlParameter("starts", NpgsqlDbType.TimestampTz) { Value = startsAt.ToUniversalTime() },
                new NpgsqlParameter("ends", NpgsqlDbType.TimestampTz) { Value = endsAt.ToUniversalTime() },
                new NpgsqlParameter("kind", kind.ToString()),
                new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now.ToUniversalTime() },
                new NpgsqlParameter("appointment", appointmentId),
            ],
            cancellationToken));
        return rows switch
        {
            null => BlockMoveResult.Conflict,
            0 => BlockMoveResult.NotFound,
            _ => BlockMoveResult.Moved,
        };
    }

    public async Task<int> ReleaseBlocksAsync(IReadOnlyCollection<Guid> appointmentIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (appointmentIds.Count == 0)
        {
            return 0;
        }

        EnsureTransaction();
        return await db.Database.ExecuteSqlRawAsync(
            "UPDATE scheduling.doctor_time_blocks SET is_active = FALSE, updated_at = @now WHERE appointment_id = ANY(@ids) AND is_active",
            [
                new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now.ToUniversalTime() },
                new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = appointmentIds.ToArray() },
            ],
            cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> LockOverduePendingAsync(Guid? branchId, IReadOnlyCollection<Guid> doctorIds, bool skipLocked, int limit, CancellationToken cancellationToken, Guid? excludeAppointmentId = null)
    {
        EnsureTransaction();
        var parameters = new List<NpgsqlParameter> { new("limit", Math.Max(1, limit)) };
        var filters = new List<string>();
        if (branchId is { } b)
        {
            filters.Add("a.branch_id = @branch");
            parameters.Add(new NpgsqlParameter("branch", b));
        }

        if (doctorIds.Count > 0)
        {
            filters.Add("EXISTS (SELECT 1 FROM scheduling.doctor_time_blocks t WHERE t.appointment_id = a.id AND t.is_active AND t.doctor_id = ANY(@doctors))");
            parameters.Add(new NpgsqlParameter("doctors", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = doctorIds.ToArray() });
        }

        var where = filters.Count == 0 ? string.Empty : " AND (" + string.Join(" OR ", filters) + ")";
        if (excludeAppointmentId is { } exclude)
        {
            where += " AND a.id <> @exclude";
            parameters.Add(new NpgsqlParameter("exclude", exclude));
        }

        var sql = $"""
            SELECT a.id AS "Value" FROM booking.appointments a
            WHERE a.status = 'Pending' AND a.expires_at <= statement_timestamp(){where}
            ORDER BY a.id
            LIMIT @limit
            FOR UPDATE OF a{(skipLocked ? " SKIP LOCKED" : string.Empty)}
            """;
#pragma warning disable EF1002 // Only fixed fragments are concatenated; values are parameters.
        return await db.Database.SqlQueryRaw<Guid>(sql, [.. parameters]).ToListAsync(cancellationToken);
#pragma warning restore EF1002
    }

    private static NpgsqlParameter Nullable(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = value is { } v ? v : DBNull.Value };

    /// <summary>Runs one statement under a savepoint; returns null when it hit the no-overlap exclusion constraint.</summary>
    private async Task<int?> UnderSavepointAsync(Func<Task<int>> statement)
    {
        var tx = EnsureTransaction();
        await tx.CreateSavepointAsync(Savepoint);
        try
        {
            var rows = await statement();
            await tx.ReleaseSavepointAsync(Savepoint);
            return rows;
        }
        catch (Exception ex) when (classifier.Classify(ex) is { Kind: DbErrorKind.ExclusionViolation, ConstraintName: DoctorTimeBlockConfiguration.NoOverlapConstraint })
        {
            await tx.RollbackToSavepointAsync(Savepoint);
            return null;
        }
    }

    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction EnsureTransaction() =>
        db.Database.CurrentTransaction ?? throw new InvalidOperationException("Booking writes require an explicit transaction (ITransactionRunner).");
}
