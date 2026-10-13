using HrPortal.Api.Auth.DevAuth;
using HrPortal.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.IntegrationTests.Infrastructure;

/// <summary>
/// Facts about the data <see cref="DevDataSeeder"/> creates, and helpers to change a dev user's HR access for one test.
/// Employee numbers per country: 0001 HR Admin, 0002 HR Executive, 0003 System Admin, 0004 Hiring Manager,
/// 0005 Line Manager, 0006 Employee, 0007 Interviewer, 0008 Junior Engineer, 0009 Finance Analyst; plus PK-0010, the
/// CEO (dev-ceo), who is the single requisition approver in both countries.
/// </summary>
public static class SeedData
{
    public static IEnumerable<string> AllDevUsers => DevUsers.All.Select(u => u.Id);

    /// <summary>Seeded employee numbers look like PK-0001; test-created ones start with "T-".</summary>
    public static bool IsSeeded(string employeeNumber) =>
        System.Text.RegularExpressions.Regex.IsMatch(employeeNumber, "^(PK|US)-[0-9]{4}$");

    public static TheoryData<string> DevUsersData => new(AllDevUsers);

    public static TheoryData<string> DevUsersExceptHrAdmins =>
        new(DevUsers.All.Where(u => !u.Roles.Contains(Roles.HrAdmin)).Select(u => u.Id));

    /// <summary>Employee numbers each dev user may see in the directory, from the seeded reporting lines.</summary>
    public static IReadOnlyList<string> VisibleTo(string devUserId)
    {
        var user = DevUsers.Find(devUserId) ?? throw new ArgumentException(devUserId);
        if (user == DevUsers.Ceo)
        {
            return ["PK-0010"];
        }

        var cc = user.CountryCode;
        string[] Numbers(params int[] n) => n.Select(i => $"{cc}-{i:D4}").ToArray();

        // Pakistan also has the CEO, PK-0010.
        var wholeCountry = cc == "PK" ? Numbers(1, 2, 3, 4, 5, 6, 7, 8, 9, 10) : Numbers(1, 2, 3, 4, 5, 6, 7, 8, 9);

        return (user.Roles[0] switch
        {
            Roles.HrAdmin => [.. wholeCountry, .. OtherCountry(cc)],
            Roles.HrExecutive => wholeCountry,
            Roles.SystemAdmin => Numbers(3),
            Roles.HiringManager => Numbers(4, 5, 6, 7, 8),
            Roles.LineManager => Numbers(5, 6, 8),
            Roles.Interviewer => Numbers(7),
            Roles.Employee => Numbers(6),
            _ => throw new ArgumentOutOfRangeException(devUserId),
        }).Order(StringComparer.Ordinal).ToList();
    }

    private static string[] OtherCountry(string cc)
    {
        var other = cc == "PK" ? "US" : "PK";
        return Enumerable.Range(1, other == "PK" ? 10 : 9).Select(i => $"{other}-{i:D4}").ToArray();
    }

    public static Task<Guid> EmployeeIdAsync(this HrPortalApiFactory factory, string employeeNumber) =>
        factory.WithDbAsync(db => db.Employees.Where(e => e.EmployeeNumber == employeeNumber).Select(e => e.Id).SingleAsync());

    /// <summary>Sets a dev user's HR access settings directly; dispose to restore the defaults.</summary>
    public static async Task<IAsyncDisposable> SetHrAccessAsync(
        this HrPortalApiFactory factory, string devUserId, CountryScope countries, bool compensation = false)
    {
        await factory.WithDbAsync(async db =>
        {
            var employeeId = await db.Employees.Where(e => e.UserObjectId == devUserId).Select(e => e.Id).SingleAsync();
            var existing = await db.HrAccessSettings.FindAsync(employeeId);
            if (existing is null)
            {
                db.HrAccessSettings.Add(new HrAccessSettings(employeeId, countries, compensation));
            }
            else
            {
                existing.Update(countries, compensation);
            }

            return await db.SaveChangesAsync();
        });

        return new Restore(() => factory.ResetHrAccessAsync(devUserId));
    }

    public static Task ResetHrAccessAsync(this HrPortalApiFactory factory, string devUserId) =>
        factory.WithDbAsync(db => db.HrAccessSettings
            .Where(s => db.Employees.Any(e => e.Id == s.EmployeeId && e.UserObjectId == devUserId))
            .ExecuteDeleteAsync());

    private sealed class Restore(Func<Task> restore) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await restore();
    }
}
