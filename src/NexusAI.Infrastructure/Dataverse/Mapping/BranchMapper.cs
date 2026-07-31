using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class BranchMapper
    : IRepositoryMapper<Branch, BranchEntity>
{
    public BranchEntity ToEntity(Branch domain)
    {
        return new BranchEntity
        {
            Id = domain.Id.Value,
            ConversationId = domain.ConversationId.Value,
            Name = domain.Name,
            Status = (int)domain.Status,
            CreatedAt = domain.CreatedAt
        };
    }

    public Branch ToDomain(BranchEntity entity)
    {
        return new Branch(
            new BranchId(entity.Id),
            new ConversationId(entity.ConversationId),
            entity.Name,
            (BranchStatus)entity.Status,
            entity.CreatedAt);
    }
}