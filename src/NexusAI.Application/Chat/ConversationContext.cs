using NexusAI.Domain.ConversationMessage;

namespace NexusAI.Application.Chat;

public sealed class ConversationContext
{
    public IReadOnlyList<ConversationMessage> Messages { get; init; }
        = [];
}