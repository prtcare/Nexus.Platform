using NexusAI.Domain.Conversation;
using NexusAI.Domain.ConversationMessage;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class ConversationMessageMapper
    : IRepositoryMapper<
        ConversationMessage,
        ConversationMessageEntity>
{
    public ConversationMessageEntity ToEntity(
        ConversationMessage domain)
    {
        return new ConversationMessageEntity
        {
            Id = domain.Id.Value,
            ConversationId = domain.ConversationId.Value,
            AgentType = ToAgentType(domain.Role),
            Content = domain.Content,
            CreatedAt = domain.CreatedOn
        };
    }

    public ConversationMessage ToDomain(
        ConversationMessageEntity entity)
    {
        return new ConversationMessage(
            new ConversationMessageId(entity.Id),
            new ConversationId(entity.ConversationId),
            FromAgentType(entity.AgentType),
            entity.Content,
            entity.CreatedAt);
    }

    private static string ToAgentType(
        ConversationMessageRole role)
    {
        return role switch
        {
            ConversationMessageRole.User =>
                "User",

            ConversationMessageRole.Assistant =>
                "Assistant",

            ConversationMessageRole.System =>
                "System",

            _ => throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "Unsupported ConversationMessageRole.")
        };
    }

    private static ConversationMessageRole FromAgentType(
        string agentType)
    {
        return agentType.Trim().ToLowerInvariant() switch
        {
            "user" =>
                ConversationMessageRole.User,

            "assistant" =>
                ConversationMessageRole.Assistant,

            "system" =>
                ConversationMessageRole.System,

            _ => throw new InvalidOperationException(
                $"Unsupported AgentType '{agentType}'.")
        };
    }
}