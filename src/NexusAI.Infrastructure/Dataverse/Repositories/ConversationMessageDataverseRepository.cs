using NexusAI.Domain.Conversation;
using NexusAI.Domain.ConversationMessage;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class ConversationMessageDataverseRepository
    : DataverseRepositoryBase<
        ConversationMessage,
        ConversationMessageEntity,
        ConversationMessageId>,
      IConversationMessageRepository
{
    public ConversationMessageDataverseRepository(
        IDataverseClient client,
        IRepositoryMapper<ConversationMessage, ConversationMessageEntity> mapper)
        : base(client, mapper)
    {
    }

    public override Task AddAsync(
        ConversationMessage message,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(
            message,
            cancellationToken);
    }

    public override Task<ConversationMessage?> GetAsync(
        ConversationMessageId id,
        CancellationToken cancellationToken = default)
    {
        return RetrieveDomainAsync(
            id.Value,
            cancellationToken);
    }

    public override Task UpdateAsync(
        ConversationMessage message,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(
            message,
            cancellationToken);
    }

    public Task<IReadOnlyList<ConversationMessage>>
        ListByConversationAsync(
            ConversationId conversationId,
            CancellationToken cancellationToken = default)
    {
        return RetrieveMultipleDomainAsync(
            entity =>
                entity.ConversationId == conversationId.Value,
            cancellationToken);
    }
}