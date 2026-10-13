using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace HrPortal.Infrastructure.Recruiting;

/// <summary>RB-04 filters. Salary is encrypted at field level, so it is filtered after the rest, in memory.</summary>
public sealed record ResumeBankFilters(
    string? Query = null,
    IReadOnlyList<string>? Skills = null,
    decimal? MinExperience = null,
    decimal? MaxExperience = null,
    string? Location = null,
    int? MaxNoticeDays = null,
    ResumeBankStatus? Status = null,
    DateOnly? ContactedSince = null,
    string? Tag = null,
    string? CountryCode = null);

public static class ResumeBankQueries
{
    /// <summary>HR see candidates based in their countries, hiring managers those in their own country (MC-03); others no one.</summary>
    public static IQueryable<CandidateProfile> VisibleTo(this IQueryable<CandidateProfile> profiles, UserAccess access)
    {
        var scopes = access.ScopesFor(HrModule.ResumeBank, Capability.View);
        if (scopes.Contains(DataScope.All) || (scopes.Contains(DataScope.Countries) && access.HrCountries.IsAll))
        {
            return profiles;
        }

        if (scopes.Contains(DataScope.Countries))
        {
            var countries = access.HrCountries.Codes.ToList();
            return profiles.Where(p => countries.Contains(p.CountryCode));
        }

        if (scopes.Contains(DataScope.HomeCountry))
        {
            var home = access.HomeCountryCode;
            return profiles.Where(p => p.CountryCode == home);
        }

        return profiles.Where(_ => false);
    }

    /// <summary>
    /// RB-04: the free-text query matches inside any of the candidate's CVs (PostgreSQL full-text search with English
    /// stemming, web-search syntax: quotes for phrases, "or", "-" to exclude) or their name, email or employer.
    /// </summary>
    public static IQueryable<CandidateProfile> Search(this IQueryable<CandidateProfile> profiles, HrPortalDbContext db, ResumeBankFilters f)
    {
        if (!string.IsNullOrWhiteSpace(f.Query))
        {
            var text = f.Query.Trim();
            var pattern = $"%{text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal)}%";
            profiles = profiles.Where(p =>
                db.CandidateCvs.Any(cv => cv.PersonId == p.PersonId
                    && EF.Property<NpgsqlTsVector>(cv, CandidateCvConfiguration.SearchVector).Matches(EF.Functions.WebSearchToTsQuery("english", text)))
                || db.People.Any(x => x.Id == p.PersonId
                    && (EF.Functions.ILike(x.GivenName + " " + x.FamilyName, pattern) || (x.PersonalEmail != null && EF.Functions.ILike(x.PersonalEmail, pattern))))
                || (p.CurrentEmployer != null && EF.Functions.ILike(p.CurrentEmployer, pattern)));
        }

        foreach (var skill in (f.Skills ?? []).Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct())
        {
            profiles = profiles.Where(p => p.Skills.Contains(skill));
        }

        if (!string.IsNullOrWhiteSpace(f.Tag))
        {
            var tag = f.Tag.Trim().ToLowerInvariant();
            profiles = profiles.Where(p => p.Tags.Contains(tag));
        }

        if (f.MinExperience is { } min)
        {
            profiles = profiles.Where(p => p.ExperienceYears >= min);
        }

        if (f.MaxExperience is { } max)
        {
            profiles = profiles.Where(p => p.ExperienceYears <= max);
        }

        if (!string.IsNullOrWhiteSpace(f.Location))
        {
            var location = $"%{f.Location.Trim()}%";
            profiles = profiles.Where(p => p.Location != null && EF.Functions.ILike(p.Location, location));
        }

        if (f.MaxNoticeDays is { } notice)
        {
            profiles = profiles.Where(p => p.NoticePeriodDays <= notice);
        }

        if (f.Status is { } status)
        {
            profiles = profiles.Where(p => p.Status == status);
        }

        if (f.ContactedSince is { } since)
        {
            var from = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            profiles = profiles.Where(p => p.LastContactedAtUtc >= from);
        }

        if (!string.IsNullOrWhiteSpace(f.CountryCode))
        {
            var country = f.CountryCode.Trim().ToUpperInvariant();
            profiles = profiles.Where(p => p.CountryCode == country);
        }

        return profiles;
    }
}
