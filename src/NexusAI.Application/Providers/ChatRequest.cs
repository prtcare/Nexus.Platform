using NexusAI.Domain.ConversationMessage;

namespace NexusAI.Application.Providers;

public sealed record ChatRequest
{
    public required string Prompt { get; init; }

    public IReadOnlyList<ConversationMessage> History { get; init; }
        = [];
}