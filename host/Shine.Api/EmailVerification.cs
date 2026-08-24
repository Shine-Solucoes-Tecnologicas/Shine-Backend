using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence;

namespace Shine.Api;

public sealed class EmailVerificationOptions
{
    public int TokenLifetimeMinutes { get; init; } = 30;
}

public sealed record EmailVerificationMessage(string Subject, string TextBody, string HtmlBody);

public interface IEmailVerificationMessageTemplate
{
    EmailVerificationMessage Create(string email, string rawToken, DateTime expiresAtUtc);
}

public interface IEmailVerificationDelivery
{
    Task DeliverAsync(string email, EmailVerificationMessage message, CancellationToken cancellationToken = default);
}

public sealed class NullEmailVerificationDelivery : IEmailVerificationDelivery
{
    public Task DeliverAsync(string email, EmailVerificationMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class InMemoryEmailVerificationDelivery : IEmailVerificationDelivery
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EmailVerificationMessage> messages =
        new(StringComparer.OrdinalIgnoreCase);

    public Task DeliverAsync(string email, EmailVerificationMessage message, CancellationToken cancellationToken = default)
    {
        messages[email.Trim()] = message;
        return Task.CompletedTask;
    }

    public bool TryGetLatest(string email, out EmailVerificationMessage? message) =>
        messages.TryGetValue(email.Trim(), out message);
}

public sealed class EmailVerificationMessageTemplate(IConfiguration configuration) : IEmailVerificationMessageTemplate
{
    public EmailVerificationMessage Create(string email, string rawToken, DateTime expiresAtUtc)
    {
        var baseUrl = configuration["EmailVerification:BaseUrl"]?.TrimEnd('/') ?? "https://app.local";
        var link = $"{baseUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";
        var expiry = expiresAtUtc.ToUniversalTime().ToString("O");
        var encodedLink = System.Net.WebUtility.HtmlEncode(link);
        return new EmailVerificationMessage(
            "Confirme seu e-mail",
            $"Confirme seu e-mail usando este link: {link}\n\nO link expira em {expiry} e pode ser usado uma única vez.",
            $"<p><a href=\"{encodedLink}\">Confirmar e-mail</a></p><p>O link expira em {expiry} e pode ser usado uma única vez.</p>");
    }
}

public static class EmailVerificationTokenService
{
    public static (string RawToken, string TokenHash) Create()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}

public sealed class EmailVerificationChallengeService(
    ShineDbContext dbContext,
    IEmailVerificationMessageTemplate messageTemplate,
    IEmailVerificationDelivery delivery,
    IOptions<EmailVerificationOptions> options,
    ILogger<EmailVerificationChallengeService> logger)
{
    public async Task IssueAsync(User user, CancellationToken cancellationToken)
    {
        if (!user.IsActive || user.IsEmailVerified) return;

        var now = DateTime.UtcNow;
        var pending = await dbContext.EmailVerificationTokens
            .Where(token => token.UserId == user.Id && token.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in pending) token.MarkUsed(now);

        var (rawToken, tokenHash) = EmailVerificationTokenService.Create();
        var expiresAtUtc = now.AddMinutes(options.Value.TokenLifetimeMinutes);
        dbContext.EmailVerificationTokens.Add(new EmailVerificationToken(user.Id, tokenHash, expiresAtUtc, now));
        await dbContext.SaveChangesAsync(cancellationToken);

        var message = messageTemplate.Create(user.Email, rawToken, expiresAtUtc);
        await delivery.DeliverAsync(user.Email, message, cancellationToken);
        logger.LogInformation("Email verification delivery prepared for user {UserId}; sensitive content omitted.", user.Id);
    }
}
