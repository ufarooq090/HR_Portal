namespace HrPortal.Domain.Common;

/// <summary>
/// Marks a property holding sensitive data (compensation, bank details, CNIC, SSN).
/// Infrastructure encrypts it at field level and the audit trail masks its values.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAttribute : Attribute;
