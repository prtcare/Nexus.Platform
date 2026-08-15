using NexusAI.Domain.Common;
using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Domain.Project;

public interface IProjectRepository
    : IRepository<Project, ProjectId>
{
    Task<Project?> GetByIdAsync(
        ProjectId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListByWorkspaceAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);
}