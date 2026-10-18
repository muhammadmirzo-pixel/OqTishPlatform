using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace OqTish.Application.Identity;

/// <summary>
/// RFC 6238 TOTP: HMAC-SHA1, 30-second steps, 6 digits (configurable for test vectors). Verification
/// accepts the current step ±1 and reports the matched step so callers can reject replays
/// (a step may be used at most once, and never a step older than the last used one).
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int DefaultDigits = 6;
    public const int SecretBytes = 20;

    public static byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>RFC 4226 HOTP value for a counter (dynamic truncation).</summary>
    public static string Compute(ReadOnlySpan<byte> secret, long step, int digits = DefaultDigits)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // HMAC-SHA1 is mandated by RFC 6238 / authenticator apps; HMAC use is not affected by SHA-1 collisions.
        HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        var modulo = 1;
        for (var i = 0; i < digits; i++)
        {
            modulo *= 10;
        }

        return (binary % modulo).ToString(new string('0', digits), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Checks <paramref name="code"/> against steps now-1, now, now+1. Returns the matching step, or null.
    /// Steps at or below <paramref name="lastUsedStep"/> are never accepted (replay protection).
    /// </summary>
    public static long? Verify(ReadOnlySpan<byte> secret, string? code, DateTimeOffset now, long? lastUsedStep, int digits = DefaultDigits)
    {
        if (code is null)
        {
            return null;
        }

        code = code.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (code.Length != digits || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var current = StepAt(now);
        long? match = null;
        for (var step = current - 1; step <= current + 1; step++)
        {
            var expected = Compute(secret, step, digits);
            // Evaluate every window step (no early exit) to keep timing independent of the match position.
            if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(code))
                && (lastUsedStep is null || step > lastUsedStep))
            {
                match ??= step;
            }
        }

        return match;
    }

    /// <summary><c>otpauth://totp/OqTish:label?secret=…&amp;issuer=OqTish&amp;algorithm=SHA1&amp;digits=6&amp;period=30</c></summary>
    public static string BuildUri(string issuer, string accountLabel, string base32Secret)
    {
        var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(accountLabel);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={DefaultDigits}&period={StepSeconds}";
    }
}
