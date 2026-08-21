namespace Shine.Api;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(Guid UserId, string Email, IReadOnlyCollection<AccessibleTenantResponse> Tenants, string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken);

public sealed record AccessibleTenantResponse(Guid Id, string Name);
