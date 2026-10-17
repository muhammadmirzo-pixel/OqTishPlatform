using Microsoft.EntityFrameworkCore;

namespace OqTish.Infrastructure.Persistence;

/// <summary>
/// Single DbContext for the modular monolith (ADR-002). Each module maps its tables into its own
/// PostgreSQL schema; cross-module writes share one transaction (booking + outbox + ledger).
/// </summary>
public sealed class OqTishDbContext(DbContextOptions<OqTishDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // btree_gist: exclusion constraints on (doctor_id, period) for booking (ADR-004).
        // citext: case-insensitive email identifiers.
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OqTishDbContext).Assembly);
    }
}
