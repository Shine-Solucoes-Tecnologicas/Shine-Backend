using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Shine.Infrastructure;

namespace Shine.Api;

public static class JwtAuthenticationConfiguration
{
    public static IServiceCollection AddShineJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, JwtSigningKeyRing>((options, jwtOptions, keyRing) =>
            {
                var settings = jwtOptions.Value;
                options.MapInboundClaims = true;
                options.TokenValidationParameters = JwtTokenValidation.Create(settings, keyRing);
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(AuthenticationErrors.Single(
                            AuthenticationErrors.SessionInvalid,
                            "A sessão é inválida ou expirou."));
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(AuthenticationErrors.Single(
                            AuthenticationErrors.UnitAccessDenied,
                            "Você não possui acesso a esta operação."));
                    }
                };
            });
        return services;
    }
}
