using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Queries.GetSnapshot;

public sealed record GetSnapshotResult(
    SnapshotId SnapshotId,
    BranchId BranchId,
    ConversationId ConversationId,
    string Name,
    string State,
    DateTimeOffset CreatedAt);