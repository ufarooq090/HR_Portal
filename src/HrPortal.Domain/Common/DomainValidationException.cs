namespace HrPortal.Domain.Common;

/// <summary>A business rule was violated by the input. Surfaced to clients as HTTP 400.</summary>
public sealed class DomainValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
