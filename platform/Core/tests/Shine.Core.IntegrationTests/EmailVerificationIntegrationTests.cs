using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shine.Api;
using Shine.Api.Controllers;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class EmailVerificationIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Registration_creates_the_customer_structure_without_issuing_a_session()
    {
        var email = $"registration-{Guid.NewGuid():N}@example.test";
        var accountName = $"Account {Guid.NewGuid():N}";
        await using var db = fixture.CreateDb();
        var template = new CapturingTemplate();
        var controller = new RegistrationController(
            db,
            new Pbkdf2PasswordHashService(),
            new PasswordPolicy(Options.Create(new PasswordPolicyOptions())),
            Challenge(db, template));

        var response = await controller.Register(
            new RegistrationRequest(email, "Strong-password-123!", accountName), default);

        Assert.IsType<AcceptedResult>(response);
        var user = await db.Users.SingleAsync(candidate => candidate.NormalizedEmail == User.NormalizeEmail(email));
        Assert.False(user.IsEmailVerified);
        Assert.False(await db.RefreshTokens.AnyAsync(token => token.UserId == user.Id));
        Assert.True(await db.CustomerAccounts.AnyAsync(account => account.Name == accountName));
        Assert.True(await db.Tenants.AnyAsync(unit => unit.Name == accountName));
        Assert.NotNull(template.RawToken);
        Assert.True(await db.EmailVerificationTokens.AnyAsync(token => token.UserId == user.Id && token.UsedAtUtc == null));

        Assert.IsType<AcceptedResult>(await controller.Register(
            new RegistrationRequest(email.ToUpperInvariant(), "Another-strong-password-123!", "Ignored"), default));
    }

    [Fact]
    public async Task Challenge_is_hashed_and_confirmation_is_single_use()
    {
        var user = new User($"verification-{Guid.NewGuid():N}@example.test", "hash", emailVerified: false);
        await using var db = fixture.CreateDb();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var template = new CapturingTemplate();
        await Challenge(db, template).IssueAsync(user, default);

        var stored = await db.EmailVerificationTokens.SingleAsync(token => token.UserId == user.Id);
        Assert.NotEqual(template.RawToken, stored.TokenHash);
        Assert.Equal(EmailVerificationTokenService.Hash(template.RawToken!), stored.TokenHash);

        var controller = Controller(db);
        Assert.IsType<NoContentResult>(await controller.Confirm(new EmailVerificationConfirmRequest(template.RawToken!), default));
        Assert.IsType<BadRequestResult>(await controller.Confirm(new EmailVerificationConfirmRequest(template.RawToken!), default));
        Assert.True((await db.Users.SingleAsync(candidate => candidate.Id == user.Id)).IsEmailVerified);
    }

    [Fact]
    public async Task Expired_challenge_is_rejected()
    {
        var now = DateTime.UtcNow;
        var user = new User($"expired-{Guid.NewGuid():N}@example.test", "hash", emailVerified: false);
        var (raw, hash) = EmailVerificationTokenService.Create();
        await using var db = fixture.CreateDb();
        db.Add(user);
        db.EmailVerificationTokens.Add(new EmailVerificationToken(user.Id, hash, now.AddSeconds(-1), now.AddHours(-1)));
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestResult>(await Controller(db).Confirm(new EmailVerificationConfirmRequest(raw), default));
        Assert.False(user.IsEmailVerified);
    }

    [Fact]
    public async Task Concurrent_confirmation_consumes_the_challenge_once()
    {
        var user = new User($"concurrent-{Guid.NewGuid():N}@example.test", "hash", emailVerified: false);
        var (raw, hash) = EmailVerificationTokenService.Create();
        await using (var seed = fixture.CreateDb())
        {
            seed.Add(user);
            seed.EmailVerificationTokens.Add(new EmailVerificationToken(user.Id, hash, DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }

        async Task<IActionResult> Confirm()
        {
            await using var db = fixture.CreateDb();
            return await Controller(db).Confirm(new EmailVerificationConfirmRequest(raw), default);
        }

        var results = await Task.WhenAll(Confirm(), Confirm());

        Assert.Single(results, result => result is NoContentResult);
        Assert.Single(results, result => result is BadRequestResult);
    }

    [Fact]
    public async Task Resend_has_the_same_public_response_for_known_and_unknown_addresses()
    {
        var user = new User($"resend-{Guid.NewGuid():N}@example.test", "hash", emailVerified: false);
        await using var db = fixture.CreateDb();
        db.Add(user);
        await db.SaveChangesAsync();
        var controller = Controller(db);

        var known = await controller.Resend(new EmailVerificationResendRequest(user.Email), default);
        var unknown = await controller.Resend(new EmailVerificationResendRequest($"unknown-{Guid.NewGuid():N}@example.test"), default);

        Assert.IsType<AcceptedResult>(known);
        Assert.IsType<AcceptedResult>(unknown);
    }

    private static EmailVerificationController Controller(ShineDbContext db) =>
        new(db, Challenge(db, new CapturingTemplate()));

    private static EmailVerificationChallengeService Challenge(ShineDbContext db, CapturingTemplate template) =>
        new(db, template, new NoopDelivery(), Options.Create(new EmailVerificationOptions()),
            NullLogger<EmailVerificationChallengeService>.Instance);

    private sealed class CapturingTemplate : IEmailVerificationMessageTemplate
    {
        public string? RawToken { get; private set; }
        public EmailVerificationMessage Create(string email, string rawToken, DateTime expiresAtUtc)
        {
            RawToken = rawToken;
            return new EmailVerificationMessage("subject", "body", "body");
        }
    }

    private sealed class NoopDelivery : IEmailVerificationDelivery
    {
        public Task DeliverAsync(string email, EmailVerificationMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
