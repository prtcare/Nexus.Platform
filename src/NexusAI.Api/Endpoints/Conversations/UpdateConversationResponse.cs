using NexusAI.Domain.Conversation;

namespace NexusAI.Api.Endpoints.Conversations;


public sealed record UpdateConversationResponse(
    Guid ConversationId,
    string Title);