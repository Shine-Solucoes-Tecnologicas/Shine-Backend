using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure.Persistence.Seed;
using Shine.Domain;
using Microsoft.AspNetCore.RateLimiting;

namespace Shine.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public sealed class RegistrationController(
    ShineDbContext dbContext,
    IPasswordHashService passwordHashService,
    IPasswordPolicy passwordPolicy,
    ITenantExecutionContext tenantExecutionContext,
    EmailVerificationChallengeService emailVerification) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.Registration)]
    public async Task<IActionResult> Register(RegistrationRequest request, CancellationToken cancellationToken)
    {
        if (!passwordPolicy.IsValid(request.Password, out var passwordErrors))
            return BadRequest(AuthenticationErrors.PasswordPolicy(passwordErrors, "password"));
        var organizationName = request.OrganizationName?.Trim();
        var unitName = request.UnitName?.Trim();
        var validationErrors = new List<AuthenticationErrorItem>();
        if (string.IsNullOrWhiteSpace(request.Email) ||
            !System.Net.Mail.MailAddress.TryCreate(request.Email, out _))
            validationErrors.Add(new(AuthenticationErrors.Validation, "Informe um e-mail válido.", "email"));
        if (string.IsNullOrWhiteSpace(organizationName) || organizationName.Length > 200)
            validationErrors.Add(new(AuthenticationErrors.OrganizationNameInvalid,
                "O nome da organização deve possuir entre 1 e 200 caracteres.", "organizationName"));
        if (string.IsNullOrWhiteSpace(unitName) || unitName.Length > 200)
            validationErrors.Add(new(AuthenticationErrors.UnitNameInvalid,
                "O nome da unidade deve possuir entre 1 e 200 caracteres.", "unitName"));
        if (validationErrors.Count > 0) return BadRequest(new AuthenticationErrorResponse(validationErrors));
        var normalizedEmail = Shine.Domain.Identity.User.NormalizeEmail(request.Email);
        var existing = await dbContext.Users.SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
        if (existing is not null)
        {
            await emailVerification.IssueAsync(existing, cancellationToken);
            return Accepted();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = new User(request.Email, passwordHashService.Hash(request.Password), emailVerified: false);
        var account = new CustomerAccount(organizationName!);
        var tenant = new Tenant(unitName!);
        tenant.AssignToCustomerAccount(account.Id);
        using var tenantScope = tenantExecutionContext.EnterTenant(tenant.Id);
        dbContext.Users.Add(user);
        dbContext.CustomerAccounts.Add(account);
        dbContext.Tenants.Add(tenant);
        dbContext.UserTenants.Add(new UserTenant(user.Id, tenant.Id, user.Id, isOwner: true));
        var accountMembership = new CustomerAccountUser(account.Id, user.Id);
        dbContext.CustomerAccountUsers.Add(accountMembership);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(dbContext, account.Id, user.Id, cancellationToken);
        await AuthorizationSeed.SeedTenantDefaultsAsync(dbContext, tenant.Id, user.Id, user.Id, cancellationToken);
        await AuthorizationSeed.EnsureGlobalRolesAsync(dbContext, cancellationToken);
        var defaultPlan = await dbContext.Plans.Include(x => x.Modules).SingleOrDefaultAsync(x => x.Code == "DEFAULT", cancellationToken);
        if (defaultPlan is null)
        {
            defaultPlan = new Plan("DEFAULT", "Plano padrão");
            defaultPlan.Modules.Add(new PlanModule(defaultPlan.Id, "SCHEDULING"));
            dbContext.Plans.Add(defaultPlan);
        }
        dbContext.TenantPlans.Add(new TenantPlan(tenant.Id, defaultPlan.Id));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await emailVerification.IssueAsync(user, cancellationToken);
        return Accepted();
    }
}

public sealed record RegistrationRequest(string Email, string Password, string OrganizationName, string UnitName);
