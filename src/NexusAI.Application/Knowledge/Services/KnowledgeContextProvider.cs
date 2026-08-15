using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Knowledge;
using KnowledgeModel = NexusAI.Domain.Knowledge.Knowledge;

namespace NexusAI.Application.Knowledge.Services;

public sealed class KnowledgeContextProvider : IKnowledgeContextProvider
{
    private readonly IKnowledgeRepository _repository;

    public KnowledgeContextProvider(
        IKnowledgeRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<KnowledgeModel>> GetAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListByWorkspaceAsync(
            new WorkspaceId(workspaceId),
            cancellationToken);
    }
}