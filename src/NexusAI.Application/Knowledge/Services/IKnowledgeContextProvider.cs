namespace NexusAI.Application.Knowledge.Services;

public interface IKnowledgeContextProvider
{
    Task<IReadOnlyList<NexusAI.Domain.Knowledge.Knowledge>> GetAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);
}