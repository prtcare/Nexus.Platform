using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Domain.Snapshot;

public interface ISnapshotRepository
    : IRepository<Snapshot, SnapshotId>
{
}