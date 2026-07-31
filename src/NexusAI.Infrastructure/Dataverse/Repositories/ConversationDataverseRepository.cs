using NexusAI.Domain.Conversation;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class ConversationDataverseRepository
    : DataverseRepositoryBase<
        Conversation,
        ConversationEntity,
        ConversationId>,
      IConversationRepository
{
    public ConversationDataverseRepository(
        IDataverseClient client,
        IRepositoryMapper<Conversation, ConversationEntity> mapper)
        : base(client, mapper)
    {
    }

    public override Task AddAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(conversation, cancellationToken);
    }

    public override Task<Conversation?> GetAsync(
        ConversationId id,
        CancellationToken cancellationToken = default)
    {
        return RetrieveDomainAsync(id.Value, cancellationToken);
    }

    public override Task UpdateAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(conversation, cancellationToken);
    }
}