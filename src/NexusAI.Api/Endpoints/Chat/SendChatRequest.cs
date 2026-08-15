using NexusAI.Domain.Conversation;

namespace NexusAI.Api.Endpoints.Chat;

public sealed record SendChatRequest(
    Guid ConversationId,
    string Prompt);