using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Api;
using Shine.Api.Controllers;
using Microsoft.Extensions.Logging;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class PasswordRecoveryIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Recovery_token_is_stored_as_hash_and_can_be_used_once()
    {
        await using var db = fixture.CreateDb();
        var user = new User($"recovery-{Guid.NewGuid():N}@example.test", "old-hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var raw = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        var token = new PasswordResetToken(user.Id, hash, DateTime.UtcNow.AddMinutes(30));
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync();

        var stored = await db.PasswordResetTokens.SingleAsync(item => item.Id == token.Id);
        Assert.Equal(hash, stored.TokenHash);
        Assert.NotEqual(raw, stored.TokenHash);
        Assert.True(stored.IsValid(DateTime.UtcNow));

        stored.MarkUsed(DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.False(stored.IsValid(DateTime.UtcNow));
    }

    [Fact]
    public async Task Recovery_tokens_from_the_same_user_can_be_invalidated_before_new_request()
    {
        await using var db = fixture.CreateDb();
        var user = new User($"recovery-{Guid.NewGuid():N}@example.test", "old-hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var first = new PasswordResetToken(user.Id, $"first-{Guid.NewGuid():N}", DateTime.UtcNow.AddMinutes(30));
        var second = new PasswordResetToken(user.Id, $"second-{Guid.NewGuid():N}", DateTime.UtcNow.AddMinutes(30));
        db.PasswordResetTokens.AddRange(first, second);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        foreach (var token in await db.PasswordResetTokens.Where(item => item.UserId == user.Id && item.UsedAtUtc == null).ToListAsync())
            token.MarkUsed(now);
        await db.SaveChangesAsync();

        Assert.False(first.IsValid(now));
        Assert.False(second.IsValid(now));
    }

    [Fact]
    public async Task Mock_delivery_keeps_the_recovery_message_testable_without_logging_the_raw_token()
    {
        await using var db = fixture.CreateDb();
        var user = new User($"mock-recovery-{Guid.NewGuid():N}@example.test", "old-hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var template = new CapturingTemplate();
        var delivery = new CapturingDelivery();
        var logger = new CapturingLogger<PasswordRecoveryController>();
        var controller = new PasswordRecoveryController(db, new TestPasswordHash(), new TestPasswordPolicy(),
            template, delivery, logger);

        await controller.RequestRecovery(new PasswordRecoveryRequest(user.Email), default);

        Assert.NotNull(template.RawToken);
        Assert.Contains(template.RawToken, delivery.Message!.TextBody);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(template.RawToken, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("reset-password?token=", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingTemplate : IPasswordRecoveryMessageTemplate
    {
        public string? RawToken { get; private set; }
        public PasswordRecoveryMessage Create(string email, string rawToken, DateTime expiresAtUtc)
        {
            RawToken = rawToken;
            return new PasswordRecoveryMessage("subject", $"secret:{rawToken}", $"secret:{rawToken}");
        }
    }

    private sealed class CapturingDelivery : IPasswordRecoveryDelivery
    {
        public PasswordRecoveryMessage? Message { get; private set; }
        public Task DeliverAsync(string email, PasswordRecoveryMessage message, CancellationToken cancellationToken = default)
        {
            Message = message;
            return Task.CompletedTask;
        }
    }

    private sealed class TestPasswordHash : IPasswordHashService
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string? encodedHash) => password == encodedHash;
    }

    private sealed class TestPasswordPolicy : IPasswordPolicy
    {
        public bool IsValid(string password, out IReadOnlyCollection<string> errors)
        {
            errors = [];
            return true;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
