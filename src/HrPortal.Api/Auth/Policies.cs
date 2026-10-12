using HrPortal.Domain.Security;
using Microsoft.AspNetCore.Authorization;

namespace HrPortal.Api.Auth;

/// <summary>Named authorisation policies. Every endpoint must declare one (or AllowAnonymous) explicitly.</summary>
public static class Policies
{
    /// <summary>Any signed-in user.</summary>
    public const string Authenticated = nameof(Authenticated);

    public const string HrStaff = nameof(HrStaff);

    public const string HrAdmin = nameof(HrAdmin);

    public const string SystemAdmin = nameof(SystemAdmin);

    /// <summary>Change per-country settings (probation, notice period, ...).</summary>
    public const string ManageCountrySettings = nameof(ManageCountrySettings);

    public static void Configure(AuthorizationBuilder builder)
    {
        var authenticated = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        builder
            .SetFallbackPolicy(authenticated)
            .AddPolicy(Authenticated, authenticated)
            .AddPolicy(HrStaff, p => p.RequireRole(Roles.HrAdmin, Roles.HrExecutive))
            .AddPolicy(HrAdmin, p => p.RequireRole(Roles.HrAdmin))
            .AddPolicy(SystemAdmin, p => p.RequireRole(Roles.SystemAdmin))
            .AddPolicy(ManageCountrySettings, p => p.RequireRole(Roles.SystemAdmin, Roles.HrAdmin));
    }
}
