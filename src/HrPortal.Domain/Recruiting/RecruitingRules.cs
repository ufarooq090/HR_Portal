using HrPortal.Domain.Security;

namespace HrPortal.Domain.Recruiting;

/// <summary>Facts about one application that decide who may see and do what with it.</summary>
public sealed record ApplicationFacts(string CountryCode, Guid RequesterEmployeeId, Guid HiringManagerEmployeeId, bool ViewerIsPanelist);

/// <summary>What one user may do with one application. The UI hides controls with it; the API enforces it.</summary>
public sealed record ApplicationPermissions(
    bool CanView,
    bool CanManage,
    bool CanSeeCompensation,
    bool CanSeeNotes,
    bool CanSeeAllFeedback);

/// <summary>
/// Candidate Tracker access, on top of the permission matrix ("Candidate Tracker": HR full within their countries;
/// Hiring Manager their own requisitions; Interviewer assigned candidates only):
/// <list type="bullet">
/// <item>HR (Admin, Executive) see and manage every application in their countries (MC-03).</item>
/// <item>Hiring managers see the applications on requisitions they raised or are named hiring manager for, including
/// salary figures and notes, and give feedback when on a panel; they do not move candidates.</item>
/// <item>Anyone on an interview panel sees that candidate, their CV and their interviews, but not salary figures or
/// notes, and other panelists' feedback only once they have submitted their own (CT-07).</item>
/// </list>
/// </summary>
public static class RecruitingRules
{
    public static bool IsHrFor(UserAccess access, string countryCode, Capability capability)
    {
        var scopes = access.ScopesFor(HrModule.Candidates, capability);
        return scopes.Contains(DataScope.All) || (scopes.Contains(DataScope.Countries) && access.HrCountries.Includes(countryCode));
    }

    /// <summary>Hiring managers' "own requisitions".</summary>
    public static bool IsHiringManagerFor(UserAccess access, Guid requesterEmployeeId, Guid hiringManagerEmployeeId) =>
        access.Roles.Contains(Roles.HiringManager)
        && access.EmployeeId is { } me
        && (me == hiringManagerEmployeeId || me == requesterEmployeeId);

    public static ApplicationPermissions For(UserAccess access, ApplicationFacts facts)
    {
        var hr = IsHrFor(access, facts.CountryCode, Capability.View);
        var hiringManager = IsHiringManagerFor(access, facts.RequesterEmployeeId, facts.HiringManagerEmployeeId);
        var insider = hr || hiringManager;
        return new ApplicationPermissions(
            CanView: insider || facts.ViewerIsPanelist,
            CanManage: IsHrFor(access, facts.CountryCode, Capability.Edit),
            CanSeeCompensation: insider,
            CanSeeNotes: insider,
            CanSeeAllFeedback: insider);
    }

    /// <summary>
    /// CT-07: which scorecards of one interview the viewer may read. A panelist who has not submitted sees none of the
    /// others' (whatever their role); after submitting they see the whole panel's. HR and the hiring manager see all.
    /// </summary>
    public static IReadOnlyList<Scorecard> VisibleScorecards(
        Interview interview, IReadOnlyList<Scorecard> scorecards, Guid? viewerEmployeeId, bool canSeeAllFeedback)
    {
        var onPanel = viewerEmployeeId is { } viewer && interview.HasPanelist(viewer);
        var submitted = onPanel && scorecards.Any(s => s.InterviewerEmployeeId == viewerEmployeeId);

        if (onPanel && !submitted)
        {
            return [];
        }

        return submitted || canSeeAllFeedback ? scorecards : [];
    }
}
