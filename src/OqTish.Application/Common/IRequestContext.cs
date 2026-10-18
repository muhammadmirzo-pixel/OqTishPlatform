namespace OqTish.Application.Common;

/// <summary>
/// Ambient facts about the current request or job: who, from where, correlation id. Implemented by the
/// API from HttpContext and by a system default in hosted services.
/// </summary>
public interface IRequestContext
{
    /// <summary>Authenticated user id, if any.</summary>
    Guid? UserId { get; }

    /// <summary>Current session id (not the cookie token), if any.</summary>
    Guid? SessionId { get; }

    /// <summary>Remote IP address as text (never logged; hashed before storage).</summary>
    string? RemoteIp { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }
}

/// <summary>The signed-in user. <see cref="UserId"/> is null for anonymous requests.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid? SessionId { get; }

    bool IsAuthenticated => UserId is not null;

    /// <summary>The user id; throws when anonymous (only call from endpoints that require authorization).</summary>
    Guid RequiredUserId => UserId ?? throw new InvalidOperationException("No authenticated user.");
}
