using System.Globalization;

namespace OqTish.Application.Common;

/// <summary>Collects field errors for one request and turns them into 400 <c>common.validation</c>.</summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
    }

    public Error ToError() => CommonErrors.Validation(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));

    /// <summary>Trimmed text; required → 1..max chars, optional → null when blank.</summary>
    public string? Text(string? value, string field, int maxLength, bool required)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (required)
            {
                Add(field, "Required.");
            }

            return null;
        }

        if (trimmed.Length > maxLength)
        {
            Add(field, $"Must be at most {maxLength} characters.");
            return null;
        }

        return trimmed;
    }

    /// <summary>Local wall-clock time in the exact form <c>HH:mm</c>.</summary>
    public TimeOnly? Time(string? value, string field)
    {
        if (value is not null && TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        Add(field, "Use the local time format HH:mm.");
        return null;
    }
}

/// <summary>Wire formats for local values (CONVENTIONS §5).</summary>
public static class LocalFormats
{
    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
