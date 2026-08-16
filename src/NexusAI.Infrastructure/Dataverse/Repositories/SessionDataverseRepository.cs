using NexusAI.Domain.Conversation;
using NexusAI.Domain.Session;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class SessionDataverseRepository
    : DataverseRepositoryBase<
        Session,
        SessionEntity,
        SessionId>,
      ISessionRepository
{
    public SessionDataverseRepository(
        IDataverseClient client,
        IRepositoryMapper<Session, SessionEntity> mapper)
        : base(client, mapper)
    {
    }

    public override Task AddAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(
            session,
            cancellationToken);
    }

    public override Task<Session?> GetAsync(
        SessionId id,
        CancellationToken cancellationToken = default)
    {
        return RetrieveDomainAsync(
            id.Value,
            cancellationToken);
    }

    public override Task UpdateAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(
            session,
            cancellationToken);
    }

    public Task<IReadOnlyList<Session>> ListByConversationAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        return RetrieveMultipleDomainAsync(
            "du_conversation",
            conversationId.Value,
            entity =>
                entity.ConversationId ==
                conversationId.Value,
            cancellationToken);
    }
}