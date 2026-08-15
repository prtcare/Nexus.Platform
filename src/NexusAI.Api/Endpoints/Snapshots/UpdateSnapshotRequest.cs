namespace NexusAI.Api.Endpoints.Snapshots;

public sealed record UpdateSnapshotRequest(
    string Name,
    string State);