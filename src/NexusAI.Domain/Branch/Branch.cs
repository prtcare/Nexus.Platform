using NexusAI.Domain.Common;
using NexusAI.Domain.Conversation;

namespace NexusAI.Domain.Branch;

public sealed class Branch : Entity<BranchId>
{
    public Branch(
        BranchId id,
        ConversationId conversationId,
        string name,
        BranchStatus status,
        DateTimeOffset createdAt)
        : base(id)
    {
        ConversationId = conversationId;
        Name = name;
        Status = status;
        CreatedAt = createdAt;
    }

    public ConversationId ConversationId { get; }

    public string Name { get; private set; }

    public BranchStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public void Rename(string name)
    {
        Name = name;
    }

    public void Archive()
    {
        Status = BranchStatus.Archived;
    }

    public void Merge()
    {
        Status = BranchStatus.Merged;
    }
}