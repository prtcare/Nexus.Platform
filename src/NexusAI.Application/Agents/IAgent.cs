namespace NexusAI.Application.Agents;

public interface IAgent
{
    AgentType Type { get; }

    Task<AgentResult> ExecuteAsync(
        AgentContext context,
        CancellationToken cancellationToken = default);
}