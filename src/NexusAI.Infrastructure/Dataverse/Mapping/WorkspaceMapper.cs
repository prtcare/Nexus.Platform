using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class WorkspaceMapper
    : IRepositoryMapper<Workspace, WorkspaceEntity>
{
    private const int DataverseActive = 121930000;
    private const int DataverseArchived = 121930001;

    public WorkspaceEntity ToEntity(Workspace workspace)
    {
        return new WorkspaceEntity
        {
            Id = workspace.Id.Value,
            Name = workspace.Name,
            Owner = workspace.Owner,
            Description = workspace.Description,
            Status = ToDataverseStatus(workspace.Status),
            CreatedAt = workspace.CreatedAt
        };
    }

    public Workspace ToDomain(WorkspaceEntity entity)
    {
        var workspace = new Workspace(
            new WorkspaceId(entity.Id),
            entity.Name,
            entity.Owner,
            entity.Description,
            entity.CreatedAt);

        if (entity.Status == DataverseArchived)
        {
            workspace.Archive();
        }

        return workspace;
    }

    private static int ToDataverseStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Active => DataverseActive,
            WorkspaceStatus.Archived => DataverseArchived,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unknown WorkspaceStatus value.")
        };
    }
}