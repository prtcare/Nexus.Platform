using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Memory;
using NexusAI.Domain.Project;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;
using NexusAI.Infrastructure.Dataverse.Mapping;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class MemoryMapper : IRepositoryMapper<Memory, MemoryEntity>
{
    public MemoryEntity ToEntity(Memory domain)
    {
        return new MemoryEntity
        {
            Id = domain.Id.Value,
            WorkspaceId = domain.WorkspaceId.Value,
            ProjectId = domain.ProjectId?.Value,
            ConversationId = domain.ConversationId?.Value,
            Type = (int)domain.Type,
            Source = (int)domain.Source,
            Content = domain.Content,
            Keywords = domain.Keywords,
            Metadata = domain.Metadata,
            CreatedAt = domain.CreatedAt
        };
    }

    public Memory ToDomain(MemoryEntity entity)
    {
        return new Memory(
            new MemoryId(entity.Id),
            new WorkspaceId(entity.WorkspaceId),
            entity.ProjectId.HasValue
                ? new ProjectId(entity.ProjectId.Value)
                : null,
            entity.ConversationId.HasValue
                ? new ConversationId(entity.ConversationId.Value)
                : null,
            (MemoryType)entity.Type,
            (MemorySource)entity.Source,
            entity.Content,
            entity.Keywords,
            entity.Metadata,
            entity.CreatedAt);
    }
}