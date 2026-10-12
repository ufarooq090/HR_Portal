namespace HrPortal.Domain.Common;

/// <summary>Base type for aggregate entities with a client-generated GUID key.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}
