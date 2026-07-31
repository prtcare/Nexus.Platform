using NexusAI.Domain.Conversation;
using NexusAI.Domain.Project;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class ConversationMapper
    : IRepositoryMapper<Conversation, ConversationEntity>
{
    public ConversationEntity ToEntity(Conversation conversation)
    {
        return new ConversationEntity
        {
            Id = conversation.Id.Value,
            ProjectId = conversation.ProjectId.Value,
            Title = conversation.Title,
            Status = (int)conversation.Status,
            CreatedAt = conversation.CreatedAt
        };
    }

    public Conversation ToDomain(ConversationEntity entity)
    {
        return new Conversation(
            new ConversationId(entity.Id),
            new ProjectId(entity.ProjectId),
            entity.Title,
            entity.CreatedAt);
    }
}