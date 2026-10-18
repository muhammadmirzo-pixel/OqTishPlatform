using System.Net.Mail;
using OqTish.Application.Common;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity;

/// <summary>Input normalisation and validation shared by the identity handlers.</summary>
internal static class IdentifierRules
{
    public const int MaxEmailLength = 254;

    public static Result<string> Phone(string? input, string field = "phone") =>
        PhoneNumber.TryNormalize(input, out var e164)
            ? e164
            : Error.InvalidField(field, "Enter an Uzbekistan mobile number (+998 and 9 digits).");

    public static Result<string> Email(string? input, string field = "email")
    {
        var value = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value) || value.Length > MaxEmailLength || !MailAddress.TryCreate(value, out var parsed) || parsed.Address != value)
        {
            return Error.InvalidField(field, "Enter a valid email address.");
        }

        return value;
    }

    public static Result Password(string? password, PasswordOptions options, string field = "newPassword")
    {
        if (string.IsNullOrEmpty(password) || password.Length < options.MinLength || password.Length > options.MaxLength)
        {
            return Error.InvalidField(field, $"Password must be {options.MinLength}–{options.MaxLength} characters long.");
        }

        if (password.Distinct().Count() < 4)
        {
            return Error.InvalidField(field, "Password is too simple.");
        }

        return Result.Success;
    }

    public static Result<string> FullName(string? input, string field = "fullName")
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > PatientProfile.MaxNameLength)
        {
            return Error.InvalidField(field, $"Name must be 1–{PatientProfile.MaxNameLength} characters long.");
        }

        return value;
    }

    public static bool IsCodeFormat(string? code, int length) =>
        code is not null && code.Length == length && code.All(char.IsAsciiDigit);
}
