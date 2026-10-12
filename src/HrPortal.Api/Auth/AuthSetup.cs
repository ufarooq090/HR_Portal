using System.IdentityModel.Tokens.Jwt;
using HrPortal.Api.Auth.DevAuth;
using HrPortal.Domain.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Identity.Web;

namespace HrPortal.Api.Auth;

public static class AuthSetup
{
    public static WebApplicationBuilder AddHrPortalAuth(this WebApplicationBuilder builder)
    {
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

        if (DevAuthGuard.Check(builder.Configuration, builder.Environment))
        {
            builder.Services
                .AddAuthentication(DevAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, null);
        }
        else
        {
            builder.Services
                .AddAuthentication()
                .AddMicrosoftIdentityWebApi(
                    jwt =>
                    {
                        builder.Configuration.Bind("AzureAd", jwt);
                        jwt.MapInboundClaims = false;
                        jwt.TokenValidationParameters.NameClaimType = HrClaimTypes.Name;
                        jwt.TokenValidationParameters.RoleClaimType = HrClaimTypes.Roles;
                    },
                    identity => builder.Configuration.Bind("AzureAd", identity));
        }

        Policies.Configure(builder.Services.AddAuthorizationBuilder());

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return builder;
    }
}
