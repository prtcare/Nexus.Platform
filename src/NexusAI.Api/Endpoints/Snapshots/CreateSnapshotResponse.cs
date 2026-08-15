namespace NexusAI.Api.Endpoints.Snapshots;

public sealed record CreateSnapshotResponse(
    Guid SnapshotId,
    string Name);