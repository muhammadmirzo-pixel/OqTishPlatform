namespace OqTish.Application.Common;

/// <summary>Error categories; each maps to exactly one HTTP status (CONVENTIONS §2).</summary>
public enum ErrorKind
{
    /// <summary>400</summary>
    Validation = 1,

    /// <summary>401</summary>
    Unauthorized = 2,

    /// <summary>403</summary>
    Forbidden = 3,

    /// <summary>404</summary>
    NotFound = 4,

    /// <summary>409</summary>
    Conflict = 5,

    /// <summary>422</summary>
    RuleViolation = 6,

    /// <summary>429</summary>
    RateLimited = 7,
}

/// <summary>
/// A business outcome that is not success. <see cref="Code"/> is stable and machine-readable (listed in
/// docs/API.md §9); the frontend localises by code. <see cref="Message"/> is an English developer hint and
/// must never contain personal data.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Error is the agreed name in CONVENTIONS §2; C#-only codebase.")]
public sealed record Error(ErrorKind Kind, string Code, string Message, IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    /// <summary>For <see cref="ErrorKind.RateLimited"/>: when the client may retry (Retry-After header).</summary>
    public TimeSpan? RetryAfter { get; init; }

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(ErrorKind.Validation, code, message, fieldErrors);

    /// <summary><c>common.validation</c> with a single field error.</summary>
    public static Error InvalidField(string field, string message) =>
        new(ErrorKind.Validation, CommonErrors.ValidationCode, message, new Dictionary<string, string[]> { [field] = [message] });

    public static Error Unauthorized(string code, string message) => new(ErrorKind.Unauthorized, code, message);

    public static Error Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);

    public static Error NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    public static Error Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);

    public static Error Rule(string code, string message) => new(ErrorKind.RuleViolation, code, message);

    public static Error RateLimited(string code, string message, TimeSpan? retryAfter = null) =>
        new(ErrorKind.RateLimited, code, message) { RetryAfter = retryAfter };
}

/// <summary>Cross-module error codes (docs/API.md §9).</summary>
public static class CommonErrors
{
    public const string ValidationCode = "common.validation";

    public static readonly Error NotFound = Error.NotFound("common.not_found", "The resource does not exist or is not visible to the caller.");

    public static readonly Error Forbidden = Error.Forbidden("common.forbidden", "The caller is not permitted to perform this action.");

    public static readonly Error IdempotencyMismatch = Error.Rule("common.idempotency_mismatch", "The Idempotency-Key was already used with a different request body.");

    public static readonly Error Concurrency = Error.Conflict("common.concurrency", "The operation conflicted with a concurrent change. Please retry.");

    public static Error Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        Error.Validation(ValidationCode, "One or more fields are invalid.", fieldErrors);
}
