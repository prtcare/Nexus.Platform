using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Commands;

public sealed record CreateSnapshotResult(
    SnapshotId SnapshotId,
    string Name);