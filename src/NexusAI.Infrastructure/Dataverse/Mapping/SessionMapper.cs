using NexusAI.Domain.Conversation;
using NexusAI.Domain.Session;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class SessionMapper
    : IRepositoryMapper<Session, SessionEntity>
{
    private const int DataverseRunning = 121930000;
    private const int DataverseEnded = 121930001;

    public SessionEntity ToEntity(Session domain)
    {
        return new SessionEntity
        {
            Id = domain.Id.Value,
            ConversationId = domain.ConversationId.Value,
            Status = ToDataverseStatus(domain.Status),
            StartedAt = domain.StartedAt,
            EndedAt = domain.EndedAt,
            CreatedAt = domain.StartedAt
        };
    }

    public Session ToDomain(SessionEntity entity)
    {
        return new Session(
            new SessionId(entity.Id),
            new ConversationId(entity.ConversationId),
            FromDataverseStatus(entity.Status),
            entity.StartedAt,
            entity.EndedAt);
    }

    private static int ToDataverseStatus(
        SessionStatus status)
    {
        return status switch
        {
            SessionStatus.Running =>
                DataverseRunning,

            SessionStatus.Ended =>
                DataverseEnded,

            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported SessionStatus value.")
        };
    }

    private static SessionStatus FromDataverseStatus(
        int value)
    {
        return value switch
        {
            DataverseRunning =>
                SessionStatus.Running,

            DataverseEnded =>
                SessionStatus.Ended,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse SessionStatus value.")
        };
    }
}