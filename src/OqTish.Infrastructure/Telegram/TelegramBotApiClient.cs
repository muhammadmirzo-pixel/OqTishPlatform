using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OqTish.Application.Identity.Telegram;
using OqTish.Application.Notifications.Telegram;

namespace OqTish.Infrastructure.Telegram;

/// <summary>
/// Telegram Bot API over HTTPS (<c>{ApiBaseUrl}/bot{token}/{method}</c>, JSON POST). Per-call timeout
/// (<c>Telegram:RequestTimeoutSeconds</c>; getUpdates: long-poll timeout + 10 s). Failures are returned classified
/// (<see cref="TelegramFailureKind"/>): timeout/network/5xx/401/404/409 transient, 429 rate limited (a short
/// <c>retry_after</c> ≤ <c>MaxInlineRetryAfterSeconds</c> is waited out and retried once), 403 and "chat not found"
/// chat unavailable, other 400 permanent. The URL (it contains the token), request bodies and message text are
/// never logged; only the method and the error code.
/// </summary>
internal sealed partial class TelegramBotApiClient(HttpClient http, IOptions<TelegramOptions> options, ILogger<TelegramBotApiClient> logger, TimeProvider clock) : ITelegramBotClient
{
    private const int MaxDescriptionLength = 200;

    public async Task<TelegramResult<long>> SendMessageAsync(TelegramOutgoingMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var result = await CallAsync("sendMessage", TelegramJson.SendMessageBody(message), RequestTimeout, cancellationToken);
        if (!result.IsSuccess)
        {
            return TelegramResult.Fail<long>(result.Error);
        }

        var id = result.Value.ValueKind == JsonValueKind.Object && result.Value.TryGetProperty("message_id", out var mid) && mid.TryGetInt64(out var v) ? v : 0;
        return TelegramResult.Ok(id);
    }

    public async Task<TelegramResult<IReadOnlyList<TelegramUpdate>>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var timeout = Math.Clamp(timeoutSeconds, 0, 50);
        var body = new JsonObject
        {
            ["offset"] = offset,
            ["timeout"] = timeout,
            ["allowed_updates"] = new JsonArray("message"),
        };
        var result = await CallAsync("getUpdates", body, TimeSpan.FromSeconds(timeout + 10), cancellationToken);
        if (!result.IsSuccess)
        {
            return TelegramResult.Fail<IReadOnlyList<TelegramUpdate>>(result.Error);
        }

