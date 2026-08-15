using NexusAI.Domain.Common;
using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Domain.Workspace;

public interface IWorkspaceRepository
    : IRepository<Workspace, WorkspaceId>
{
    Task<IReadOnlyList<Workspace>> ListAsync(
        CancellationToken cancellationToken = default);
}
