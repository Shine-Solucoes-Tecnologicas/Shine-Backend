using Microsoft.AspNetCore.Http;
using Shine.Api;

namespace Shine.IntegrationTests;

internal sealed class LegacyTestWebSessionTransport : IWebSessionTransport
{
    public bool AllowsLegacyBodyTransport => true;
    public RefreshTokenCredential? ReadRefreshToken(HttpContext context, string? legacyBodyToken) =>
        string.IsNullOrWhiteSpace(legacyBodyToken) ? null : new RefreshTokenCredential(legacyBodyToken, false);
    public Task<bool> ValidateAntiforgeryAsync(HttpContext context, RefreshTokenCredential credential) =>
        Task.FromResult(true);
    public void WriteSession(HttpContext context, string rawRefreshToken, DateTime expiresAtUtc) { }
    public void ClearSession(HttpContext context) { }
    public void IssueAntiforgeryToken(HttpContext context, DateTime? expiresAtUtc = null) { }
    public string? ResponseRefreshToken(string rawRefreshToken) => rawRefreshToken;
}
