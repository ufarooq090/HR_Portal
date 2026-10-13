namespace HrPortal.Domain.Security;

/// <summary>Resolves the current user's <see cref="UserAccess"/>, once per request.</summary>
public interface IAccessContext
{
    Task<UserAccess> GetAsync(CancellationToken cancellationToken = default);
}
