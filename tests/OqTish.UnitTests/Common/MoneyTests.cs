using System.Text.Json;
using OqTish.Application.Common.Json;
using OqTish.Domain.Common;

namespace OqTish.UnitTests.Common;

public class MoneyTests
{
    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("1.004", "1.00")]
    [InlineData("-1.005", "-1.01")]
    [InlineData("2.5", "2.50")]
    public void Round_is_two_decimals_away_from_zero(string input, string expected)
    {
        var money = Money.Uzs(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)).Round();
        Assert.Equal(expected, money.ToAmountString());
    }

    [Fact]
    public void Arithmetic_and_comparison_work_in_one_currency()
    {
        var a = Money.Uzs(1500000m);
        var b = Money.Uzs(250000.50m);
        Assert.Equal(Money.Uzs(1750000.50m), a + b);
        Assert.Equal(Money.Uzs(1249999.50m), a - b);
        Assert.True(a > b);
        Assert.Equal(Money.Uzs(150000.00m), a.Multiply(0.1m).Round());
    }

    [Fact]
    public void Amount_beyond_numeric_19_2_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Uzs(Money.MaxAbsoluteAmount + 1));

    [Fact]
    public void Exact_creation_rejects_more_than_two_decimals()
    {
        Assert.False(Money.TryCreateExact(1.001m, Currency.UZS, out _));
        Assert.True(Money.TryCreateExact(1.10m, Currency.UZS, out var m));
        Assert.Equal("1.10", m.ToAmountString());
    }

    [Fact]
    public void Serializes_amount_as_string_with_two_decimals()
    {
        var json = JsonSerializer.Serialize(new { price = Money.Uzs(1500000m) }, JsonDefaults.Options);
        Assert.Equal("{\"price\":{\"amount\":\"1500000.00\",\"currency\":\"UZS\"}}", json);
    }

    [Theory]
    [InlineData("{\"amount\":\"1500000.00\",\"currency\":\"UZS\"}", "1500000.00")]
    [InlineData("{\"amount\":1500000.5,\"currency\":\"UZS\"}", "1500000.50")]
    [InlineData("{\"currency\":\"UZS\",\"amount\":\"12\"}", "12.00")]
    public void Parses_valid_money(string json, string expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<Money>(json, JsonDefaults.Options).ToAmountString());

    [Theory]
    [InlineData("{\"amount\":\"1.001\",\"currency\":\"UZS\"}")]
    [InlineData("{\"amount\":1.001,\"currency\":\"UZS\"}")]
    [InlineData("{\"amount\":1e3,\"currency\":\"UZS\"}")]
    [InlineData("{\"amount\":\"abc\",\"currency\":\"UZS\"}")]
    [InlineData("{\"amount\":\"10\",\"currency\":\"USD\"}")]
    [InlineData("{\"amount\":\"10\"}")]
    [InlineData("{\"amount\":\"10\",\"currency\":\"UZS\",\"extra\":1}")]
    [InlineData("\"10.00\"")]
    public void Rejects_invalid_money_with_money_format_exception(string json) =>
        Assert.Throws<MoneyFormatException>(() => JsonSerializer.Deserialize<Money>(json, JsonDefaults.Options));
}
