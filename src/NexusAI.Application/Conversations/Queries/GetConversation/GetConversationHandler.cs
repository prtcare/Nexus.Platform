using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Conversations.Queries.GetConversation;

public sealed class GetConversationHandler
{
    private readonly IConversationRepository _repository;

    public GetConversationHandler(
        IConversationRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetConversationResult?> HandleAsync(
        GetConversationQuery query,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _repository.GetAsync(
            query.ConversationId,
            cancellationToken);

        if (conversation is null)
            return null;

        return new GetConversationResult(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAt);
    }
}