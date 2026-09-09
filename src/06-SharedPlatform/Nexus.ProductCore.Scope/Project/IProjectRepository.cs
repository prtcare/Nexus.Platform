using Nexus.ProductCore.Scope.Common;
using Nexus.ProductCore.Scope.Common.Identifiers;

namespace Nexus.ProductCore.Scope.Project;

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
