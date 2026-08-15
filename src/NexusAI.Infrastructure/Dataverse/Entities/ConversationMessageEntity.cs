using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class ConversationMessageEntity : DataverseEntity
{
    public Guid ConversationId { get; set; }

    public string AgentType { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int? Sequence { get; set; }

    public int? TokenCount { get; set; }

    public int? LatencyMs { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public string? CreatedBy { get; set; }
}