        var updates = new List<TelegramUpdate>();
        if (result.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in result.Value.EnumerateArray())
            {
                if (TelegramJson.ParseUpdate(item) is { } update)
                {
                    updates.Add(update);
                }
            }
        }

        return TelegramResult.Ok<IReadOnlyList<TelegramUpdate>>(updates);
    }

    public async Task<TelegramResult<bool>> SetWebhookAsync(string url, string secretToken, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["url"] = url,
            ["secret_token"] = secretToken,
            ["allowed_updates"] = new JsonArray("message"),
            ["drop_pending_updates"] = false,
        };
        var result = await CallAsync("setWebhook", body, RequestTimeout, cancellationToken);
        return result.IsSuccess ? TelegramResult.Ok(true) : TelegramResult.Fail<bool>(result.Error);
    }

    public async Task<TelegramResult<bool>> DeleteWebhookAsync(CancellationToken cancellationToken)
    {
        var result = await CallAsync("deleteWebhook", new JsonObject { ["drop_pending_updates"] = false }, RequestTimeout, cancellationToken);
        return result.IsSuccess ? TelegramResult.Ok(true) : TelegramResult.Fail<bool>(result.Error);
    }

    public async Task<TelegramResult<TelegramBotInfo>> GetMeAsync(CancellationToken cancellationToken)
    {
        var result = await CallAsync("getMe", new JsonObject(), RequestTimeout, cancellationToken);
        if (!result.IsSuccess)
        {
            return TelegramResult.Fail<TelegramBotInfo>(result.Error);
        }

        var v = result.Value;
        var id = v.ValueKind == JsonValueKind.Object && v.TryGetProperty("id", out var i) && i.TryGetInt64(out var n) ? n : 0;
        var username = v.ValueKind == JsonValueKind.Object && v.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        return TelegramResult.Ok(new TelegramBotInfo(id, username));
    }

    private TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Clamp(options.Value.RequestTimeoutSeconds, 1, 120));

    private async Task<TelegramResult<JsonElement>> CallAsync(string method, JsonObject body, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.BotToken))
        {
            return Fail(method, new TelegramError(TelegramFailureKind.Transient, "telegram.not_configured", "config", null));
        }

        var uri = new Uri($"{o.ApiBaseUrl.TrimEnd('/')}/bot{o.BotToken}/{method}");
        var json = body.ToJsonString();
        for (var attempt = 0; ; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            int status;
            string text;
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(uri, content, cts.Token);
                status = (int)response.StatusCode;
                text = await response.Content.ReadAsStringAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail(method, new TelegramError(TelegramFailureKind.Transient, "telegram.timeout", "timeout", null));
            }
            catch (HttpRequestException)
            {
                // The exception message can include the request URI (and therefore the token): never surfaced.
                return Fail(method, new TelegramError(TelegramFailureKind.Transient, "telegram.network", "network", null));
            }

            var parsed = Parse(text);
            if (status is >= 200 and < 300 && parsed.Ok)
            {
                return TelegramResult.Ok(parsed.Result);
            }

            var error = Classify(status, parsed.ErrorCode, Scrub(parsed.Description, o.BotToken), parsed.RetryAfter);
            if (error.Kind == TelegramFailureKind.RateLimited && attempt == 0 && error.RetryAfter is { } wait
                && wait <= TimeSpan.FromSeconds(Math.Max(0, o.MaxInlineRetryAfterSeconds)))
            {
                await Task.Delay(wait, clock, cancellationToken);
                continue;
            }

            return Fail(method, error);
        }
    }

    internal static TelegramError Classify(int httpStatus, int? errorCode, string? description, int? retryAfterSeconds)
    {
        var code = errorCode ?? httpStatus;
        var status = code.ToString(CultureInfo.InvariantCulture);
        var lower = description?.ToLowerInvariant() ?? string.Empty;
        return code switch
        {
            (int)HttpStatusCode.TooManyRequests => new TelegramError(TelegramFailureKind.RateLimited, "telegram.rate_limited", status, description,
                TimeSpan.FromSeconds(Math.Max(1, retryAfterSeconds ?? 5))),
            (int)HttpStatusCode.Forbidden => new TelegramError(TelegramFailureKind.ChatUnavailable, "telegram.blocked", status, description),
            (int)HttpStatusCode.BadRequest when lower.Contains("chat not found", StringComparison.Ordinal) || lower.Contains("user not found", StringComparison.Ordinal)
                => new TelegramError(TelegramFailureKind.ChatUnavailable, "telegram.chat_not_found", status, description),
            (int)HttpStatusCode.BadRequest => new TelegramError(TelegramFailureKind.Permanent, "telegram.bad_request", status, description),
            (int)HttpStatusCode.Unauthorized => new TelegramError(TelegramFailureKind.Transient, "telegram.unauthorized", status, description),
            (int)HttpStatusCode.NotFound => new TelegramError(TelegramFailureKind.Transient, "telegram.not_found", status, description),
            (int)HttpStatusCode.Conflict => new TelegramError(TelegramFailureKind.Transient, "telegram.conflict", status, description),
            _ when httpStatus is >= 200 and < 300 => new TelegramError(TelegramFailureKind.Transient, "telegram.bad_response", status, description),
            _ => new TelegramError(TelegramFailureKind.Transient, $"telegram.http_{status}", status, description),
        };
    }

    internal static string? Scrub(string? description, string? token)
    {
        if (string.IsNullOrEmpty(description))
        {
            return null;
        }

        var scrubbed = string.IsNullOrEmpty(token) ? description : description.Replace(token, "***", StringComparison.Ordinal);
        return scrubbed.Length > MaxDescriptionLength ? scrubbed[..MaxDescriptionLength] : scrubbed;
    }

    private static (bool Ok, JsonElement Result, int? ErrorCode, string? Description, int? RetryAfter) Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, default, null, null, null);
            }

            var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            var result = root.TryGetProperty("result", out var r) ? r.Clone() : default;
            int? errorCode = root.TryGetProperty("error_code", out var ec) && ec.TryGetInt32(out var e) ? e : null;
            var description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            int? retryAfter = root.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object
                && p.TryGetProperty("retry_after", out var ra) && ra.TryGetInt32(out var seconds) ? seconds : null;
            return (ok, result, errorCode, description, retryAfter);
        }
        catch (JsonException)
        {
            return (false, default, null, null, null);
        }
    }

    private TelegramResult<JsonElement> Fail(string method, TelegramError error)
    {
        LogCallFailed(logger, method, error.Code);
        return TelegramResult.Fail<JsonElement>(error);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram Bot API {Method} failed ({Code})")]
    private static partial void LogCallFailed(ILogger logger, string method, string code);
}
