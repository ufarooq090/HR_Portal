using HrPortal.Domain.Security;

namespace HrPortal.Domain.Templates;

/// <summary>What the API knows about the record an email would be sent from.</summary>
/// <param name="IsHr">The user is HR for the record's country (MC-03).</param>
/// <param name="IsHiringManager">The user is the hiring manager of the application's requisition.</param>
public sealed record SendFacts(string CountryCode, bool IsHr, bool IsHiringManager);

/// <summary>
/// Email Templates in the permission matrix: HR Admins manage templates; HR (Admins and Executives) use them for
/// records in their countries; hiring managers use interview templates for candidates on their own requisitions.
/// Termination communication is HR Admin only and never sent in bulk (rule 8).
/// </summary>
public static class TemplateRules
{
    public static bool CanManage(UserAccess access) =>
        access.HasAny(HrModule.EmailTemplates, Capability.Create) && access.HasAny(HrModule.EmailTemplates, Capability.Edit);

    /// <summary>Whether the user may see the template in their list (they may still not use it on every record).</summary>
    public static bool CanSee(UserAccess access, EmailTemplate template) =>
        CanManage(access) || (access.HasAny(HrModule.EmailTemplates, Capability.Use) && IsUsableByRole(access, template));

    /// <returns>Null when allowed; otherwise why not, for a 403.</returns>
    public static string? WhyNotSend(UserAccess access, EmailTemplate template, SendFacts facts, bool bulk)
    {
        if (!access.HasAny(HrModule.EmailTemplates, Capability.Use))
        {
            return "You cannot send emails from templates.";
        }

        if (template.IsRestricted)
        {
            if (!access.Roles.Contains(Roles.HrAdmin))
            {
                return "Only HR Admins can send termination communication.";
            }

            if (bulk)
            {
                return "Termination communication is sent to one employee at a time, never in bulk.";
            }
        }

        if (facts.IsHr)
        {
            return null;
        }

        return template.Category == TemplateCategory.Interview && template.RecordType == TemplateRecordType.Application && facts.IsHiringManager
            ? null
            : "You cannot send this email for this record.";
    }

    /// <summary>Hiring managers (without an HR role) only use interview templates.</summary>
    private static bool IsUsableByRole(UserAccess access, EmailTemplate template) =>
        access.Roles.Contains(Roles.HrAdmin)
        || (!template.IsRestricted && access.Roles.Contains(Roles.HrExecutive))
        || (template.Category == TemplateCategory.Interview && template.RecordType == TemplateRecordType.Application);
}
