using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OqTish.Domain.Common;

namespace OqTish.Application.Common.Json;

/// <summary>
/// Thrown for a malformed money value in a request body. The API maps it to
/// 400 <c>finance.amount_invalid</c>.
/// </summary>
public sealed class MoneyFormatException : JsonException
{
    public const string ErrorCode = "finance.amount_invalid";

    public MoneyFormatException()
        : base("Invalid money value.")
    {
    }

    public MoneyFormatException(string message)
        : base(message)
    {
    }

    public MoneyFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Wire format <c>{ "amount": "1500000.00", "currency": "UZS" }</c> (CONVENTIONS §5). Writing always
/// emits a string with exactly two decimals. Reading is strict: the amount may be a JSON string or
/// number with at most two decimals and no exponent; the currency must be a supported ISO code; any
/// other property, a missing field or a bad value raises <see cref="MoneyFormatException"/>.
/// </summary>
public sealed partial class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new MoneyFormatException("Money must be an object { amount, currency }.");
        }

        decimal? amount = null;
        Currency? currency = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new MoneyFormatException("Malformed money object.");
            }

            var name = reader.GetString();
            if (!reader.Read())
            {
                throw new MoneyFormatException("Malformed money object.");
            }

            if (string.Equals(name, "amount", StringComparison.OrdinalIgnoreCase))
            {
                amount = ReadAmount(ref reader);
            }
            else if (string.Equals(name, "currency", StringComparison.OrdinalIgnoreCase))
            {
                currency = ReadCurrency(ref reader);
            }
            else
            {
                throw new MoneyFormatException("Unknown money property.");
            }
        }

        if (amount is null || currency is null)
        {
            throw new MoneyFormatException("Money requires amount and currency.");
        }

        return Money.TryCreateExact(amount.Value, currency.Value, out var money)
            ? money
            : throw new MoneyFormatException("Amount is out of range.");
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        writer.WriteString("amount", value.ToAmountString());
        writer.WriteString("currency", value.Currency.ToString());
        writer.WriteEndObject();
    }

    /// <summary>Parses "1500000", "1500000.5", "-12.34" (≤ 17 integer digits, ≤ 2 decimals).</summary>
    public static bool TryParseAmount(string text, out decimal amount)
    {
        amount = 0m;
        return AmountPattern().IsMatch(text)
            && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    private static decimal ReadAmount(ref Utf8JsonReader reader)
    {
        string text = reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
            _ => throw new MoneyFormatException("Amount must be a string or a number."),
        };

        return TryParseAmount(text, out var amount)
            ? amount
            : throw new MoneyFormatException("Amount must have at most 2 decimal places.");
    }

    private static Currency ReadCurrency(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new MoneyFormatException("Currency must be a string.");
        }

        var code = reader.GetString();
        return Enum.TryParse<Currency>(code, ignoreCase: false, out var currency) && Enum.IsDefined(currency) && code == currency.ToString()
            ? currency
            : throw new MoneyFormatException("Unsupported currency.");
    }

    [GeneratedRegex(@"^-?\d{1,17}(\.\d{1,2})?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex AmountPattern();
}
