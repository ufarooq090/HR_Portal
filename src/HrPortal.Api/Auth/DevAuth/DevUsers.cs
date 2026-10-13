using HrPortal.Domain.Security;

namespace HrPortal.Api.Auth.DevAuth;

public sealed record DevUser(string Id, string DisplayName, string Email, string CountryCode, IReadOnlyList<string> Roles);

/// <summary>
/// Seeded test users for local development and E2E tests: one per role in each country, plus a CEO, an ordinary
/// employee who is the default requisition approver in both countries (REQ-02).
/// </summary>
public static class DevUsers
{
    public static readonly DevUser Ceo = new("dev-ceo", "Dev CEO", "dev-ceo@hrportal.test", "PK", [Roles.Employee]);

    /// <summary>The per-role users only.</summary>
    public static readonly IReadOnlyList<DevUser> ByRole = BuildByRole();

    public static readonly IReadOnlyList<DevUser> All = [.. ByRole, Ceo];

    public static DevUser? Find(string id) => All.FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string IdFor(string role, string country) => $"dev-{country}-{role}".ToLowerInvariant();

    private static List<DevUser> BuildByRole()
    {
        var users = new List<DevUser>();
        foreach (var country in new[] { "PK", "US" })
        {
            foreach (var role in Roles.All)
            {
                var id = IdFor(role, country);
                IReadOnlyList<string> roles = role == Roles.Employee ? [role] : [role, Roles.Employee];
                users.Add(new DevUser(id, $"Dev {role} ({country})", $"{id}@hrportal.test", country, roles));
            }
        }

        return users;
    }
}
