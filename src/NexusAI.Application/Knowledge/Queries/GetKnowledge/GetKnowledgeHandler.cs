using NexusAI.Domain.Knowledge;

namespace NexusAI.Application.Knowledge.Queries.GetKnowledge;

public sealed class GetKnowledgeHandler
{
    private readonly IKnowledgeRepository _repository;

    public GetKnowledgeHandler(
        IKnowledgeRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetKnowledgeResult?> HandleAsync(
        GetKnowledgeQuery query,
        CancellationToken cancellationToken = default)
    {
        var knowledge = await _repository.GetAsync(
            query.KnowledgeId,
            cancellationToken);

        if (knowledge is null)
        {
            return null;
        }

        return new GetKnowledgeResult(
            knowledge.Id,
            knowledge.WorkspaceId,
            knowledge.Title,
            knowledge.Content,
            knowledge.Type,
            knowledge.CreatedAt);
    }
}