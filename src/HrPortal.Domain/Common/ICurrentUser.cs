namespace HrPortal.Domain.Common;

/// <summary>The authenticated user performing the current operation.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Stable identifier (Entra object id, or the dev user id locally). "system" for background work.</summary>
    string UserId { get; }

    string DisplayName { get; }

    IReadOnlySet<string> Roles { get; }

    bool IsInRole(string role) => Roles.Contains(role);
}
