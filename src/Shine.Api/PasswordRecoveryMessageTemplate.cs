namespace Shine.Api;

public sealed record PasswordRecoveryMessage(string Subject, string TextBody, string HtmlBody);

public interface IPasswordRecoveryMessageTemplate
{
    PasswordRecoveryMessage Create(string email, string rawToken, DateTime expiresAtUtc);
}

public sealed class PasswordRecoveryMessageTemplate(IConfiguration configuration) : IPasswordRecoveryMessageTemplate
{
    public PasswordRecoveryMessage Create(string email, string rawToken, DateTime expiresAtUtc)
    {
        var baseUrl = configuration["PasswordRecovery:BaseUrl"]?.TrimEnd('/') ?? "https://app.local";
        var link = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        var expiry = expiresAtUtc.ToUniversalTime().ToString("O");
        var subject = "Redefinição de senha";
        var text = $"Olá, {email}.\n\nUse este link para redefinir sua senha: {link}\n\nO link expira em {expiry} e pode ser usado uma única vez.";
        var html = $"<p>Olá, {System.Net.WebUtility.HtmlEncode(email)}.</p><p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Redefinir senha</a></p><p>O link expira em {expiry} e pode ser usado uma única vez.</p>";
        return new PasswordRecoveryMessage(subject, text, html);
    }
}
