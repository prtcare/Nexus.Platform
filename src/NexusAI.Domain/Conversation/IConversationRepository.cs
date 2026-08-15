namespace NexusAI.Domain.Conversation;

using NexusAI.Domain.Project;

public interface IConversationRepository
{
    Task AddAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default);

    Task<Conversation?> GetAsync(
        ConversationId id,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Conversation>> ListByProjectAsync(
    ProjectId projectId,
    CancellationToken cancellationToken = default);
}