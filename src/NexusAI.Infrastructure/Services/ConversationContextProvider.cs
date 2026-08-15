using NexusAI.Application.Chat;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.ConversationMessage;

namespace NexusAI.Infrastructure.Services;

public sealed class ConversationContextProvider
    : IConversationContextProvider
{
    private readonly IConversationMessageRepository _repository;

    public ConversationContextProvider(
        IConversationMessageRepository repository)
    {
        _repository = repository;
    }

    public async Task<ConversationContext> GetAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        var messages =
    await _repository.ListByConversationAsync(
        conversationId,
        cancellationToken);

        return new ConversationContext
        {
            Messages = messages
        };
    }
}