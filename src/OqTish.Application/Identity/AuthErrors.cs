using OqTish.Application.Common;

namespace OqTish.Application.Identity;

/// <summary>Identity error codes (docs/API.md §9).</summary>
public static class AuthErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Sign-in required.");

    public static readonly Error InvalidCredentials = Error.Unauthorized("auth.invalid_credentials", "Wrong email or password.");

    public static readonly Error TotpRequired = Error.Unauthorized("auth.totp_required", "A TOTP code is required for this account.");

    public static readonly Error TotpInvalid = Error.Unauthorized("auth.totp_invalid", "The TOTP code is invalid.");

    /// <summary>Same code as <see cref="TotpInvalid"/> but 400: used by /me endpoints so the SPA does not treat it as signed out.</summary>
    public static readonly Error TotpInvalidForSignedIn = Error.Validation("auth.totp_invalid", "The TOTP code is invalid.");

    public static readonly Error TotpLocked = Error.RateLimited("auth.totp_locked", "Too many wrong TOTP codes. Try again later.");

    public static readonly Error TotpSetupRequired = Error.Forbidden("auth.totp_setup_required", "Enable two-factor authentication (TOTP) to use this role.");

    public static readonly Error TotpAlreadyEnabled = Error.Conflict("auth.totp_already_enabled", "TOTP is already enabled.");

    public static readonly Error TotpNotEnabled = Error.Conflict("auth.totp_not_enabled", "TOTP is not enabled.");

    public static readonly Error OtpInvalid = Error.Validation("auth.otp_invalid", "The code is invalid.");

    public static readonly Error OtpExpired = Error.Validation("auth.otp_expired", "The code has expired. Request a new one.");

    public static readonly Error OtpAttemptsExceeded = Error.Validation("auth.otp_attempts_exceeded", "Too many attempts. Request a new code.");

    public static readonly Error IdentityInUse = Error.Conflict("auth.identity_in_use", "This identifier is already linked to another account.");

    public static readonly Error AccountSuspended = Error.Forbidden("auth.account_suspended", "The account is suspended.");

    public static readonly Error TelegramInvalid = Error.Unauthorized("auth.telegram_invalid", "The Telegram sign-in data is invalid or too old.");

    public static readonly Error TelegramDisabled = Error.Rule("telegram.disabled", "Telegram integration is not enabled.");

    public static readonly Error CurrentPasswordInvalid = Error.Validation("auth.current_password_invalid", "The current password is wrong.");

    public static readonly Error PhoneVerificationRequired = Error.Rule("auth.phone_verification_required", "Verify a phone number before setting a password.");

    public static readonly Error DeleteBlocked = Error.Rule("account.delete_blocked", "Leave all clinics and give up platform roles before deleting the account.");

    public static Error RateLimited(TimeSpan? retryAfter) =>
        Error.RateLimited("auth.rate_limited", "Too many requests. Try again later.", retryAfter);
}
