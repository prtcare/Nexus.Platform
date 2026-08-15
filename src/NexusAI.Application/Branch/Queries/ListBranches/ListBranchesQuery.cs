using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Branch.Queries.ListBranches;

public sealed record ListBranchesQuery(
    ConversationId ConversationId);