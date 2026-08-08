using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(ShineDbContext dbContext, IPasswordHashService passwordHashService, IAccessTokenService accessTokenService) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized();

        var user = await dbContext.Users.Include(candidate => candidate.Tenants).ThenInclude(link => link.Tenant).SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == Shine.Domain.Identity.User.NormalizeEmail(request.Email), cancellationToken);

        if (user is null || !user.IsActive || !passwordHashService.Verify(request.Password, user.PasswordHash))
            return Unauthorized();

        var tenants = user.Tenants.Where(link => link.IsActive && link.Tenant.IsActive)
            .Select(link => new AccessibleTenantResponse(link.Tenant.Id, link.Tenant.Name)).ToArray();
        var accessToken = accessTokenService.Create(user.Id, null, null, []);
        return Ok(new LoginResponse(user.Id, user.Email, tenants, accessToken.Token, accessToken.ExpiresAtUtc));
    }
}
