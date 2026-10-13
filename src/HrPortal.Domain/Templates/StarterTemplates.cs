namespace HrPortal.Domain.Templates;

/// <param name="Key">Stable identifier, so a starter template is only ever added once.</param>
public sealed record StarterTemplate(string Key, string Trigger, TemplateContent Content);

/// <summary>
/// The starter templates from the spec ("Email Template System"). Added once to every environment; HR edit them
/// freely afterwards and seeding never overwrites their changes. Placeholder wording until HR share their own.
/// </summary>
public static class StarterTemplates
{
    private const string Regards = "<p>Kind regards,<br>{{sender_name}}<br>{{company_name}}</p>";

    private static TemplateContent Content(
        string name, TemplateCategory category, TemplateRecordType recordType, string subject, string body, bool ccManager = false) =>
        new(name, category, recordType, subject, body + Regards, null, SenderMailbox.HrMailbox, ccManager, IsActive: true);

    public static readonly IReadOnlyList<StarterTemplate> All =
    [
        new("interview-confirmation", "Interview scheduled or rescheduled (candidate; the panel gets a calendar invite)", Content(
            "Interview confirmation", TemplateCategory.Interview, TemplateRecordType.Application,
            "Your interview for {{designation}} on {{interview_date}}",
            "<p>Dear {{first_name}},</p>" +
            "<p>Thank you for your interest in the <strong>{{designation}}</strong> role at {{company_name}}. We are pleased to confirm your interview:</p>" +
            "<ul><li><strong>Date:</strong> {{interview_date}}</li><li><strong>Time:</strong> {{interview_time}}</li>" +
            "<li><strong>Mode:</strong> {{interview_mode}}</li><li><strong>Where:</strong> {{interview_location}}</li>" +
            "<li><strong>Panel:</strong> {{panel_names}}</li></ul>" +
            "<p>If you need to change the time, please reply to this email.</p>")),
        new("interview-reminder", "24 hours before the interview (candidate)", Content(
            "Interview reminder", TemplateCategory.Interview, TemplateRecordType.Application,
            "Reminder: your interview tomorrow at {{interview_time}}",
            "<p>Dear {{first_name}},</p>" +
            "<p>This is a reminder of your interview for <strong>{{designation}}</strong> on {{interview_date}} at {{interview_time}} ({{interview_mode}}, {{interview_location}}).</p>" +
            "<p>We look forward to speaking with you.</p>")),
        new("rejection", "Status set to Rejected (candidate)", Content(
            "Rejection notification", TemplateCategory.Rejection, TemplateRecordType.Application,
            "Your application for {{designation}}",
            "<p>Dear {{first_name}},</p>" +
            "<p>Thank you for your interest in the <strong>{{designation}}</strong> role and for the time you gave us during the process.</p>" +
            "<p>After careful consideration, we have decided not to move forward with your application on this occasion. " +
            "We will keep your details on file and may contact you about future roles that match your experience.</p>" +
            "<p>We wish you every success.</p>")),
        new("offer", "Offer letter generated (candidate)", Content(
            "Offer mail", TemplateCategory.Offer, TemplateRecordType.Application,
            "Offer of employment: {{designation}}",
            "<p>Dear {{first_name}},</p>" +
            "<p>We are delighted to offer you the position of <strong>{{designation}}</strong> at {{company_name}}. Your offer letter is attached.</p>" +
            "<p>Please review it and let us know your decision. {{hiring_manager_name}} and the HR team are happy to answer any questions.</p>")),
        new("onboarding-welcome", "Offer accepted, or 3 days before joining (new joiner)", Content(
            "Onboarding welcome", TemplateCategory.Onboarding, TemplateRecordType.Employee,
            "Welcome to {{company_name}}, {{first_name}}!",
            "<p>Dear {{first_name}},</p>" +
            "<p>We are excited to welcome you as <strong>{{designation}}</strong> in {{department}}. Your first day is <strong>{{joining_date}}</strong> at our {{office}} office.</p>" +
            "<p>Your manager, {{manager_name}}, will meet you on your first day. We will send any documents we need from you separately.</p>")),
        new("document-request", "Onboarding started (new joiner)", Content(
            "Document request", TemplateCategory.Onboarding, TemplateRecordType.Employee,
            "Documents needed before you join on {{joining_date}}",
            "<p>Dear {{first_name}},</p>" +
            "<p>To complete your onboarding before you join on {{joining_date}}, please send us the documents on your country's checklist. " +
            "We will share a secure upload link.</p>" +
            "<p>If you have any questions, reply to this email.</p>")),
        new("probation-completion", "Probation review outcome = Confirm (employee, manager in CC)", Content(
            "Probation completion notice", TemplateCategory.Probation, TemplateRecordType.Employee,
            "Confirmation of your employment",
            "<p>Dear {{first_name}},</p>" +
            "<p>We are pleased to confirm that you have successfully completed your probation as <strong>{{designation}}</strong>. " +
            "Your employment is confirmed.</p>" +
            "<p>Thank you for your contribution so far; {{manager_name}} is copied on this email.</p>", ccManager: true)),
        new("probation-extension", "Probation review outcome = Extend (employee, manager in CC)", Content(
            "Probation extension notice", TemplateCategory.Probation, TemplateRecordType.Employee,
            "Extension of your probation period",
            "<p>Dear {{first_name}},</p>" +
            "<p>Following your probation review, your probation as <strong>{{designation}}</strong> has been extended until <strong>{{probation_end_date}}</strong>.</p>" +
            "<p>{{manager_name}}, who is copied on this email, will agree the next steps and objectives with you.</p>", ccManager: true)),
        new("termination", "Manual only, HR Admin (employee)", Content(
            "Termination communication", TemplateCategory.Termination, TemplateRecordType.Employee,
            "Termination of employment",
            "<p>Dear {{first_name}},</p>" +
            "<p>This email confirms that your employment as {{designation}} with {{company_name}} ends on <strong>{{last_working_date}}</strong>.</p>" +
            "<p>HR will contact you about your final settlement, the return of company property and your experience letter.</p>")),
        new("resignation-acknowledgement", "Resignation recorded (employee)", Content(
            "Resignation acknowledgement", TemplateCategory.Separation, TemplateRecordType.Employee,
            "Your resignation has been received",
            "<p>Dear {{first_name}},</p>" +
            "<p>We acknowledge your resignation from the position of {{designation}}. Your last working date will be <strong>{{last_working_date}}</strong>.</p>" +
            "<p>HR will be in touch about handover, clearance and your exit interview. Thank you for your contribution to {{company_name}}.</p>")),
        new("requisition-approval", "Requisition submitted (approver)", Content(
            "Requisition approval request", TemplateCategory.Requisition, TemplateRecordType.Requisition,
            "Approval needed: {{requisition_number}} {{requisition_title}}",
            "<p>Dear {{first_name}},</p>" +
            "<p>{{requester_name}} has raised requisition <strong>{{requisition_number}}: {{requisition_title}}</strong> " +
            "({{openings}} opening(s) in {{department}}, {{office}}; target date {{target_date}}) and it is waiting for your approval.</p>" +
            "<p><a href=\"{{requisition_link}}\">Review the requisition</a></p>")),
        new("pip-initiation", "PIP approved by HR Admin (employee, manager in CC)", Content(
            "PIP initiation notice", TemplateCategory.Performance, TemplateRecordType.Employee,
            "Your performance improvement plan",
            "<p>Dear {{first_name}},</p>" +
            "<p>As discussed with {{manager_name}}, who is copied on this email, a performance improvement plan has been put in place for your role as {{designation}}. " +
            "It sets out the goals, support and review dates agreed with you.</p>" +
            "<p>Please speak to your manager or HR if you have any questions.</p>", ccManager: true)),
        new("pip-outcome", "PIP closed as completed or extended (employee, manager in CC)", Content(
            "PIP outcome notice", TemplateCategory.Performance, TemplateRecordType.Employee,
            "Outcome of your performance improvement plan",
            "<p>Dear {{first_name}},</p>" +
            "<p>Your performance improvement plan has now been reviewed with {{manager_name}}, who is copied on this email. " +
            "The outcome and any next steps are set out in the plan.</p>" +
            "<p>Thank you for your effort during this period.</p>", ccManager: true)),
    ];
}
