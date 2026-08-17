using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Shine.Api;

public sealed class ShineAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.User.Identity?.IsAuthenticated != true)
            return Task.FromResult(AuthenticateResult.NoResult());

        var ticket = new AuthenticationTicket(Context.User, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
