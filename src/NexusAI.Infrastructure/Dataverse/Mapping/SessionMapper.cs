using NexusAI.Domain.Conversation;
using NexusAI.Domain.Session;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class SessionMapper
    : IRepositoryMapper<Session, SessionEntity>
{
    public SessionEntity ToEntity(Session domain)
    {
        return new SessionEntity
        {
            Id = domain.Id.Value,
            ConversationId = domain.ConversationId.Value,
            Status = (int)domain.Status,
            StartedAt = domain.StartedAt
        };
    }

    public Session ToDomain(SessionEntity entity)
    {
        return new Session(
            new SessionId(entity.Id),
            new ConversationId(entity.ConversationId),
            (SessionStatus)entity.Status,
            entity.StartedAt);
    }
}