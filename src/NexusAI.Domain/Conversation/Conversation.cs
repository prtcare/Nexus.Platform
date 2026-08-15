using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;

namespace NexusAI.Domain.Conversation;

public sealed class Conversation
{
    public Conversation(
        ConversationId id,
        ProjectId projectId,
        WorkspaceId workspaceId,
        string title,
        string description,
        ConversationType type,
        ConversationVisibility visibility,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Id = id;
        ProjectId = projectId;
        WorkspaceId = workspaceId;
        Title = title.Trim();
        Description = description?.Trim() ?? string.Empty;
        Type = type;
        Visibility = visibility;
        CreatedAt = createdAt;
        Status = ConversationStatus.Active;
    }

    public ConversationId Id { get; }

    public ProjectId ProjectId { get; }

    public WorkspaceId WorkspaceId { get; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public ConversationType Type { get; private set; }

    public ConversationVisibility Visibility { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? LastMessageOn { get; private set; }

    public ConversationStatus Status { get; private set; }

    public void Rename(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Title = title.Trim();
    }

    public void ChangeDescription(string description)
    {
        Description = description?.Trim() ?? string.Empty;
    }

    public void ChangeType(ConversationType type)
    {
        Type = type;
    }

    public void ChangeVisibility(ConversationVisibility visibility)
    {
        Visibility = visibility;
    }

    public void RecordMessage(DateTimeOffset messageAt)
    {
        LastMessageOn = messageAt;
    }

    public void Archive()
    {
        Status = ConversationStatus.Archived;
    }
    public void ChangeStatus(ConversationStatus status)
    {
        Status = status;
    }

}