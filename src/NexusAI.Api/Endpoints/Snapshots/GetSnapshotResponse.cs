namespace NexusAI.Api.Endpoints.Snapshots;

public sealed record GetSnapshotResponse(
    Guid SnapshotId,
    Guid BranchId,
    Guid ConversationId,
    string Name,
    string State,
    DateTimeOffset CreatedAt);