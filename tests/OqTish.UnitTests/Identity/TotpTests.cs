using System.Text;
using OqTish.Application.Identity;

namespace OqTish.UnitTests.Identity;

public class TotpTests
{
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    // RFC 6238 Appendix B, SHA1 column (8 digits).
    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Matches_rfc6238_sha1_vectors(long unixTime, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixTime));
        Assert.Equal(expected, Totp.Compute(RfcSecret, step, digits: 8));
        Assert.Equal(expected[^6..], Totp.Compute(RfcSecret, step));
    }

    [Fact]
    public void Accepts_adjacent_steps_and_reports_the_matched_step()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111111);
        var step = Totp.StepAt(now);
        Assert.Equal(step, Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step), now, null));
        Assert.Equal(step - 1, Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step - 1), now, null));
        Assert.Equal(step + 1, Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step + 1), now, null));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step + 2), now, null));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step - 2), now, null));
    }

    [Fact]
    public void Replay_of_used_or_older_step_is_rejected()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = Totp.StepAt(now);
        var code = Totp.Compute(RfcSecret, step);
        Assert.Null(Totp.Verify(RfcSecret, code, now, lastUsedStep: step));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step - 1), now, lastUsedStep: step - 1));
        Assert.Equal(step, Totp.Verify(RfcSecret, code, now, lastUsedStep: step - 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    public void Malformed_codes_fail(string? code) => Assert.Null(Totp.Verify(RfcSecret, code, DateTimeOffset.UtcNow, null));

    [Fact]
    public void Base32_round_trips_and_matches_rfc4648()
    {
        Assert.Equal("MZXW6YTBOI", Base32.Encode("foobar"u8));
        Assert.Equal("foobar"u8.ToArray(), Base32.Decode("mzxw6ytboi======"));
        var secret = Totp.GenerateSecret();
        Assert.Equal(secret, Base32.Decode(Base32.Encode(secret)));
        Assert.Null(Base32.Decode("not*base32"));
    }

    [Fact]
    public void Otpauth_uri_has_standard_parameters()
    {
        var uri = Totp.BuildUri("OqTish", "admin@oqtish.local", "ABC");
        Assert.Equal("otpauth://totp/OqTish:admin%40oqtish.local?secret=ABC&issuer=OqTish&algorithm=SHA1&digits=6&period=30", uri);
    }
}
