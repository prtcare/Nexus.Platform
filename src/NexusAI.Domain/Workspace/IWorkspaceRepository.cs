using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Domain.Workspace;

public interface IWorkspaceRepository
{
    Task AddAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default);

    Task<Workspace?> GetAsync(
        WorkspaceId id,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default);
}