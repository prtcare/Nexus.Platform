using NexusAI.Domain.Conversation;

namespace NexusAI.Api.Endpoints.Conversations;

public sealed record UpdateConversationRequest(
    string Title,
    string Description,
    ConversationType Type,
    ConversationVisibility Visibility);

