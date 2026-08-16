using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class ProjectMapper
    : IRepositoryMapper<Project, ProjectEntity>
{
    private const int DataverseActive = 121930001;
    private const int DataverseArchived = 121930004;

    public ProjectEntity ToEntity(Project project)
    {
        return new ProjectEntity
        {
            Id = project.Id.Value,
            WorkspaceId = project.WorkspaceId.Value,
            Name = project.Name,
            Status = ToDataverseStatus(project.Status),
            CreatedAt = project.CreatedAt
        };
    }

    public Project ToDomain(ProjectEntity entity)
    {
        var project = new Project(
            new ProjectId(entity.Id),
            new WorkspaceId(entity.WorkspaceId),
            entity.Name,
            entity.CreatedAt);

        var status = FromDataverseStatus(entity.Status);

        if (status == ProjectStatus.Archived)
        {
            project.Archive();
        }

        return project;
    }

    private static int ToDataverseStatus(ProjectStatus status)
    {
        return status switch
        {
            ProjectStatus.Active => DataverseActive,
            ProjectStatus.Archived => DataverseArchived,

            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported ProjectStatus.")
        };
    }

    private static ProjectStatus FromDataverseStatus(int value)
    {
        return value switch
        {
            // 0 is DataverseContext's sentinel for a missing/unset
            // du_projectstatus attribute, not a real OptionSet code -
            // treat it as Active, the status every project is created with.
            0 => ProjectStatus.Active,

            DataverseActive => ProjectStatus.Active,
            DataverseArchived => ProjectStatus.Archived,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse Project Status value.")
        };
    }
}