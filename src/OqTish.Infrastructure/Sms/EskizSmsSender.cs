using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Sms;

namespace OqTish.Infrastructure.Sms;

/// <summary>
/// Eskiz.uz SMS gateway adapter (OD-09). <b>Not verified against the live service</b> (no credentials yet);
/// behaviour follows the public API documentation and is covered by tests with a fake HTTP handler:
/// <list type="number">
/// <item><c>POST /api/auth/login</c> (form: email, password) → <c>{ data: { token } }</c>; the bearer token is
/// cached and fetched again once when a send returns 401.</item>
/// <item><c>POST /api/message/sms/send</c> (form: mobile_phone = digits without '+', message, from).</item>
/// </list>
/// Production SMS texts must match a template approved by Eskiz. Never logs the phone, text or token.
/// </summary>
internal sealed partial class EskizSmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<EskizSmsSender> logger) : ISmsSender, IDisposable
{
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;

    private EskizOptions Options => options.Value.Eskiz;

    public async Task SendAsync(string e164Phone, string text, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(forceRefresh: false, cancellationToken);
        using var first = await PostSmsAsync(token, e164Phone, text, cancellationToken);
        if (first.StatusCode == HttpStatusCode.Unauthorized)
        {
            token = await GetTokenAsync(forceRefresh: true, cancellationToken, staleToken: token);
            using var retry = await PostSmsAsync(token, e164Phone, text, cancellationToken);
            EnsureAccepted(retry);
            return;
        }

        EnsureAccepted(first);
    }

    public void Dispose() => _tokenLock.Dispose();

    private async Task<HttpResponseMessage> PostSmsAsync(string token, string e164Phone, string text, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Options.BaseUrl, "/api/message/sms/send"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["mobile_phone"] = PhoneNumber.DigitsOnly(e164Phone),
                ["message"] = text,
                ["from"] = Options.From,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, ct);
    }

    private void EnsureAccepted(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            LogSendRejected(logger, (int)response.StatusCode);
            throw new SmsDeliveryException($"Eskiz rejected the message (HTTP {(int)response.StatusCode}).");
        }
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct, string? staleToken = null)
    {
        if (!forceRefresh && _token is { } cached)
        {
            return cached;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while we waited.
            if (_token is { } current && (!forceRefresh || current != staleToken))
            {
                return current;
            }

            using var response = await http.PostAsync(
                new Uri(Options.BaseUrl, "/api/auth/login"),
                new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = Options.Email, ["password"] = Options.Password }),
                ct);
            if (!response.IsSuccessStatusCode)
            {
                LogLoginFailed(logger, (int)response.StatusCode);
                throw new SmsDeliveryException($"Eskiz login failed (HTTP {(int)response.StatusCode}).");
            }

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
            _token = body?.Data?.Token is { Length: > 0 } t ? t : throw new SmsDeliveryException("Eskiz login returned no token.");
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed record LoginResponse([property: JsonPropertyName("data")] LoginData? Data);

    private sealed record LoginData([property: JsonPropertyName("token")] string? Token);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Eskiz login failed with HTTP {Status}")]
    private static partial void LogLoginFailed(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Eskiz rejected an SMS with HTTP {Status}")]
    private static partial void LogSendRejected(ILogger logger, int status);
}
