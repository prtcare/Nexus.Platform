using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Snapshot;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class SnapshotMapper
    : IRepositoryMapper<Snapshot, SnapshotEntity>
{
    public SnapshotEntity ToEntity(Snapshot domain)
    {
        return new SnapshotEntity
        {
            Id = domain.Id.Value,
            BranchId = domain.BranchId.Value,
            ConversationId = domain.ConversationId.Value,
            Name = domain.Name,
            State = domain.State,
            CreatedAt = domain.CreatedAt
        };
    }

    public Snapshot ToDomain(SnapshotEntity entity)
    {
        return new Snapshot(
            new SnapshotId(entity.Id),
            new BranchId(entity.BranchId),
            new ConversationId(entity.ConversationId),
            entity.Name,
            entity.State,
            entity.CreatedAt);
    }
}