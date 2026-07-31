using NexusAI.Domain.Common;
using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Domain.Session;

public interface ISessionRepository
    : IRepository<Session, SessionId>
{
}