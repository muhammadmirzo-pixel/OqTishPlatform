using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Identity;

/// <summary>GET /api/me (docs/API.md §0 <c>Me</c>).</summary>
public sealed record MeDto(
    Guid Id,
    string DisplayName,
    bool PhoneVerified,
    string? PhoneMasked,
    string? Email,
    bool TelegramLinked,
    bool TotpEnabled,
    bool TotpSetupRequired,
    PatientDto? Patient,
    IReadOnlyList<MembershipDto> Memberships,
    IReadOnlyList<string> PlatformPermissions,
    Guid? AvatarId);

public sealed record PatientDto(string FullName, string? ContactPhone);

public sealed record MembershipDto(
    Guid MembershipId,
    Guid ClinicId,
    string ClinicName,
    ClinicVerificationStatus ClinicStatus,
    StaffRole Role,
    MembershipScope Scope,
    IReadOnlyList<Guid> BranchIds,
    IReadOnlyList<string> Permissions,
    bool TotpSetupRequired,
    Guid? ClinicLogoId);

/// <summary>Result of a successful sign-in: the API sets the session cookie and returns <see cref="Me"/>.</summary>
public sealed record SignInOutcome(MeDto Me, string SessionToken, DateTimeOffset ExpiresAt);

/// <summary>202 body of OTP requests. Identical for known and unknown numbers.</summary>
public sealed record OtpRequestedDto(Guid ChallengeId, int ExpiresInSeconds, int ResendAfterSeconds);

/// <summary>202 body of POST /auth/password/reset/request. Always carries a challenge id.</summary>
public sealed record PasswordResetRequestedDto(Guid ChallengeId);

public sealed record SessionDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    string AuthMethod,
    string? UserAgent,
    bool Current);

public sealed record TotpSetupDto(string Secret, string OtpauthUri);

public sealed record TelegramLinkDto(string DeepLink, DateTimeOffset ExpiresAt);

/// <summary><c>GET /api/telegram/config</c>. <see cref="BotId"/> (BE4, additive) = numeric bot id for the Login Widget popup (FRONTEND §9.2).</summary>
public sealed record TelegramConfigDto(bool Enabled, string? BotUsername, string? MiniAppUrl, long? BotId = null);

public static class AuthMethods
{
    public const string Otp = "Otp";
    public const string Password = "Password";
    public const string TelegramWebApp = "TelegramWebApp";
    public const string TelegramWidget = "TelegramWidget";
}
