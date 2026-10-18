using Microsoft.Extensions.Options;
using OqTish.Application.Identity;

namespace OqTish.Api.Auth;

/// <summary>
/// The session cookie. Over HTTPS (or whenever <c>Auth:Session:RequireSecureCookie</c> is true) it is
/// <c>__Host-oqtish</c>: HttpOnly, Secure, SameSite=Lax, Path=/, no Domain. Browsers reject <c>__Host-</c>
/// cookies without Secure, so the plain-HTTP Development profile uses <c>oqtish-dev</c> instead.
/// </summary>
public sealed class SessionCookie(IOptions<AuthOptions> options)
{
    public const string SecureName = "__Host-oqtish";
    public const string InsecureDevName = "oqtish-dev";

    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Cookies.TryGetValue(SecureName, out var secure) && !string.IsNullOrEmpty(secure)
            ? secure
            : request.Cookies.TryGetValue(InsecureDevName, out var dev) && !string.IsNullOrEmpty(dev) ? dev : null;
    }

    public void Append(HttpContext http, string token, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(http);
        var secure = IsSecure(http);
        http.Response.Cookies.Append(secure ? SecureName : InsecureDevName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAt,
            MaxAge = expiresAt - DateTimeOffset.UtcNow,
            IsEssential = true,
        });
    }

    public void Delete(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var secure = IsSecure(http);
        http.Response.Cookies.Delete(secure ? SecureName : InsecureDevName, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }

    private bool IsSecure(HttpContext http) => options.Value.Session.RequireSecureCookie || http.Request.IsHttps;
}
