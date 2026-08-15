using KnowledgeModel = NexusAI.Domain.Knowledge.Knowledge;

namespace NexusAI.Application.Knowledge.Services;

public interface IKnowledgeRetrievalService
{
    Task<IReadOnlyList<KnowledgeModel>> RetrieveAsync(
        Guid workspaceId,
        string query,
        CancellationToken cancellationToken = default);
}