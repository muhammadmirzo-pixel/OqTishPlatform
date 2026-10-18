using System.Text.Json.Serialization;

namespace OqTish.Application.Common;

/// <summary>Standard list response <c>{ items, page, pageSize, totalCount }</c> (CONVENTIONS §5).</summary>
public sealed record Page<T>(
    IReadOnlyList<T> Items,
    [property: JsonPropertyName("page")] int PageNumber,
    int PageSize,
    int TotalCount);

/// <summary>Paging input: <c>?page=1&amp;pageSize=20</c>, page ≥ 1, page size 1..100.</summary>
public readonly record struct PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private PageRequest(int page, int pageSize)
    {
        Number = page;
        Size = pageSize;
    }

    public int Number { get; }

    public int Size { get; }

    public int Skip => (Number - 1) * Size;

    /// <summary>Validates raw query values (null = default). Out-of-range values are a validation error, not clamped.</summary>
    public static Result<PageRequest> Create(int? page, int? pageSize)
    {
        var p = page ?? 1;
        var s = pageSize ?? DefaultPageSize;
        var errors = new Dictionary<string, string[]>();
        if (p < 1)
        {
            errors["page"] = ["Must be 1 or greater."];
        }

        if (s is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        }

        return errors.Count > 0 ? CommonErrors.Validation(errors) : new PageRequest(p, s);
    }
}
