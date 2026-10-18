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

    [Fact]
    public void Mask_shows_only_last_four_digits() => Assert.Equal("+998*****4567", PhoneNumber.Mask("+998901234567"));
}
