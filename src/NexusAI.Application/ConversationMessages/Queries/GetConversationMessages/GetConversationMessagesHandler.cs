using NexusAI.Domain.ConversationMessage;

namespace NexusAI.Application.ConversationMessages.Queries.GetConversationMessages;

public sealed class GetConversationMessagesHandler
{
    private readonly IConversationMessageRepository _repository;

    public GetConversationMessagesHandler(
        IConversationMessageRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ConversationMessageResult>> HandleAsync(
        GetConversationMessagesQuery query,
        CancellationToken cancellationToken = default)
    {
        var messages =
            await _repository.ListByConversationAsync(
                query.ConversationId,
                cancellationToken);

        return messages
            .Select(message =>
                new ConversationMessageResult(
                    message.Id,
                    message.Role,
                    message.Content,
                    message.CreatedOn))
            .ToList();
    }
}