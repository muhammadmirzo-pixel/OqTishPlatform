using Microsoft.EntityFrameworkCore;
using Npgsql;
using OqTish.Infrastructure;
using OqTish.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(OqTish.IntegrationTests.Infrastructure.PostgresFixture))]
[assembly: Xunit.v3.Parallelization(MaxThreads = 2)]

namespace OqTish.IntegrationTests.Infrastructure;

/// <summary>
/// One real PostgreSQL container per test run (ADR-009: no in-memory providers). Each test gets its own
/// database. Migrated databases are cloned from a template that is migrated once per run
/// (<c>CREATE DATABASE … TEMPLATE</c>), so every test still starts from the real migrations.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18-alpine";
    private const string TemplateName = "oqtish_template";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).WithCommand("-c", "max_connections=300").Build();
    private readonly SemaphoreSlim _cloneLock = new(1, 1);
    private bool _templateReady;

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync()
    {
        _cloneLock.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync(CancellationToken cancellationToken)
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await ExecAdminAsync($"CREATE DATABASE \"{name}\"", cancellationToken);
        return ConnectionStringFor(name);
    }

    /// <summary>Creates a database with all migrations applied (cloned from the migrated template).</summary>
    public async Task<string> CreateMigratedDatabaseAsync(CancellationToken cancellationToken)
    {
        await _cloneLock.WaitAsync(cancellationToken);
        try
        {
            if (!_templateReady)
            {
                await ExecAdminAsync($"CREATE DATABASE \"{TemplateName}\"", cancellationToken);
                var templateConnection = new NpgsqlConnectionStringBuilder(ConnectionStringFor(TemplateName)) { Pooling = false }.ConnectionString;
                await using (var db = CreateDbContext(templateConnection))
                {
                    await db.Database.MigrateAsync(cancellationToken);
                }

                _templateReady = true;
            }

            var name = "t_" + Guid.NewGuid().ToString("N");
            await ExecAdminAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateName}\"", cancellationToken);
            return ConnectionStringFor(name);
        }
        finally
        {
            _cloneLock.Release();
        }
    }

    public static OqTishDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OqTishDbContext>();
        DependencyInjection.ConfigureDbContext(options, connectionString);
        return new OqTishDbContext(options.Options);
    }

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

    private async Task ExecAdminAsync(string sql, CancellationToken ct)
    {
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false }.ConnectionString);
        await admin.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, admin);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
