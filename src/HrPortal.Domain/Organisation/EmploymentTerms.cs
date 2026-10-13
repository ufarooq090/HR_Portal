using HrPortal.Domain.Common;

namespace HrPortal.Domain.Organisation;

/// <summary>Probation and notice period. The country sets the default; a grade may override either.</summary>
public readonly record struct EmploymentTerms(int ProbationMonths, int NoticePeriodDays)
{
    public static EmploymentTerms For(CountrySettings country, Grade? grade)
    {
        if (grade is not null && grade.CountryCode != country.CountryCode)
        {
            throw new ArgumentException("Grade belongs to a different country.", nameof(grade));
        }

        return new EmploymentTerms(
            grade?.ProbationMonths ?? country.ProbationMonths,
            grade?.NoticePeriodDays ?? country.NoticePeriodDays);
    }

    internal static int ValidProbation(string field, int months) =>
        Guard.Range(field, months, 0, 24, "Probation must be between 0 and 24 months.");

    internal static int ValidNotice(string field, int days) =>
        Guard.Range(field, days, 0, 365, "Notice period must be between 0 and 365 days.");
}
