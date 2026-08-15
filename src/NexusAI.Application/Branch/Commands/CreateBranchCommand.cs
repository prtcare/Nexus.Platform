using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Branch.Commands;

public sealed record CreateBranchCommand(
    ConversationId ConversationId,
    string Name,
    string Description);