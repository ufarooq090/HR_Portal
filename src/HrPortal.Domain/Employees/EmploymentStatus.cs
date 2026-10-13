using System.Text.Json.Serialization;

namespace HrPortal.Domain.Employees;

[JsonConverter(typeof(JsonStringEnumConverter<EmploymentType>))]
public enum EmploymentType
{
    Permanent,
    Contract,
    Intern,
}

/// <summary>EMP-04. Resigned, Terminated and Absconded are final: a rehire gets a new employee record.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmploymentStatus>))]
public enum EmploymentStatus
{
    Probation,
    Confirmed,
    OnNotice,
    Resigned,
    Terminated,
    Absconded,
}

public static class EmploymentStatusRules
{
    private static readonly Dictionary<EmploymentStatus, EmploymentStatus[]> Allowed = new()
    {
        [EmploymentStatus.Probation] =
            [EmploymentStatus.Confirmed, EmploymentStatus.OnNotice, EmploymentStatus.Resigned, EmploymentStatus.Terminated, EmploymentStatus.Absconded],
        [EmploymentStatus.Confirmed] =
            [EmploymentStatus.OnNotice, EmploymentStatus.Resigned, EmploymentStatus.Terminated, EmploymentStatus.Absconded],

        // Back to Confirmed when a resignation is withdrawn.
        [EmploymentStatus.OnNotice] =
            [EmploymentStatus.Confirmed, EmploymentStatus.Resigned, EmploymentStatus.Terminated, EmploymentStatus.Absconded],
        [EmploymentStatus.Resigned] = [],
        [EmploymentStatus.Terminated] = [],
        [EmploymentStatus.Absconded] = [],
    };

    public static bool IsActive(this EmploymentStatus status) =>
        status is EmploymentStatus.Probation or EmploymentStatus.Confirmed or EmploymentStatus.OnNotice;

    public static bool CanChange(EmploymentStatus from, EmploymentStatus to) => Allowed[from].Contains(to);

    public static IReadOnlyList<EmploymentStatus> NextStatuses(EmploymentStatus from) => Allowed[from];
}
