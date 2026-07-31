using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class WorkspaceMapper
    : IRepositoryMapper<Workspace, WorkspaceEntity>
{
    public WorkspaceEntity ToEntity(Workspace workspace)
    {
        return new WorkspaceEntity
        {
            Id = workspace.Id.Value,
            Name = workspace.Name,
            Status = (int)workspace.Status,
            CreatedAt = workspace.CreatedAt
        };
    }

    public Workspace ToDomain(WorkspaceEntity entity)
    {
        return new Workspace(
            new WorkspaceId(entity.Id),
            entity.Name,
            entity.CreatedAt);
    }
}