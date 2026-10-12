using HrPortal.Domain.Security;

namespace HrPortal.Api.Auth.DevAuth;

public sealed record DevUser(string Id, string DisplayName, string Email, string CountryCode, IReadOnlyList<string> Roles);

/// <summary>Seeded test users for local development and E2E tests: one per role in each country.</summary>
public static class DevUsers
{
    public static readonly IReadOnlyList<DevUser> All = Build();

    public static DevUser? Find(string id) => All.FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase));

    private static List<DevUser> Build()
    {
        var users = new List<DevUser>();
        foreach (var country in new[] { "PK", "US" })
        {
            foreach (var role in Roles.All)
            {
                var id = $"dev-{country}-{role}".ToLowerInvariant();
                IReadOnlyList<string> roles = role == Roles.Employee ? [role] : [role, Roles.Employee];
                users.Add(new DevUser(id, $"Dev {role} ({country})", $"{id}@hrportal.test", country, roles));
            }
        }

        return users;
    }
}
