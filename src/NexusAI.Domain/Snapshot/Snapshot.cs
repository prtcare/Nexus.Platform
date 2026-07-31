using NexusAI.Domain.Branch;
using NexusAI.Domain.Common;

namespace NexusAI.Domain.Snapshot;

public sealed class Snapshot : Entity<SnapshotId>
{
    public Snapshot(
        SnapshotId id,
        BranchId branchId,
        string description,
        SnapshotStatus status,
        DateTimeOffset createdAt)
        : base(id)
    {
        BranchId = branchId;
        Description = description;
        Status = status;
        CreatedAt = createdAt;
    }

    public BranchId BranchId { get; }

    public string Description { get; private set; }

    public SnapshotStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public void FinalizeSnapshot()
    {
        Status = SnapshotStatus.Finalized;
    }

    public void UpdateDescription(string description)
    {
        Description = description;
    }
}