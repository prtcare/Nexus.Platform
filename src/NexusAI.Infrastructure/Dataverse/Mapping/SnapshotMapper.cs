using NexusAI.Domain.Branch;
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
            Description = domain.Description,
            Status = (int)domain.Status,
            CreatedAt = domain.CreatedAt
        };
    }

    public Snapshot ToDomain(SnapshotEntity entity)
    {
        return new Snapshot(
            new SnapshotId(entity.Id),
            new BranchId(entity.BranchId),
            entity.Description,
            (SnapshotStatus)entity.Status,
            entity.CreatedAt);
    }
}