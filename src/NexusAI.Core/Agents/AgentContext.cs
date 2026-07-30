namespace NexusAI.Core.Agents;

public sealed class AgentContext
{
    public required string ConversationId { get; init; }

    public required string WorkspaceId { get; init; }
}