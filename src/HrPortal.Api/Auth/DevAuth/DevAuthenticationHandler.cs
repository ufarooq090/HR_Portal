using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HrPortal.Api.Auth.DevAuth;

/// <summary>
/// Development/Test-only scheme that signs in as a seeded <see cref="DevUser"/> named in the
/// <see cref="HeaderName"/> request header. Startup fails if it is enabled in any other environment.
/// </summary>
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";
    public const string HeaderName = "X-Dev-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var user = DevUsers.Find(values.ToString());
        if (user is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown dev user."));
        }

        var claims = new List<Claim>
        {
            new(HrClaimTypes.ObjectId, user.Id),
            new(HrClaimTypes.Name, user.DisplayName),
            new(HrClaimTypes.PreferredUsername, user.Email),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(HrClaimTypes.Roles, r)));

        var identity = new ClaimsIdentity(claims, SchemeName, HrClaimTypes.Name, HrClaimTypes.Roles);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
