using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Shine.Api;

public sealed record AuthenticationErrorResponse(IReadOnlyCollection<AuthenticationErrorItem> Errors);

public sealed record AuthenticationErrorItem(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Field = null);

public static class AuthenticationErrors
{
    public const string Validation = "validation_error";
    public const string InvalidCredentials = "invalid_credentials";
    public const string SessionInvalid = "session_invalid";
    public const string CsrfValidationFailed = "csrf_validation_failed";
    public const string UnitAccessDenied = "unit_access_denied";
    public const string SessionNotFound = "session_not_found";
    public const string PasswordPolicyFailed = "password_policy_failed";
    public const string PasswordResetInvalid = "password_reset_invalid";
    public const string EmailAlreadyRegistered = "email_already_registered";
    public const string EmailVerificationInvalid = "email_verification_invalid";
    public const string OrganizationNameInvalid = "organization_name_invalid";
    public const string UnitNameInvalid = "unit_name_invalid";
    public const string UnitNameConflict = "unit_name_conflict";
    public const string RateLimitExceeded = "rate_limit_exceeded";

    public static AuthenticationErrorResponse Single(string code, string message, string? field = null) =>
        new([new AuthenticationErrorItem(code, message, field)]);

    public static ObjectResult Result(int statusCode, string code, string message, string? field = null) =>
        new(Single(code, message, field)) { StatusCode = statusCode };

    public static AuthenticationErrorResponse PasswordPolicy(
        IReadOnlyCollection<string> requirements,
        string field) =>
        new(requirements.Count == 0
            ? [new AuthenticationErrorItem(PasswordPolicyFailed, "A senha não atende aos requisitos.", field)]
            : requirements.Select(requirement => new AuthenticationErrorItem(
                PasswordPolicyFailed, requirement, field)).ToArray());
}
