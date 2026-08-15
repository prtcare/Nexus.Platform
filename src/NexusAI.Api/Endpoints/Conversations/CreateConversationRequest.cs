using NexusAI.Domain.Conversation;

namespace NexusAI.Api.Endpoints.Conversations;

public sealed record CreateConversationRequest(
    Guid ProjectId,
    Guid WorkspaceId,
    string Title,
    string Description,
    ConversationType Type,
    ConversationVisibility Visibility);