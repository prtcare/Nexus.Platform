using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Queries.ListSnapshots;

public sealed record ListSnapshotResult(
    SnapshotId SnapshotId,
    string Name,
    string State,
    DateTimeOffset CreatedAt);