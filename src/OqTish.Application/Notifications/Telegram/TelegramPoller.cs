using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Identity.Telegram;
using OqTish.Domain.Notifications;

namespace OqTish.Application.Notifications.Telegram;

/// <summary>
/// One long-poll round of the bot (<c>Telegram:Mode=Polling</c>, Worker <c>TelegramPollingService</c>): reads the
/// persisted offset (<c>notify.telegram_bot_state</c>, per bot id), calls getUpdates, hands every update to
/// <see cref="TelegramBotService"/> and stores <c>update_id + 1</c> after each one. A crash between handling and
/// storing re-handles that single update (at-least-once); a failing update is logged (type only) and skipped so it
/// cannot block the queue.
/// </summary>
public sealed partial class TelegramPoller(
    IOqTishDb db,
    ITelegramBotClient telegram,
    TelegramBotService bot,
    IDbClock clock,
    IOptions<TelegramOptions> options,
    ILogger<TelegramPoller> logger)
{
    /// <returns>The number of updates handled, or the Bot API error.</returns>
    public async Task<TelegramResult<int>> PollOnceAsync(CancellationToken ct)
    {
        var o = options.Value;
        var botId = o.BotId ?? 0;
        var offset = await db.TelegramBotStates.AsNoTracking().Where(s => s.BotId == botId).Select(s => (long?)s.UpdateOffset).FirstOrDefaultAsync(ct) ?? 0;
        var result = await telegram.GetUpdatesAsync(offset, o.PollingTimeoutSeconds, ct);
        if (!result.IsSuccess)
        {
            return TelegramResult.Fail<int>(result.Error);
        }

        var handled = 0;
        foreach (var update in result.Value!.Where(u => u.UpdateId >= offset).OrderBy(u => u.UpdateId))
        {
            try
            {
                await bot.HandleAsync(update, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogUpdateFailed(logger, ex.GetType().Name);
            }

            await StoreOffsetAsync(botId, update.UpdateId + 1, ct);
            handled++;
        }

        return TelegramResult.Ok(handled);
    }

    private async Task StoreOffsetAsync(long botId, long next, CancellationToken ct)
    {
        var now = await clock.NowAsync(ct);
        var updated = await db.TelegramBotStates
            .Where(s => s.BotId == botId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.UpdateOffset, x => x.UpdateOffset < next ? next : x.UpdateOffset)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (updated == 0)
        {
            var state = TelegramBotState.Create(botId, next, now);
            db.TelegramBotStates.Add(state);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                db.TelegramBotStates.Remove(state); // detach; the next round retries the upsert
                throw;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram update handling failed ({ExceptionType}); update skipped")]
    private static partial void LogUpdateFailed(ILogger logger, string exceptionType);
}
