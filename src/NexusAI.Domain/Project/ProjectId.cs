using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Domain.Project;

public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.NewGuid());
}