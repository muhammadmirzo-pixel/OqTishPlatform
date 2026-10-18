using OqTish.Application.Common;

namespace OqTish.Infrastructure.Common;

/// <summary>Default request context for hosted services and CLI commands: no user, no IP.</summary>
internal sealed class SystemRequestContext : IRequestContext, ICurrentUser
{
    public Guid? UserId => null;

    public Guid? SessionId => null;

    public string? RemoteIp => null;

    public string? UserAgent => null;

    public string? CorrelationId => null;
}
