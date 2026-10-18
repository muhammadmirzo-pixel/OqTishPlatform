using Microsoft.EntityFrameworkCore;

namespace OqTish.Application.Common;

public static class PagingExtensions
{
    /// <summary>Counts and pages in SQL. The query must already be ordered deterministically.</summary>
    public static async Task<Page<T>> ToPageAsync<T>(this IQueryable<T> orderedQuery, PageRequest page, CancellationToken ct)
    {
        var total = await orderedQuery.CountAsync(ct);
        var items = await orderedQuery.Skip(page.Skip).Take(page.Size).ToListAsync(ct);
        return new Page<T>(items, page.Number, page.Size, total);
    }
}
