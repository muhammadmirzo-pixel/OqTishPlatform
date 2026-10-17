using Microsoft.EntityFrameworkCore;
using Npgsql;
using OqTish.IntegrationTests.Infrastructure;

namespace OqTish.IntegrationTests.Persistence;

public class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task All_migrations_apply_to_a_clean_database()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateEmptyDatabaseAsync(ct);

        await using var db = PostgresFixture.CreateDbContext(connectionString);
        Assert.NotEmpty(db.Database.GetMigrations());

        await db.Database.MigrateAsync(ct);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
    }

    [Fact]
    public async Task Required_postgres_extensions_are_installed_by_migrations()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateMigratedDatabaseAsync(ct);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT extname FROM pg_extension", connection);
        var installed = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                installed.Add(reader.GetString(0));
            }
        }

        Assert.Contains("btree_gist", installed);
        Assert.Contains("citext", installed);
    }

    [Fact]
    public void Model_has_no_changes_missing_from_migrations()
    {
        // Fails when an entity/configuration changed but nobody ran `dotnet ef migrations add`.
        using var db = PostgresFixture.CreateDbContext("Host=unused;Database=unused");
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
