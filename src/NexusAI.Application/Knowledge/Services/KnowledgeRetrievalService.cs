using KnowledgeModel = NexusAI.Domain.Knowledge.Knowledge;

namespace NexusAI.Application.Knowledge.Services;

public sealed class KnowledgeRetrievalService : IKnowledgeRetrievalService
{
    private readonly IKnowledgeContextProvider _contextProvider;
    private readonly IKnowledgeRanker _ranker;

    public KnowledgeRetrievalService(
        IKnowledgeContextProvider contextProvider,
        IKnowledgeRanker ranker)
    {
        _contextProvider = contextProvider;
        _ranker = ranker;
    }

    public async Task<IReadOnlyList<KnowledgeModel>> RetrieveAsync(
        Guid workspaceId,
        string query,
        CancellationToken cancellationToken = default)
    {
        // Load all workspace knowledge
        var knowledge = await _contextProvider.GetAsync(
            workspaceId,
            cancellationToken);

        // Rank and return the most relevant entries
        return _ranker.Rank(
            knowledge,
            query);
    }
}