using OqTish.Application.Identity;

namespace OqTish.UnitTests.Identity;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+998901234567")]
    [InlineData("+998 90 123-45-67")]
    [InlineData("998901234567")]
    [InlineData("00998901234567")]
    [InlineData("(90) 123 45 67")]
    [InlineData("901234567")]
    [InlineData(" 90.123.45.67 ")]
    public void Accepts_common_formats(string input)
    {
        Assert.True(PhoneNumber.TryNormalize(input, out var e164));
        Assert.Equal("+998901234567", e164);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("+7 900 123 45 67")]
    [InlineData("+99890123456")]
    [InlineData("+9989012345678")]
    [InlineData("012345678")]
    [InlineData("90123456a")]
    [InlineData("+998+901234567")]
    public void Rejects_non_uzbek_or_malformed(string? input) => Assert.False(PhoneNumber.TryNormalize(input, out _));

    [Theory]
    [InlineData("20")]
    [InlineData("33")]
    [InlineData("50")]
    [InlineData("55")]
    [InlineData("71")]
    [InlineData("75")]
    [InlineData("77")]
    [InlineData("88")]
    [InlineData("90")]
    [InlineData("91")]
    [InlineData("92")]
    [InlineData("93")]
    [InlineData("94")]
    [InlineData("95")]
    [InlineData("97")]
    [InlineData("98")]
    [InlineData("99")]
    public void Accepts_known_operator_codes(string code)
    {
        Assert.True(PhoneNumber.TryNormalize("+998" + code + "1234567", out var e164));
        Assert.Equal("+998" + code + "1234567", e164);
        Assert.True(PhoneNumber.TryNormalize(code + "1234567", out _));
    }

    [Theory]
    [InlineData("10")]
    [InlineData("12")]
    [InlineData("21")]
    [InlineData("30")]
    [InlineData("44")]
    [InlineData("60")]
    [InlineData("66")]
    [InlineData("70")]
    [InlineData("78")]
    [InlineData("80")]
    [InlineData("89")]
    [InlineData("96")]
    public void Rejects_unknown_operator_codes(string code)
    {
        Assert.False(PhoneNumber.TryNormalize("+998" + code + "1234567", out _));
        Assert.False(PhoneNumber.TryNormalize("998" + code + "1234567", out _));
        Assert.False(PhoneNumber.TryNormalize("00998" + code + "1234567", out _));
        Assert.False(PhoneNumber.TryNormalize(code + "1234567", out _));
    }

    [Fact]
    public void Mask_shows_only_last_four_digits() => Assert.Equal("+998*****4567", PhoneNumber.Mask("+998901234567"));
}
