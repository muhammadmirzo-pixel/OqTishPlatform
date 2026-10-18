using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OqTish.Application.Identity.Telegram;
using OqTish.Application.Notifications.Telegram;
using OqTish.Infrastructure.Telegram;

namespace OqTish.UnitTests.Notifications;

/// <summary>The real Bot API adapter against a fake HTTP handler (request shape, error classification, 429, timeouts, token hygiene).</summary>
public class TelegramBotApiClientTests
{
    private const string Token = "123456:AAH-unit-test-token";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeHandler(Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public FakeHandler(Func<HttpRequestMessage, string, Task<HttpResponseMessage>> respond)
            : this((request, body, _) => respond(request, body))
        {
        }

        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return await respond(request, body, cancellationToken);
        }
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });

    private static TelegramBotApiClient Create(FakeHandler handler, int requestTimeoutSeconds = 15, int maxInlineRetry = 5) => new(
        new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
        Options.Create(new TelegramOptions { Enabled = true, BotToken = Token, BotUsername = "oqtish_bot", RequestTimeoutSeconds = requestTimeoutSeconds, MaxInlineRetryAfterSeconds = maxInlineRetry }),
        NullLogger<TelegramBotApiClient>.Instance,
        TimeProvider.System);

    private static TelegramOutgoingMessage Message() => new(42, "Salom", [[new TelegramButton("Ochish", WebAppUrl: "https://oqtish.uz/tg/appointments/1")], [new TelegramButton("Konsol", Url: "https://oqtish.uz/clinic")]]);

    [Fact]
    public async Task Send_message_posts_json_with_inline_keyboard_to_the_bot_url()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":77}}"));
        var result = await Create(handler).SendMessageAsync(Message(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(77, result.Value);
        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://api.telegram.org/bot{Token}/sendMessage", request.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(42, root.GetProperty("chat_id").GetInt64());
        Assert.Equal("Salom", root.GetProperty("text").GetString());
        Assert.False(root.TryGetProperty("parse_mode", out _));
        var keyboard = root.GetProperty("reply_markup").GetProperty("inline_keyboard");
        Assert.Equal("https://oqtish.uz/tg/appointments/1", keyboard[0][0].GetProperty("web_app").GetProperty("url").GetString());
        Assert.Equal("https://oqtish.uz/clinic", keyboard[1][0].GetProperty("url").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden: bot was blocked by the user", TelegramFailureKind.ChatUnavailable, "telegram.blocked")]
    [InlineData(HttpStatusCode.BadRequest, "Bad Request: chat not found", TelegramFailureKind.ChatUnavailable, "telegram.chat_not_found")]
    [InlineData(HttpStatusCode.BadRequest, "Bad Request: message text is empty", TelegramFailureKind.Permanent, "telegram.bad_request")]
    [InlineData(HttpStatusCode.InternalServerError, "Internal Server Error", TelegramFailureKind.Transient, "telegram.http_500")]
    [InlineData(HttpStatusCode.BadGateway, "Bad Gateway", TelegramFailureKind.Transient, "telegram.http_502")]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized", TelegramFailureKind.Transient, "telegram.unauthorized")]
    [InlineData(HttpStatusCode.Conflict, "Conflict: terminated by other getUpdates request", TelegramFailureKind.Transient, "telegram.conflict")]
    public async Task Errors_are_classified(HttpStatusCode status, string description, TelegramFailureKind kind, string code)
    {
        var handler = new FakeHandler((_, _) => Json(status, $"{{\"ok\":false,\"error_code\":{(int)status},\"description\":\"{description}\"}}"));
        var result = await Create(handler).SendMessageAsync(Message(), Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(kind, result.Error.Kind);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), result.Error.ProviderStatus);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task Short_retry_after_is_waited_out_and_retried_once()
    {
        var calls = 0;
        var handler = new FakeHandler((_, _) => ++calls == 1
            ? Json(HttpStatusCode.TooManyRequests, "{\"ok\":false,\"error_code\":429,\"description\":\"Too Many Requests: retry after 1\",\"parameters\":{\"retry_after\":1}}")
            : Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":5}}"));
        var result = await Create(handler).SendMessageAsync(Message(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task Long_retry_after_is_returned_to_the_dispatcher()
    {
        var handler = new FakeHandler((_, _) =>
            Json(HttpStatusCode.TooManyRequests, "{\"ok\":false,\"error_code\":429,\"description\":\"Too Many Requests: retry after 120\",\"parameters\":{\"retry_after\":120}}"));
        var result = await Create(handler).SendMessageAsync(Message(), Ct);

        Assert.Equal(TelegramFailureKind.RateLimited, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(120), result.Error.RetryAfter);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task Timeout_and_network_errors_are_transient_and_never_expose_the_token()
    {
        var slow = new FakeHandler(async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var timeout = await Create(slow, requestTimeoutSeconds: 1).SendMessageAsync(Message(), Ct);
        Assert.Equal(TelegramFailureKind.Transient, timeout.Error!.Kind);
        Assert.Equal("telegram.timeout", timeout.Error.Code);

        var broken = new FakeHandler((req, _) => throw new HttpRequestException($"Connection refused ({req.RequestUri})"));
        var network = await Create(broken).SendMessageAsync(Message(), Ct);
        Assert.Equal("telegram.network", network.Error!.Code);
        Assert.Null(network.Error.Description);

        var echo = new FakeHandler((_, _) => Json(HttpStatusCode.BadRequest, $"{{\"ok\":false,\"error_code\":400,\"description\":\"bad token {Token}\"}}"));
        var echoed = await Create(echo).SendMessageAsync(Message(), Ct);
        Assert.DoesNotContain(Token, echoed.Error!.Description!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_updates_parses_private_and_group_messages()
    {
        const string json = """
            {"ok":true,"result":[
              {"update_id":10,"message":{"message_id":1,"chat":{"id":555,"type":"private"},"from":{"id":555,"is_bot":false,"language_code":"ru"},"text":"/start tok"}},
              {"update_id":11,"message":{"message_id":2,"chat":{"id":-100,"type":"group"},"from":{"id":556,"is_bot":false},"text":"/appointments"}},
              {"update_id":12,"edited_message":{"message_id":3}}
            ]}
            """;
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, json));
        var result = await Create(handler).GetUpdatesAsync(10, 25, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([10L, 11L, 12L], result.Value!.Select(u => u.UpdateId));
        var first = result.Value![0].Message!;
        Assert.Equal((555L, "private", 555L, "ru", "/start tok"), (first.ChatId, first.ChatType, first.FromId!.Value, first.LanguageCode, first.Text));
        Assert.Equal("group", result.Value[1].Message!.ChatType);
        Assert.Null(result.Value[2].Message);
        using var body = JsonDocument.Parse(handler.Calls[0].Body);
        Assert.Equal(10, body.RootElement.GetProperty("offset").GetInt64());
        Assert.Equal(25, body.RootElement.GetProperty("timeout").GetInt32());
        Assert.Equal("message", body.RootElement.GetProperty("allowed_updates")[0].GetString());
    }

    [Fact]
    public async Task Set_webhook_sends_the_secret_token()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":true}"));
        var result = await Create(handler).SetWebhookAsync("https://oqtish.uz/api/telegram/webhook/abc", "s3cr3t", Ct);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/setWebhook", handler.Calls[0].Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(handler.Calls[0].Body);
        Assert.Equal("s3cr3t", body.RootElement.GetProperty("secret_token").GetString());
    }
}
