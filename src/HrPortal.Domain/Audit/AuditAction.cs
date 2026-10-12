namespace HrPortal.Domain.Audit;

public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,

    /// <summary>Explicitly recorded read of sensitive data (salary, PIP, performance).</summary>
    Viewed = 4,
}
