using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Snapshot.Commands;

public sealed record CreateSnapshotCommand(
    BranchId BranchId,
    ConversationId ConversationId,
    string Name,
    string State);