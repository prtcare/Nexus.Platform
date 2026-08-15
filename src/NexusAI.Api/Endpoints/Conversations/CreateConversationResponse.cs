namespace NexusAI.Api.Endpoints.Conversations;

public sealed record CreateConversationResponse(
    Guid ConversationId,
    string Title);