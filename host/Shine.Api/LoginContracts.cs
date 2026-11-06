using System.Text.Json.Serialization;

namespace Shine.Api;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(
    Guid UserId,
    string Email,
    IReadOnlyCollection<AccessibleTenantResponse> Tenants,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RefreshToken = null);

public sealed record AccessibleTenantResponse(Guid Id, string Name);
