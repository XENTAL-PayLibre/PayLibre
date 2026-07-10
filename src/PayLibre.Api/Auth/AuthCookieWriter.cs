using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Infrastructure.Security;

namespace PayLibre.Api.Auth;

/// <summary>
/// Writes/clears the dashboard session cookies. The access + refresh tokens are delivered ONLY as
/// HttpOnly, Secure, SameSite cookies — never in a response body — so browser JS can't read them and
/// they only travel over HTTPS.
/// </summary>
public sealed class AuthCookieWriter(IOptions<AuthOptions> auth)
{
    public const string AccessCookie = "plb_access";
    public const string RefreshCookie = "plb_refresh";

    private readonly AuthOptions _auth = auth.Value;

    public void SetSession(HttpResponse response, IssuedSession session)
    {
        response.Cookies.Append(AccessCookie, session.Access.Token, BuildOptions(session.Access.ExpiresAt));
        response.Cookies.Append(RefreshCookie, session.RefreshToken, BuildOptions(session.RefreshExpiresAt));
    }

    public void Clear(HttpResponse response)
    {
        var opts = BuildOptions(DateTimeOffset.UnixEpoch);
        response.Cookies.Delete(AccessCookie, opts);
        response.Cookies.Delete(RefreshCookie, opts);
    }

    private CookieOptions BuildOptions(DateTimeOffset expires)
    {
        var sameSite = ParseSameSite(_auth.CookieSameSite);
        return new CookieOptions
        {
            HttpOnly = true,
            // SameSite=None requires Secure, so force it in that case regardless of config.
            Secure = _auth.CookieSecure || sameSite == SameSiteMode.None,
            SameSite = sameSite,
            Expires = expires,
            Path = "/",
            Domain = string.IsNullOrWhiteSpace(_auth.CookieDomain) ? null : _auth.CookieDomain,
        };
    }

    private static SameSiteMode ParseSameSite(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "none" => SameSiteMode.None,
        "strict" => SameSiteMode.Strict,
        _ => SameSiteMode.Lax,
    };
}
