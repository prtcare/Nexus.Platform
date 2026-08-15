using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Common;

namespace NexusAI.Domain.Knowledge;

public interface IKnowledgeRepository
    : IRepository<Knowledge, KnowledgeId>
{
    Task<IReadOnlyList<Knowledge>> ListByWorkspaceAsync(
    WorkspaceId workspaceId,
    CancellationToken cancellationToken = default);
}
