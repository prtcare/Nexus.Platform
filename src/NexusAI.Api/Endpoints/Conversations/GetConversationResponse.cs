namespace NexusAI.Api.Endpoints.Conversations;

public sealed record GetConversationResponse(
    Guid ConversationId,
    string Title,
    DateTimeOffset CreatedAt);