using Microsoft.EntityFrameworkCore;
using Npgsql;
using OqTish.Infrastructure;
using OqTish.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(OqTish.IntegrationTests.Infrastructure.PostgresFixture))]

namespace OqTish.IntegrationTests.Infrastructure;

/// <summary>
/// One real PostgreSQL container per test run (ADR-009: no in-memory providers). Each test class
/// creates its own database, so tests never share state and migrations always start from empty.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync(CancellationToken cancellationToken)
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync(cancellationToken);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>Creates a database and applies all migrations to it.</summary>
    public async Task<string> CreateMigratedDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = await CreateEmptyDatabaseAsync(cancellationToken);
        await using var db = CreateDbContext(connectionString);
        await db.Database.MigrateAsync(cancellationToken);
        return connectionString;
    }

    public static OqTishDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OqTishDbContext>();
        DependencyInjection.ConfigureDbContext(options, connectionString);
        return new OqTishDbContext(options.Options);
    }
}
