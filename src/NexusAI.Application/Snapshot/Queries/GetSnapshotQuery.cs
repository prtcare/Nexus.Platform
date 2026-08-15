using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Queries.GetSnapshot;

public sealed record GetSnapshotQuery(
    SnapshotId SnapshotId);