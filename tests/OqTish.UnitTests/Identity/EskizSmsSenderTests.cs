using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OqTish.Application.Identity.Sms;
using OqTish.Infrastructure.Sms;

namespace OqTish.UnitTests.Identity;

/// <summary>Eskiz adapter against a fake HTTP handler (NOT verified against the live Eskiz service).</summary>
public class EskizSmsSenderTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static EskizSmsSender Create(FakeHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new SmsOptions { Provider = "Eskiz", Eskiz = new EskizOptions { Email = "me@example.test", Password = "pw", From = "4546" } }),
        NullLogger<EskizSmsSender>.Instance);

    [Fact]
    public async Task Logs_in_once_and_sends_form_fields()
    {
        var handler = new FakeHandler((req, _) => req.RequestUri!.AbsolutePath == "/api/auth/login"
            ? Json(HttpStatusCode.OK, "{\"message\":\"token_generated\",\"data\":{\"token\":\"T1\"},\"token_type\":\"bearer\"}")
            : Json(HttpStatusCode.OK, "{\"id\":\"1\",\"status\":\"waiting\"}"));
        using var sender = Create(handler);

        await sender.SendAsync("+998901234567", "Kod: 123456", TestContext.Current.CancellationToken);
        await sender.SendAsync("+998901234567", "Kod: 654321", TestContext.Current.CancellationToken);

        Assert.Equal(["/api/auth/login", "/api/message/sms/send", "/api/message/sms/send"], handler.Calls.Select(c => c.Request.RequestUri!.AbsolutePath));
        Assert.Equal("https://notify.eskiz.uz/api/auth/login", handler.Calls[0].Request.RequestUri!.ToString());
        Assert.Contains("email=me%40example.test", handler.Calls[0].Body, StringComparison.Ordinal);
        var send = handler.Calls[1];
        Assert.Equal("Bearer", send.Request.Headers.Authorization!.Scheme);
        Assert.Equal("T1", send.Request.Headers.Authorization.Parameter);
        Assert.Contains("mobile_phone=998901234567", send.Body, StringComparison.Ordinal);
        Assert.Contains("from=4546", send.Body, StringComparison.Ordinal);
        Assert.Contains("message=Kod%3A+123456", send.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refreshes_token_once_on_401()
    {
        var logins = 0;
        var handler = new FakeHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/login")
            {
                logins++;
                return Json(HttpStatusCode.OK, $"{{\"data\":{{\"token\":\"T{logins}\"}}}}");
            }

            return req.Headers.Authorization!.Parameter == "T1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json(HttpStatusCode.OK, "{}");
        });
        using var sender = Create(handler);

        await sender.SendAsync("+998901234567", "x", TestContext.Current.CancellationToken);

        Assert.Equal(2, logins);
        Assert.Equal("T2", handler.Calls[^1].Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Provider_errors_raise_sms_delivery_exception()
    {
        var handler = new FakeHandler((req, _) => req.RequestUri!.AbsolutePath == "/api/auth/login"
            ? Json(HttpStatusCode.OK, "{\"data\":{\"token\":\"T\"}}")
            : new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var sender = Create(handler);
        await Assert.ThrowsAsync<SmsDeliveryException>(() => sender.SendAsync("+998901234567", "x", TestContext.Current.CancellationToken));

        var badLogin = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var sender2 = Create(badLogin);
        await Assert.ThrowsAsync<SmsDeliveryException>(() => sender2.SendAsync("+998901234567", "x", TestContext.Current.CancellationToken));
    }
}
