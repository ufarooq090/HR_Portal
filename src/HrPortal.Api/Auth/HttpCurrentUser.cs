using HrPortal.Domain.Common;

namespace HrPortal.Api.Auth;

/// <summary><see cref="ICurrentUser"/> backed by the HTTP request; "system" when there is no request (background jobs).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string SystemUserId = "system";

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public string UserId => IsAuthenticated
        ? accessor.HttpContext!.User.FindFirst(HrClaimTypes.ObjectId)?.Value ?? "unknown"
        : SystemUserId;

    public string DisplayName => IsAuthenticated
        ? accessor.HttpContext!.User.FindFirst(HrClaimTypes.Name)?.Value ?? UserId
        : "System";

    public IReadOnlySet<string> Roles => IsAuthenticated
        ? accessor.HttpContext!.User.FindAll(HrClaimTypes.Roles).Select(c => c.Value).ToHashSet(StringComparer.Ordinal)
        : new HashSet<string>();
}
