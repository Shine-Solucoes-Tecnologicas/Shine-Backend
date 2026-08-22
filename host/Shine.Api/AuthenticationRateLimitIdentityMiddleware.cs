using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shine.Domain.Identity;

namespace Shine.Api;

public sealed class NullRemoteForwardedHeadersGuardMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is null)
        {
            context.Request.Headers.Remove("X-Forwarded-For");
            context.Request.Headers.Remove("X-Forwarded-Proto");
        }
        return next(context);
    }
}

public sealed class AuthenticationRateLimitIdentityMiddleware(RequestDelegate next)
{
    private const string ItemKey = "Shine.AuthenticationRateLimitIdentity";
    private const int MaximumBodyBytes = 16 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.StartsWithSegments("/api/auth") &&
            context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true &&
            context.Request.ContentLength is null or > 0 and <= MaximumBodyBytes)
        {
            context.Request.EnableBuffering(bufferThreshold: 1024, bufferLimit: MaximumBodyBytes);
            try
            {
                using var document = await JsonDocument.ParseAsync(context.Request.Body,
                    cancellationToken: context.RequestAborted);
                var identity = ExtractIdentity(document.RootElement);
                if (identity is not null)
                    context.Items[ItemKey] = Hash(identity);
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                // Invalid JSON remains in the IP-only partition and is handled by MVC.
            }
            finally
            {
                context.Request.Body.Position = 0;
            }
        }

        await next(context);
    }

    internal static string GetPartitionIdentity(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) && value is string identity ? identity : "anonymous";

    private static string? ExtractIdentity(JsonElement body)
    {
        if (TryGetString(body, "email", out var email) && !string.IsNullOrWhiteSpace(email))
            return $"email:{User.NormalizeEmail(email)}";
        if (TryGetString(body, "refreshToken", out var refreshToken) && !string.IsNullOrWhiteSpace(refreshToken))
            return $"refresh:{refreshToken}";
        if (TryGetString(body, "token", out var token) && !string.IsNullOrWhiteSpace(token))
            return $"token:{token}";
        return null;
    }

    private static bool TryGetString(JsonElement body, string propertyName, out string? value)
    {
        value = null;
        if (body.ValueKind != JsonValueKind.Object)
            return false;
        var property = body.EnumerateObject().LastOrDefault(candidate =>
            string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase)).Value;
        if (property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return true;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
