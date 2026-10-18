using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Infrastructure.Common;

/// <summary><c>SELECT clock_timestamp()</c> on the request's connection (inside its transaction when one is open).</summary>
internal sealed class DbClock(OqTishDbContext db) : IDbClock
{
    public async Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").ToListAsync(cancellationToken);
        return rows[0].ToUniversalTime();
    }
}
