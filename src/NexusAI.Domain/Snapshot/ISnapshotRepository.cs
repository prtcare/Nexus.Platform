using NexusAI.Domain.Branch;
using NexusAI.Domain.Common;

namespace NexusAI.Domain.Snapshot;

public interface ISnapshotRepository
    : IRepository<Snapshot, SnapshotId>
{
    Task<IReadOnlyList<Snapshot>> ListByBranchAsync(
        BranchId branchId,
        CancellationToken cancellationToken = default);
}