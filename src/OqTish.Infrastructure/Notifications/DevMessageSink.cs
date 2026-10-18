using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OqTish.Application.Notifications;
using OqTish.Domain.Notifications;
using OqTish.Infrastructure.Persistence;

namespace OqTish.Infrastructure.Notifications;

/// <summary>
/// Writes fake deliveries to <c>notify.dev_messages</c> in Development only (own DI scope and connection, never inside a
/// business transaction). Failures are swallowed (type logged): capture must never affect delivery.
/// </summary>
internal sealed partial class DevMessageSink(IServiceScopeFactory scopes, bool enabled, string origin, TimeProvider clock, ILogger<DevMessageSink> logger) : IDevMessageSink
{
    public bool Enabled => enabled;

    public string Origin => origin;

    public async Task CaptureAsync(string channel, string recipient, string text, string? buttonsJson, CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OqTishDbContext>();
            db.DevCapturedMessages.Add(DevCapturedMessage.Create(channel, recipient, text.Length > 4096 ? text[..4096] : text, buttonsJson, origin, clock.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCaptureFailed(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Development message capture failed ({ExceptionType})")]
    private static partial void LogCaptureFailed(ILogger logger, string exceptionType);
}
