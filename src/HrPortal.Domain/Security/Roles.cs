namespace HrPortal.Domain.Security;

/// <summary>Application roles. Values match the Entra ID app role values.</summary>
public static class Roles
{
    public const string SystemAdmin = "SystemAdmin";
    public const string HrAdmin = "HrAdmin";
    public const string HrExecutive = "HrExecutive";
    public const string HiringManager = "HiringManager";
    public const string Interviewer = "Interviewer";
    public const string LineManager = "LineManager";
    public const string Employee = "Employee";

    public static readonly IReadOnlyList<string> All =
        [SystemAdmin, HrAdmin, HrExecutive, HiringManager, Interviewer, LineManager, Employee];
}
