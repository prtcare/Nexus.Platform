using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Conversations.Queries.ListConversations;

public sealed class ListConversationsHandler
{
    private readonly IConversationRepository _repository;

    public ListConversationsHandler(
        IConversationRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ListConversationResult>> HandleAsync(
        ListConversationsQuery query,
        CancellationToken cancellationToken = default)
    {
        var conversations = await _repository.ListByProjectAsync(
            query.ProjectId,
            cancellationToken);

        return conversations
            .Select(c => new ListConversationResult(
                c.Id,
                c.Title,
                c.CreatedAt))
            .ToList();
    }
}