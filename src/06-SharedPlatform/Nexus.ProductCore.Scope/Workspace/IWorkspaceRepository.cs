using Nexus.ProductCore.Scope.Common;
using Nexus.ProductCore.Scope.Common.Identifiers;

namespace Nexus.ProductCore.Scope.Workspace;

public interface IWorkspaceRepository
    : IRepository<Workspace, WorkspaceId>
{
    Task<IReadOnlyList<Workspace>> ListAsync(
        CancellationToken cancellationToken = default);
}
