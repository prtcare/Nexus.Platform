namespace NexusAI.Api.Endpoints.Snapshots;

public sealed record CreateSnapshotRequest(
    Guid BranchId,
    Guid ConversationId,
    string Name,
    string State);