using NexusAI.Domain.Common;

namespace NexusAI.Domain.Session;

public interface ISessionRepository
    : IRepository<Session, SessionId>
{
}