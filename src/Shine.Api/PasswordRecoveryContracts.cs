namespace Shine.Api;

public sealed record PasswordRecoveryRequest(string Email);
public sealed record PasswordResetRequest(string Token, string NewPassword);
