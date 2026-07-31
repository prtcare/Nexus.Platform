namespace NexusAI.Domain.WorkItem;

public readonly record struct WorkItemId(Guid Value)
{
    public static WorkItemId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}