using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Branch.Queries.GetBranch;

public sealed record GetBranchResult(
    BranchId BranchId,
    ConversationId ConversationId,
    string Name,
    string Description,
    BranchStatus Status,
    DateTimeOffset CreatedAt);