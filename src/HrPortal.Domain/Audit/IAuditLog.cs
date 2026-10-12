namespace HrPortal.Domain.Audit;

/// <summary>
/// Explicit audit records that the change interceptor cannot see, such as views of
/// salary, PIP and performance data.
/// </summary>
public interface IAuditLog
{
    Task RecordViewAsync(string entityType, string entityId, CancellationToken cancellationToken = default);
}
