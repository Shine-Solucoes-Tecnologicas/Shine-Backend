using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Shine.Api;

public sealed class WebSessionOptions
{
    public bool AllowLegacyBodyTransport { get; init; }
    public string? PublicApiOrigin { get; init; }
    public string? SiteDomain { get; init; }
}

public sealed record RefreshTokenCredential(string RawToken, bool FromCookie);

public interface IWebSessionTransport
{
    bool AllowsLegacyBodyTransport { get; }
    RefreshTokenCredential? ReadRefreshToken(HttpContext context, string? legacyBodyToken);
    Task<bool> ValidateAntiforgeryAsync(HttpContext context, RefreshTokenCredential credential);
    void WriteSession(HttpContext context, string rawRefreshToken, DateTime expiresAtUtc);
    void ClearSession(HttpContext context);
    void IssueAntiforgeryToken(HttpContext context, DateTime? expiresAtUtc = null);
    string? ResponseRefreshToken(string rawRefreshToken);
}

public sealed class WebSessionTransport(
    IAntiforgery antiforgery,
    IOptions<WebSessionOptions> options,
    IHostEnvironment environment) : IWebSessionTransport
{
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";
    public const string AntiforgeryRequestCookieName = "XSRF-TOKEN";
    private const string ProductionRefreshCookieName = "__Host-Shine.Refresh";
    private const string DevelopmentRefreshCookieName = "Shine.Refresh";

    public bool AllowsLegacyBodyTransport => options.Value.AllowLegacyBodyTransport;

    public RefreshTokenCredential? ReadRefreshToken(HttpContext context, string? legacyBodyToken)
    {
        if (options.Value.AllowLegacyBodyTransport)
            return string.IsNullOrWhiteSpace(legacyBodyToken)
                ? null
                : new RefreshTokenCredential(legacyBodyToken, false);

        if (context.Request.Cookies.TryGetValue(RefreshCookieName, out var cookieToken) &&
            !string.IsNullOrWhiteSpace(cookieToken))
            return new RefreshTokenCredential(cookieToken, true);

        return null;
    }

    public async Task<bool> ValidateAntiforgeryAsync(HttpContext context, RefreshTokenCredential credential)
    {
        if (!credential.FromCookie) return true;
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    public void WriteSession(HttpContext context, string rawRefreshToken, DateTime expiresAtUtc)
    {
        context.Response.Cookies.Append(RefreshCookieName, rawRefreshToken, CookieOptions(expiresAtUtc, httpOnly: true));
        IssueAntiforgeryToken(context, expiresAtUtc);
    }

    public void ClearSession(HttpContext context)
    {
        context.Response.Cookies.Delete(RefreshCookieName, CookieOptions(null, httpOnly: true));
        context.Response.Cookies.Delete(AntiforgeryRequestCookieName, CookieOptions(null, httpOnly: false));
        context.Response.Cookies.Delete(AntiforgeryCookieName, CookieOptions(null, httpOnly: true));
    }

    public void IssueAntiforgeryToken(HttpContext context, DateTime? expiresAtUtc = null)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken)) return;
        context.Response.Cookies.Append(
            AntiforgeryRequestCookieName,
            tokens.RequestToken,
            CookieOptions(expiresAtUtc, httpOnly: false));
    }

    public string? ResponseRefreshToken(string rawRefreshToken) =>
        options.Value.AllowLegacyBodyTransport ? rawRefreshToken : null;

    private string RefreshCookieName => environment.IsDevelopment()
        ? DevelopmentRefreshCookieName
        : ProductionRefreshCookieName;

    private string AntiforgeryCookieName => environment.IsDevelopment()
        ? "Shine.Antiforgery"
        : "__Host-Shine.Antiforgery";

    private CookieOptions CookieOptions(DateTime? expiresAtUtc, bool httpOnly) => new()
    {
        HttpOnly = httpOnly,
        Secure = !environment.IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expiresAtUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc.Value, DateTimeKind.Utc)),
        IsEssential = true
    };
}
