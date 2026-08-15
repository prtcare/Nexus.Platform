using NexusAI.Domain.Knowledge;

namespace NexusAI.Application.Knowledge.Queries.ListKnowledge;

public sealed class ListKnowledgeHandler
{
    private readonly IKnowledgeRepository _repository;

    public ListKnowledgeHandler(
        IKnowledgeRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ListKnowledgeResult>> HandleAsync(
        ListKnowledgeQuery query,
        CancellationToken cancellationToken = default)
    {
        var knowledgeItems = await _repository.ListByWorkspaceAsync(
            query.WorkspaceId,
            cancellationToken);

        return knowledgeItems
    .Select(knowledge =>
        new ListKnowledgeResult(
            knowledge.Id,
            knowledge.WorkspaceId,
            knowledge.Title,
            knowledge.Type,
            knowledge.CreatedAt))
    .ToList();
    }
}