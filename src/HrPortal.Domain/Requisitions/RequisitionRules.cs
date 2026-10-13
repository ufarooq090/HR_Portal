using HrPortal.Domain.Security;

namespace HrPortal.Domain.Requisitions;

/// <summary>What one user may do with one requisition. The UI hides controls with it; the API enforces it.</summary>
public sealed record RequisitionPermissions(
    bool CanEdit,
    bool CanSubmit,
    bool CanWithdraw,
    bool CanDecide,
    IReadOnlyList<RequisitionStatus> StatusOptions);

/// <summary>
/// Requisition access on top of the permission matrix (Requisitions: HR full within their countries; hiring and
/// line managers create and view for their own department). The requester always sees and manages their own
/// requests while they are drafts or pending; approvers see the requisitions they are asked to approve.
/// </summary>
public static class RequisitionRules
{
    /// <summary>HR within their countries (MC-03), for the given capability.</summary>
    public static bool IsHrFor(UserAccess access, string countryCode, Capability capability)
    {
        var scopes = access.ScopesFor(HrModule.Requisitions, capability);
        return scopes.Contains(DataScope.All) || (scopes.Contains(DataScope.Countries) && access.HrCountries.Includes(countryCode));
    }

    /// <summary>REQ-01: managers raise requisitions for their own department; HR for any department in their countries.</summary>
    public static bool CanCreate(UserAccess access, Guid departmentId, string countryCode) =>
        IsHrFor(access, countryCode, Capability.Create)
        || (access.ScopesFor(HrModule.Requisitions, Capability.Create).Contains(DataScope.Department) && access.DepartmentId == departmentId);

    public static RequisitionPermissions For(UserAccess access, Requisition requisition)
    {
        var isRequester = access.EmployeeId is { } me && me == requisition.RequesterEmployeeId;
        var isHr = IsHrFor(access, requisition.CountryCode, Capability.Edit);
        var manages = isRequester || isHr;

        IReadOnlyList<RequisitionStatus> statusOptions = isHr
            ? requisition.ManualNextStatuses
            : isRequester && requisition.Status is RequisitionStatus.Draft or RequisitionStatus.PendingApproval
                ? [RequisitionStatus.Cancelled]
                : [];

        return new RequisitionPermissions(
            CanEdit: manages && requisition.Status == RequisitionStatus.Draft,
            CanSubmit: manages && requisition.Status == RequisitionStatus.Draft,
            CanWithdraw: manages && requisition.Status == RequisitionStatus.PendingApproval,
            CanDecide: access.EmployeeId is { } approver && requisition.CurrentStep?.ApproverEmployeeId == approver && !isRequester,
            StatusOptions: statusOptions);
    }
}
