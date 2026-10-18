using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Application.Common;
using OqTish.Domain.Audit;
using OqTish.Domain.Identity;
using OqTish.Domain.Tenancy;
using OqTish.Infrastructure.Persistence;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Common;

/// <summary>ITransactionRunner, IRowLocks, IOutbox, IAuditLog, IIdempotencyStore against real PostgreSQL.</summary>
public class CommonPortsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record Reply(Guid Id, string Value);

    private static async Task<T> InScopeAsync<T>(ApiFactory factory, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static async Task<Guid> NewUserAsync(ApiFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var user = User.Create("Port test", DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }

    [Fact]
    public async Task Failure_result_rolls_back_and_success_commits_with_audit_and_outbox()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        _ = factory.CreateApiClient();
        var failed = await InScopeAsync(factory, async sp =>
        {
            var db = sp.GetRequiredService<IOqTishDb>();
            return await sp.GetRequiredService<ITransactionRunner>().RunAsync(async ct =>
            {
                sp.GetRequiredService<IAuditLog>().Record(new AuditEvent("test.rolled_back"));
                await sp.GetRequiredService<IOutbox>().EnqueueAsync("test.msg", "dedup:rollback", new { a = 1 }, null, ct);
                await db.SaveChangesAsync(ct);
                return Result.Failure(CommonErrors.Forbidden);
            }, Ct);
        });
        Assert.False(failed.IsSuccess);

        var ok = await InScopeAsync(factory, async sp =>
        {
            var db = sp.GetRequiredService<IOqTishDb>();
            var outbox = sp.GetRequiredService<IOutbox>();
            return await sp.GetRequiredService<ITransactionRunner>().RunAsync<bool>(async ct =>
            {
                sp.GetRequiredService<IAuditLog>().Record(new AuditEvent("test.committed", Details: new { n = 1 }));
                var first = await outbox.EnqueueAsync("test.msg", "dedup:1", new { appointmentId = Guid.Empty }, null, ct);
                var dup = await outbox.EnqueueAsync("test.msg", "dedup:1", new { appointmentId = Guid.Empty }, null, ct);
                await db.SaveChangesAsync(ct);
                return first && !dup;
            }, Ct);
        });
        Assert.True(ok.Value);

        await using var check = factory.CreateDbContext();
        Assert.False(await check.AuditLog.AnyAsync(a => a.Action == "test.rolled_back", Ct));
        Assert.False(await check.OutboxMessages.AnyAsync(m => m.DedupKey == "dedup:rollback", Ct));
        var audit = await check.AuditLog.SingleAsync(a => a.Action == "test.committed", Ct);
        Assert.Equal(AuditActorKind.System, audit.ActorKind);
        var msg = await check.OutboxMessages.SingleAsync(m => m.DedupKey == "dedup:1", Ct);
        Assert.Contains("appointmentId", msg.Payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    public async Task Transient_errors_are_retried_three_times_then_concurrency(string sqlState)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        _ = factory.CreateApiClient();
        var attempts = 0;
        var result = await InScopeAsync(factory, sp => sp.GetRequiredService<ITransactionRunner>().RunAsync(async ct =>
        {
            attempts++;
            var db = sp.GetRequiredService<OqTishDbContext>();
            var sql = "DO $$ BEGIN RAISE EXCEPTION 'forced' USING ERRCODE = '" + sqlState + "'; END $$;";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
            return Result.Success;
        }, Ct));

        Assert.Equal(3, attempts);
        Assert.Equal("common.concurrency", result.Error!.Code);
    }

    [Fact]
    public async Task Non_transient_errors_are_not_retried()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        _ = factory.CreateApiClient();
        var attempts = 0;
        await Assert.ThrowsAsync<PostgresException>(() => InScopeAsync(factory, sp => sp.GetRequiredService<ITransactionRunner>().RunAsync(async ct =>
        {
            attempts++;
            await sp.GetRequiredService<OqTishDbContext>().Database.ExecuteSqlRawAsync("DO $$ BEGIN RAISE EXCEPTION 'x' USING ERRCODE = '23505'; END $$;", ct);
            return Result.Success;
        }, Ct)));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Error_classifier_reports_constraint_names()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        _ = factory.CreateApiClient();
        var user = User.Create(null, DateTimeOffset.UtcNow);
        await using var db = factory.CreateDbContext();
        db.Users.Add(user);
        db.AuthIdentities.Add(AuthIdentity.Create(user.Id, AuthIdentityKind.Phone, "+998901111111", true, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Ct);
        var other = User.Create(null, DateTimeOffset.UtcNow);
        db.Users.Add(other);
        db.AuthIdentities.Add(AuthIdentity.Create(other.Id, AuthIdentityKind.Phone, "+998901111111", true, DateTimeOffset.UtcNow));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        var error = factory.Services.GetRequiredService<IDbErrorClassifier>().Classify(ex);
        Assert.Equal(DbErrorKind.UniqueViolation, error!.Kind);
        Assert.Equal("ix_auth_identities_kind_normalized_identifier", error.ConstraintName);
    }

    [Fact]
    public async Task Row_locks_need_a_transaction_and_branch_lock_rows_exist()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var clinic = await CreateClinicAsync(factory, 1);
        await using var scope = factory.Services.CreateAsyncScope();
        var locks = scope.ServiceProvider.GetRequiredService<IRowLocks>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => locks.LockBranchAsync(clinic.BranchIds[0], Ct));

        var result = await scope.ServiceProvider.GetRequiredService<ITransactionRunner>().RunAsync<(bool, bool, Clinic?)>(async ct =>
            (await locks.LockBranchAsync(clinic.BranchIds[0], ct),
             await locks.LockBranchAsync(Guid.NewGuid(), ct),
             await locks.LoadForUpdateAsync<Clinic>(clinic.ClinicId, ct)), Ct);
        Assert.True(result.Value.Item1);
        Assert.False(result.Value.Item2);
        Assert.Equal(clinic.ClinicId, result.Value.Item3!.Id);
    }

    [Fact]
    public async Task Branch_lock_blocks_a_concurrent_transaction()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var clinic = await CreateClinicAsync(factory, 1);
        var branch = clinic.BranchIds[0];
        await using var holder = factory.Services.CreateAsyncScope();
        var gate = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var holding = holder.ServiceProvider.GetRequiredService<ITransactionRunner>().RunAsync(async ct =>
        {
            await holder.ServiceProvider.GetRequiredService<IRowLocks>().LockBranchAsync(branch, ct);
            gate.SetResult();
            await release.Task;
            return Result.Success;
        }, Ct);
        await gate.Task;

        await using (var conn = new NpgsqlConnection(factory.ConnectionString))
        {
            await conn.OpenAsync(Ct);
            await using var cmd = new NpgsqlCommand($"SET lock_timeout = '200ms'; BEGIN; SELECT 1 FROM tenancy.branch_booking_locks WHERE branch_id = '{branch}' FOR UPDATE;", conn);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync(Ct));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, ex.SqlState);
        }

        release.SetResult();
        Assert.True((await holding).IsSuccess);
    }

    [Fact]
    public async Task Idempotency_replays_detects_mismatch_and_serialises_concurrent_duplicates()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        _ = factory.CreateApiClient();
        var userId = await NewUserAsync(factory);
        var runs = 0;

        async Task<Result<Reply>> CallAsync(object request, string key) => await InScopeAsync(factory, sp =>
            sp.GetRequiredService<ITransactionRunner>().RunAsync<Reply>(ct => sp.GetRequiredService<IIdempotencyStore>().RunAsync<Reply>(userId, key, request, async inner =>
            {
                Interlocked.Increment(ref runs);
                await Task.Delay(100, inner);
                return new Reply(Guid.NewGuid(), "created");
            }, ct), Ct));

        var first = await CallAsync(new { branch = 1 }, "key-1");
        var replay = await CallAsync(new { branch = 1 }, "key-1");
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(1, runs);

        var mismatch = await CallAsync(new { branch = 2 }, "key-1");
        Assert.Equal("common.idempotency_mismatch", mismatch.Error!.Code);

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CallAsync(new { branch = 3 }, "key-2")));
        Assert.All(concurrent, r => Assert.True(r.IsSuccess));
        Assert.Single(concurrent.Select(r => r.Value.Id).Distinct());
        Assert.Equal(2, runs);

        var badKey = await CallAsync(new { branch = 1 }, new string('k', 65));
        Assert.Equal("common.validation", badKey.Error!.Code);
    }
}
