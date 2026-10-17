using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OqTish.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Adding migrations needs no live database; commands that do
/// (database update, bundle execution) take the connection string from OQTISH_MIGRATIONS_CONNECTION.
/// No credentials are embedded here.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OqTishDbContext>
{
    public OqTishDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("OQTISH_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=oqtish";

        var options = new DbContextOptionsBuilder<OqTishDbContext>();
        DependencyInjection.ConfigureDbContext(options, connectionString);
        return new OqTishDbContext(options.Options);
    }
}
