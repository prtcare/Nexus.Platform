using NexusAI.Domain.Common;
using NexusAI.Domain.Conversation;

namespace NexusAI.Domain.Branch;

public sealed class Branch : Entity<BranchId>
{
    public Branch(
        BranchId id,
        ConversationId conversationId,
        string name,
        string description,
        BranchStatus status,
        DateTimeOffset createdAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ConversationId = conversationId;
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        Status = status;
        CreatedAt = createdAt;
    }

    public ConversationId ConversationId { get; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public BranchStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void UpdateDescription(string description)
    {
        Description = description?.Trim() ?? string.Empty;
    }

    public void ChangeStatus(BranchStatus status)
    {
        Status = status;
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