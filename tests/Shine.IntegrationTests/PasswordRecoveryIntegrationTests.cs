using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure;

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
}
