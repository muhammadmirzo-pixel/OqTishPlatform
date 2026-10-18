using System.Security.Claims;
using OqTish.Application.Common;

namespace OqTish.Api.Common;

/// <summary><see cref="IRequestContext"/> and <see cref="ICurrentUser"/> from the current HttpContext.</summary>
internal sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext, ICurrentUser
{
    public const string SessionClaim = "sid";

    public Guid? UserId => ReadGuid(ClaimTypes.NameIdentifier);

    public Guid? SessionId => ReadGuid(SessionClaim);

    public string? RemoteIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;

    private Guid? ReadGuid(string claim) =>
        accessor.HttpContext?.User.FindFirst(claim)?.Value is { } value && Guid.TryParse(value, out var id) ? id : null;
}
