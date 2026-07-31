namespace NexusAI.Domain.Conversation;

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
}