using HrPortal.Domain.Security;
using Microsoft.AspNetCore.Authorization;

namespace HrPortal.Api.Auth;

/// <summary>Named authorisation policies. Every endpoint must declare one (or AllowAnonymous) explicitly.</summary>
/// <remarks>
/// Policies decide whether a role may call an endpoint at all. Which records it returns is decided by the
/// user's data scope, applied in the query (see <c>EmployeeScopeQueries</c>).
/// </remarks>
public static class Policies
{
    /// <summary>Any signed-in user.</summary>
    public const string Authenticated = nameof(Authenticated);

    public const string HrStaff = nameof(HrStaff);

    public const string HrAdmin = nameof(HrAdmin);

    public const string SystemAdmin = nameof(SystemAdmin);

    /// <summary>Change per-country settings (probation, notice period, time zone, ...).</summary>
    public const string ManageCountrySettings = nameof(ManageCountrySettings);

    /// <summary>Manage legal entities, office locations, departments and grades (NFR-12).</summary>
    public const string ManageOrganisation = nameof(ManageOrganisation);

    /// <summary>Change HR users' country and compensation access (MC-03).</summary>
    public const string ManageHrAccess = nameof(ManageHrAccess);

    private static readonly Capability[] SingleCapabilities =
        [Capability.View, Capability.Create, Capability.Edit, Capability.Delete, Capability.Approve, Capability.Use];

    /// <summary>Roles that may hold <paramref name="capability"/> on <paramref name="module"/> per the permission matrix.</summary>
    public static string Module(HrModule module, Capability capability) => $"Module:{module}:{capability}";

    public static void Configure(AuthorizationBuilder builder)
    {
        var authenticated = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        builder
            .SetFallbackPolicy(authenticated)
            .AddPolicy(Authenticated, authenticated)
            .AddPolicy(HrStaff, p => p.RequireRole(Roles.HrAdmin, Roles.HrExecutive))
            .AddPolicy(HrAdmin, p => p.RequireRole(Roles.HrAdmin))
            .AddPolicy(SystemAdmin, p => p.RequireRole(Roles.SystemAdmin))
            .AddPolicy(ManageCountrySettings, p => p.RequireRole(Roles.SystemAdmin, Roles.HrAdmin))
            .AddPolicy(ManageOrganisation, p => p.RequireRole(Roles.HrAdmin))
            .AddPolicy(ManageHrAccess, p => p.RequireRole(Roles.HrAdmin));

        foreach (var module in Enum.GetValues<HrModule>())
        {
            foreach (var capability in SingleCapabilities)
            {
                var roles = PermissionMatrix.RolesWith(module, capability);
                builder.AddPolicy(Module(module, capability), p =>
                {
                    if (roles.Count == 0)
                    {
                        p.RequireAssertion(_ => false);
                    }
                    else
                    {
                        p.RequireRole(roles);
                    }
                });
            }
        }
    }
}
