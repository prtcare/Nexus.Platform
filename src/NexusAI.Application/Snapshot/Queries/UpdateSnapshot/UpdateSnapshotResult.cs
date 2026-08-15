using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Commands.UpdateSnapshot;

public sealed record UpdateSnapshotResult(
    SnapshotId SnapshotId,
    string Name,
    string State);