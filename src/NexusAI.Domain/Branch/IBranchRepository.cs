using NexusAI.Domain.Common;
using NexusAI.Domain.Conversation;

namespace NexusAI.Domain.Branch;

public interface IBranchRepository
    : IRepository<Branch, BranchId>
{
    Task<IReadOnlyList<Branch>> ListByConversationAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}