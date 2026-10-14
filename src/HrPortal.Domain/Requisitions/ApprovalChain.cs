using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;

namespace HrPortal.Domain.Requisitions;

public sealed record ApprovalStep(string Label, Guid ApproverEmployeeId);

/// <summary>
/// REQ-02: who approves requisitions in a country, in order. The default HR agreed is a single CEO approval
/// (decision 12 Oct 2026); HR Admins can add steps. Changes apply to requisitions submitted afterwards.
/// </summary>
public sealed class ApprovalChain
{
    public const int MaxSteps = 5;

    private ApprovalChain()
    {
    }

    public ApprovalChain(string countryCode, IReadOnlyList<(string Label, Employee Approver)> steps)
    {
        CountryCode = Guard.CountryCode(nameof(countryCode), countryCode);
        Replace(steps);
    }

    public string CountryCode { get; private set; } = null!;

    /// <summary>Stored as JSON; at most <see cref="MaxSteps"/>.</summary>
    public List<ApprovalStep> Steps { get; private set; } = [];

    public void Replace(IReadOnlyList<(string Label, Employee Approver)> steps)
    {
        // A chain always keeps at least one step: without one, requisitions in the country could not be approved.
        if (steps.Count == 0)
        {
            throw new DomainValidationException("steps", "An approval chain needs at least one step.");
        }

        if (steps.Count > MaxSteps)
        {
            throw new DomainValidationException("steps", $"An approval chain can have at most {MaxSteps} steps.");
        }

        if (steps.Select(s => s.Approver.Id).Distinct().Count() != steps.Count)
        {
            throw new DomainValidationException("steps", "Each approver can appear only once.");
        }

        if (steps.Any(s => !s.Approver.Status.IsActive()))
        {
            throw new DomainValidationException("steps", "Approvers must be current employees.");
        }

        Steps = steps.Select(s => new ApprovalStep(Guard.RequiredText("steps", s.Label, 100), s.Approver.Id)).ToList();
    }
}
