using System.Net;
using System.Text.RegularExpressions;

namespace HrPortal.Domain.Templates;

/// <param name="Key">Written in templates as {{key}}.</param>
public sealed record MergeField(string Key, string Label, string Description, IReadOnlyList<TemplateRecordType> RecordTypes);

/// <summary>The result of filling merge fields: the text, and the fields that had no value (which block sending).</summary>
public sealed record FilledText(string Text, IReadOnlyList<string> Missing);

/// <summary>
/// ET-02: the merge fields HR pick from, and which records supply them. Values come from the record the email is
/// sent from; a field with no value is reported as missing and blocks sending.
/// </summary>
public static partial class MergeFields
{
    private static readonly TemplateRecordType[] All = [TemplateRecordType.Application, TemplateRecordType.Employee, TemplateRecordType.Requisition];
    private static readonly TemplateRecordType[] Candidate = [TemplateRecordType.Application];
    private static readonly TemplateRecordType[] Staff = [TemplateRecordType.Employee];
    private static readonly TemplateRecordType[] Hiring = [TemplateRecordType.Application, TemplateRecordType.Requisition];

    public static readonly IReadOnlyList<MergeField> Catalog =
    [
        new("first_name", "First name", "The recipient's first name.", All),
        new("last_name", "Last name", "The recipient's last name.", All),
        new("full_name", "Full name", "The recipient's full name.", All),
        new("designation", "Designation", "The job title: the requisition's title for candidates, the current designation for employees.", All),
        new("department", "Department", "The requisition's or employee's department.", All),
        new("office", "Office", "The requisition's or employee's office.", All),
        new("company_name", "Company name", "The legal entity, or the company name for candidates.", All),
        new("sender_name", "Sender name", "The person sending the email.", All),
        new("requisition_number", "Requisition number", "e.g. REQ-00042.", Hiring),
        new("requisition_title", "Requisition title", "The position's title.", Hiring),
        new("hiring_manager_name", "Hiring manager", "The requisition's hiring manager.", Hiring),
        new("interview_date", "Interview date", "The next scheduled interview's date, in the office's time zone.", Candidate),
        new("interview_time", "Interview time", "The next scheduled interview's start time, with the time zone.", Candidate),
        new("interview_round", "Interview round", "The next scheduled interview's round number.", Candidate),
        new("interview_mode", "Interview mode", "In person, Teams or phone.", Candidate),
        new("interview_location", "Interview location", "The address or meeting link.", Candidate),
        new("panel_names", "Panel names", "The interviewers on the next scheduled interview.", Candidate),
        new("joining_date", "Joining date", "The employee's joining date (for candidates, once they have joined).", [TemplateRecordType.Application, TemplateRecordType.Employee]),
        new("manager_name", "Manager name", "The employee's manager, or the hiring manager for candidates.", [TemplateRecordType.Application, TemplateRecordType.Employee]),
        new("employee_number", "Employee number", "e.g. PK-0042.", Staff),
        new("probation_end_date", "Probation end date", "When probation ends.", Staff),
        new("last_working_date", "Last working date", "Set when notice, resignation or termination is recorded.", Staff),
        new("requester_name", "Requested by", "Who raised the requisition.", [TemplateRecordType.Requisition]),
        new("openings", "Openings", "Number of positions.", [TemplateRecordType.Requisition]),
        new("target_date", "Target date", "When the position should be filled.", [TemplateRecordType.Requisition]),
        new("requisition_link", "Requisition link", "Link to the requisition in the portal.", [TemplateRecordType.Requisition]),
    ];

    public static IReadOnlyList<MergeField> For(TemplateRecordType recordType) => Catalog.Where(f => f.RecordTypes.Contains(recordType)).ToList();

    /// <summary>Distinct field names used in the text, in order of first use.</summary>
    public static IReadOnlyList<string> UsedIn(string text) =>
        Pattern().Matches(text).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();

    /// <summary>Fields used in the text that this kind of record cannot fill (including misspelt ones).</summary>
    public static IReadOnlyList<string> UnknownIn(string text, TemplateRecordType recordType)
    {
        var known = For(recordType).Select(f => f.Key).ToHashSet();
        return UsedIn(text).Where(f => !known.Contains(f)).ToList();
    }

    /// <summary>
    /// Replaces each {{field}} with its value. Values are HTML-encoded when <paramref name="html"/> is set. Fields
    /// without a value are left in place and reported, so HR can see where they are.
    /// </summary>
    public static FilledText Fill(string text, IReadOnlyDictionary<string, string?> values, bool html)
    {
        var missing = new List<string>();
        var filled = Pattern().Replace(text, m =>
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return html ? WebUtility.HtmlEncode(value) : value;
            }

            if (!missing.Contains(key))
            {
                missing.Add(key);
            }

            return m.Value;
        });
        return new FilledText(filled, missing);
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Pattern();
}
