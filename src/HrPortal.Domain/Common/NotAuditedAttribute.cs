namespace HrPortal.Domain.Common;

/// <summary>
/// Marks a large derived property (e.g. a CV's extracted text) whose value is left out of audit rows; the audit
/// records that it changed, not its content.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;
