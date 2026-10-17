using Microsoft.EntityFrameworkCore;
using Npgsql;
using OqTish.IntegrationTests.Infrastructure;
using Testcontainers.PostgreSql;

namespace OqTish.IntegrationTests.Persistence;

/// <summary>
/// Runs the real deploy/postgres/init script in a fresh container and verifies the least-privilege
/// role layout (docs/DATABASE.md §3): the app roles get DML on migrated objects but no DDL.
/// </summary>
public sealed class DatabaseRoleTests : IAsyncLifetime
{
    private const string OwnerPassword = "test-owner-pw";
    private const string ApiPassword = "test-api-pw";
    private const string WorkerPassword = "test-worker-pw";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresFixture.Image)
        .WithEnvironment("OQTISH_DB_OWNER_PASSWORD", OwnerPassword)
        .WithEnvironment("OQTISH_DB_API_PASSWORD", ApiPassword)
        .WithEnvironment("OQTISH_DB_WORKER_PASSWORD", WorkerPassword)
        .WithResourceMapping(new FileInfo(RepoPath("deploy/postgres/init/01-create-roles.sh")), "/docker-entrypoint-initdb.d/")
        .Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task App_roles_get_dml_on_owner_objects_but_no_ddl()
    {
        var ct = TestContext.Current.CancellationToken;

        // Migrations run as the owner, exactly like the migrate container.
        await using (var db = PostgresFixture.CreateDbContext(ConnectionFor("oqtish_owner", OwnerPassword)))
        {
            await db.Database.MigrateAsync(ct);
        }

        // Simulates a table created by a future migration in a module schema.
        await ExecAsync("oqtish_owner", OwnerPassword, "CREATE SCHEMA probe; CREATE TABLE probe.items (id int PRIMARY KEY)", ct);

        foreach (var (user, password) in new[] { ("oqtish_api", ApiPassword), ("oqtish_worker", WorkerPassword) })
        {
            await ExecAsync(user, password, $"INSERT INTO probe.items VALUES ({(user == "oqtish_api" ? 1 : 2)})", ct);
            await ExecAsync(user, password, "SELECT count(*) FROM probe.items", ct);

            await AssertDeniedAsync(user, password, "CREATE TABLE public.sneaky (id int)", ct);
            await AssertDeniedAsync(user, password, "CREATE TABLE probe.sneaky (id int)", ct);
            await AssertDeniedAsync(user, password, "CREATE SCHEMA sneaky", ct);
            await AssertDeniedAsync(user, password, "DROP TABLE probe.items", ct);
            await AssertDeniedAsync(user, password, "ALTER TABLE probe.items ADD COLUMN x int", ct);
        }
    }

    private string ConnectionFor(string user, string password) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "oqtish",
            Username = user,
            Password = password,
        }.ConnectionString;

    private async Task ExecAsync(string user, string password, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ConnectionFor(user, password));
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task AssertDeniedAsync(string user, string password, string sql, CancellationToken ct)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(user, password, sql, ct));
        // 42501 insufficient_privilege: "permission denied" and "must be owner" both map to it.
        Assert.True(ex.SqlState is PostgresErrorCodes.InsufficientPrivilege,
            $"{user}: expected permission denied for `{sql}`, got {ex.SqlState} {ex.MessageText}");
    }

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OqTish.slnx")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new InvalidOperationException("Repository root (OqTish.slnx) not found.")
            : Path.Combine(dir.FullName, relative);
    }
}
