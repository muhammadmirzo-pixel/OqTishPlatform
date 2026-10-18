namespace OqTish.Application.Identity;

/// <summary>Configuration section <c>Auth</c>. Defaults implement SECURITY.md §1/§4.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public OtpOptions Otp { get; set; } = new();

    public SessionOptions Session { get; set; } = new();

    public PasswordOptions Password { get; set; } = new();

    public TotpLockoutOptions Totp { get; set; } = new();

    /// <summary>Lifetime of a Telegram deep-link token.</summary>
    public int TelegramLinkTokenMinutes { get; set; } = 10;
}

public sealed class OtpOptions
{
    public int CodeLength { get; set; } = 6;

    public int TtlSeconds { get; set; } = 300;

    public int MaxAttempts { get; set; } = 5;

    /// <summary>Minimum delay between two codes for the same phone and purpose.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;

    /// <summary>Codes per phone per <see cref="WindowMinutes"/>.</summary>
    public int MaxPerPhone { get; set; } = 3;

    /// <summary>Codes per client IP per <see cref="WindowMinutes"/>.</summary>
    public int MaxPerIp { get; set; } = 10;

    public int WindowMinutes { get; set; } = 15;

    /// <summary>SMS text; <c>{code}</c> is replaced. Eskiz requires the template to be approved.</summary>
    public string MessageTemplate { get; set; } = "OqTish tasdiqlash kodi: {code}. Kodni hech kimga bermang.";
}

public sealed class SessionOptions
{
    /// <summary>Sliding lifetime: each (throttled) use extends the session and cookie to now + this.</summary>
    public int SlidingLifetimeDays { get; set; } = 30;

    /// <summary>A session unused for this long is dead even if not yet expired.</summary>
    public int IdleTimeoutDays { get; set; } = 14;

    /// <summary>Hard cap from sign-in, regardless of activity (review R3).</summary>
    public int AbsoluteLifetimeDays { get; set; } = 60;

    /// <summary><c>last_seen_at</c> is written at most this often per session.</summary>
    public int TouchIntervalSeconds { get; set; } = 300;

    /// <summary>Require the Secure cookie attribute even over plain HTTP. False only in Development.</summary>
    public bool RequireSecureCookie { get; set; } = true;
}

public sealed class PasswordOptions
{
    public int MinLength { get; set; } = 10;

    public int MaxLength { get; set; } = 128;

    /// <summary>Consecutive failures before the account's password sign-in is locked.</summary>
    public int MaxFailedAttempts { get; set; } = 10;

    public int LockoutMinutes { get; set; } = 15;
}

/// <summary>TOTP brute-force protection shared by every sign-in path (review R2).</summary>
public sealed class TotpLockoutOptions
{
    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;
}
