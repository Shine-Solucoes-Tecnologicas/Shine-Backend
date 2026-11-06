using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Options;
using Shine.Domain;
using System.Text.Json.Serialization;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants")]
public sealed class TenantsController(
    ShineDbContext dbContext,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IAccessTokenService accessTokenService,
    IOptions<JwtOptions> jwtOptions,
    IWebSessionTransport webSession,
    TenantSessionSelectionService tenantSelection) : ControllerBase
{
    [HttpGet("accessible")]
    public async Task<ActionResult<IReadOnlyCollection<AccessibleTenantResponse>>> Accessible(CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenants = await dbContext.UserTenants
            .AsNoTracking()
            .Where(link => link.UserId == userId && link.IsActive && link.Tenant.IsActive)
            .OrderBy(link => link.Tenant.Name)
            .Select(link => new AccessibleTenantResponse(link.TenantId, link.Tenant.Name))
            .ToArrayAsync(cancellationToken);
        return Ok(tenants);
    }

    [HttpPost]
    public async Task<ActionResult<TenantSelectionResponse>> Create(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenantName = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(tenantName) || tenantName.Length > 200)
            return AuthenticationErrors.Result(400, AuthenticationErrors.UnitNameInvalid,
                "O nome da unidade deve possuir entre 1 e 200 caracteres.", "name");
        if (await dbContext.Tenants.AnyAsync(tenant => tenant.Name.ToLower() == tenantName.ToLower(), cancellationToken))
            return AuthenticationErrors.Result(409, AuthenticationErrors.UnitNameConflict,
                "Já existe uma unidade com este nome.", "name");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var accountId = currentTenant.TenantId is Guid selectedTenantId
            ? await dbContext.Tenants.AsNoTracking().Where(x => x.Id == selectedTenantId).Select(x => x.CustomerAccountId).SingleOrDefaultAsync(cancellationToken)
            : null;
        CustomerAccount? newAccount = null;
        if (accountId is null)
        {
            newAccount = new CustomerAccount(tenantName);
            accountId = newAccount.Id;
            dbContext.CustomerAccounts.Add(newAccount);
            dbContext.CustomerAccountUsers.Add(new CustomerAccountUser(accountId.Value, userId));
        }
        var tenant = new Tenant(tenantName);
        tenant.AssignToCustomerAccount(accountId.Value);
        dbContext.Tenants.Add(tenant);
        dbContext.UserTenants.Add(new UserTenant(userId, tenant.Id, userId, isOwner: true));
        await dbContext.SaveChangesAsync(cancellationToken);
        if (newAccount is not null) await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(dbContext, newAccount.Id, userId, cancellationToken);
        await AuthorizationSeed.SeedTenantDefaultsAsync(dbContext, tenant.Id, userId, userId, cancellationToken);
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
        var roles = await dbContext.UserTenantRoles.Where(item => item.UserId == userId && item.TenantId == tenant.Id).Select(item => item.Role.Name).Distinct().ToArrayAsync(cancellationToken);
        var refresh = RefreshTokenHash.Create();
        var session = new RefreshToken(userId, tenant.Id, tenant.Users.Single().UserTenantId, refresh.Hash,
            DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays));
        dbContext.RefreshTokens.Add(session);
        var token = accessTokenService.Create(userId, session.Id, tenant.Id, tenant.Users.Single().UserTenantId, roles);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        webSession.WriteSession(HttpContext, refresh.Raw, session.ExpiresAtUtc);
        return Created($"/api/tenants/{tenant.Id}", new TenantSelectionResponse(
            tenant.Id, tenant.Name, token.Token, token.ExpiresAtUtc, webSession.ResponseRefreshToken(refresh.Raw)));
    }

    [HttpPost("select")]
    [HttpPost("switch")]
    public async Task<ActionResult<TenantSelectionResponse>> Select(TenantSelectionRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var credential = webSession.ReadRefreshToken(HttpContext, request.RefreshToken);
        if (credential is null)
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");
        if (!await webSession.ValidateAntiforgeryAsync(HttpContext, credential))
            return AuthenticationErrors.Result(400, AuthenticationErrors.CsrfValidationFailed,
                "Não foi possível validar a origem da requisição.");
        var result = await tenantSelection.SelectAsync(userId, request.TenantId, credential.RawToken, cancellationToken);
        if (result.Outcome == TenantSessionSelectionOutcome.Unauthorized)
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");
        if (result.Outcome == TenantSessionSelectionOutcome.Forbidden)
            return AuthenticationErrors.Result(403, AuthenticationErrors.UnitAccessDenied,
                "Você não possui acesso a esta unidade.");
        webSession.WriteSession(HttpContext, result.RefreshToken!, result.RefreshTokenExpiresAtUtc!.GetValueOrDefault());
        return Ok(new TenantSelectionResponse(
            result.TenantId!.Value,
            result.TenantName!,
            result.AccessToken!,
            result.AccessTokenExpiresAtUtc!.GetValueOrDefault(),
            webSession.ResponseRefreshToken(result.RefreshToken!)));
    }

    private Guid RequireUser() => currentUser.UserId ?? throw new UnauthorizedAccessException("An authenticated user is required.");
}

public sealed record TenantSelectionRequest(Guid TenantId, string? RefreshToken = null);
public sealed record CreateTenantRequest(string Name);
public sealed record TenantSelectionResponse(
    Guid TenantId,
    string TenantName,
    string AccessToken,
    DateTime ExpiresAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RefreshToken = null);
