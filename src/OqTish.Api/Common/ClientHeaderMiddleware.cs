using OqTish.Application.Common;

namespace OqTish.Api.Common;

/// <summary>
/// CSRF defence (OD-23): every non-safe request under <c>/api</c> must carry
/// <c>X-OqTish-Client: web | tg-miniapp</c>, which browsers cannot send cross-site without a CORS preflight
/// (CORS is closed). Missing/unknown → 403 <c>security.client_header_missing</c>. The Telegram webhook
/// (<c>/api/telegram/webhook/…</c>) is exempt: it is authenticated by its secret.
/// </summary>
public sealed class ClientHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-OqTish-Client";

    public static readonly IReadOnlySet<string> AllowedClients = new HashSet<string>(StringComparer.Ordinal) { "web", "tg-miniapp" };

    public static readonly Error Missing = Error.Forbidden("security.client_header_missing", "The X-OqTish-Client header is required for this request.");

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request;
        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method)
            && !request.Path.StartsWithSegments("/api/telegram/webhook", StringComparison.OrdinalIgnoreCase)
            && !(request.Headers.TryGetValue(HeaderName, out var values) && values.Count == 1 && AllowedClients.Contains(values[0]!)))
        {
            return ProblemResults.WriteAsync(context, Missing);
        }

        return next(context);
    }
}
