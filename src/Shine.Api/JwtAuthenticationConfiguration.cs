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
            });
        return services;
    }
}
