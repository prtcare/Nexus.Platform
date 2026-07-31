using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class ProjectMapper
    : IRepositoryMapper<Project, ProjectEntity>
{
    public ProjectEntity ToEntity(Project project)
    {
        return new ProjectEntity
        {
            Id = project.Id.Value,
            WorkspaceId = project.WorkspaceId.Value,
            Name = project.Name,
            Status = (int)project.Status,
            CreatedAt = project.CreatedAt
        };
    }

    public Project ToDomain(ProjectEntity entity)
    {
        return new Project(
            new ProjectId(entity.Id),
            new WorkspaceId(entity.WorkspaceId),
            entity.Name,
            entity.CreatedAt);
    }
}