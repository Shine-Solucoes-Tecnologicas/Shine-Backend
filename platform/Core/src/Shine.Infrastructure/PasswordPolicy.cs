using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Shine.Infrastructure;

public sealed class PasswordPolicyOptions
{
    [Range(8, 128)]
    public int MinimumLength { get; init; } = 12;
    public bool RequireUppercase { get; init; } = true;
    public bool RequireLowercase { get; init; } = true;
    public bool RequireDigit { get; init; } = true;
    public bool RequireSpecialCharacter { get; init; } = true;
}

public interface IPasswordPolicy
{
    bool IsValid(string password, out IReadOnlyCollection<string> errors);
}

public sealed class PasswordPolicy(Microsoft.Extensions.Options.IOptions<PasswordPolicyOptions> options) : IPasswordPolicy
{
    private readonly PasswordPolicyOptions settings = options.Value;

    public bool IsValid(string password, out IReadOnlyCollection<string> errors)
    {
        password ??= string.Empty;
        var result = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < settings.MinimumLength)
            result.Add($"Password must contain at least {settings.MinimumLength} characters.");
        if (settings.RequireUppercase && !Regex.IsMatch(password, "[A-Z]")) result.Add("Password must contain an uppercase letter.");
        if (settings.RequireLowercase && !Regex.IsMatch(password, "[a-z]")) result.Add("Password must contain a lowercase letter.");
        if (settings.RequireDigit && !Regex.IsMatch(password, "[0-9]")) result.Add("Password must contain a digit.");
        if (settings.RequireSpecialCharacter && !Regex.IsMatch(password, "[^a-zA-Z0-9]")) result.Add("Password must contain a special character.");
        errors = result;
        return result.Count == 0;
    }
}
