namespace OqTish.Application.Identity;

/// <summary>
/// Uzbekistan phone numbers only: E.164 <c>+998</c> followed by 9 digits. Accepts common input forms:
/// <c>+998 90 123-45-67</c>, <c>998901234567</c>, <c>00998901234567</c>, <c>(90) 123 45 67</c>,
/// <c>901234567</c>. Spaces, dashes, dots and parentheses are ignored; anything else is rejected.
/// </summary>
public static class PhoneNumber
{
    public const string CountryCode = "998";
    private const int NationalLength = 9;

    public static bool TryNormalize(string? input, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 32)
        {
            return false;
        }

        var trimmed = input.Trim();
        var hasPlus = trimmed.StartsWith('+');
        Span<char> digits = stackalloc char[32];
        var count = 0;
        for (var i = hasPlus ? 1 : 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (char.IsAsciiDigit(c))
            {
                digits[count++] = c;
            }
            else if (c is not (' ' or '-' or '.' or '(' or ')'))
            {
                return false;
            }
        }

        var value = new string(digits[..count]);
        if (hasPlus)
        {
            // International form must be +998 + 9 digits.
            if (value.Length != CountryCode.Length + NationalLength || !value.StartsWith(CountryCode, StringComparison.Ordinal))
            {
                return false;
            }

            value = value[CountryCode.Length..];
        }
        else if (value.Length == 2 + CountryCode.Length + NationalLength && value.StartsWith("00" + CountryCode, StringComparison.Ordinal))
        {
            value = value[(2 + CountryCode.Length)..];
        }
        else if (value.Length == CountryCode.Length + NationalLength && value.StartsWith(CountryCode, StringComparison.Ordinal))
        {
            value = value[CountryCode.Length..];
        }
        else if (value.Length != NationalLength)
        {
            return false;
        }

        // National numbers never start with 0 (operator/area codes are 20-99).
        if (value[0] == '0')
        {
            return false;
        }

        e164 = "+" + CountryCode + value;
        return true;
    }

    /// <summary><c>+998*****4567</c> — the only form of a phone that may appear in logs or UI lists.</summary>
    public static string Mask(string e164)
    {
        ArgumentNullException.ThrowIfNull(e164);
        return e164.Length < 8 ? "***" : "+" + CountryCode + "*****" + e164[^4..];
    }

    /// <summary>Digits without the plus sign (<c>998901234567</c>), as SMS gateways expect.</summary>
    public static string DigitsOnly(string e164)
    {
        ArgumentNullException.ThrowIfNull(e164);
        return e164.TrimStart('+');
    }
}
