namespace NexusAI.Core.Agents;

public interface IAgent
{
    AgentMetadata Metadata { get; }

    Task RunAsync(
        AgentContext context,
        CancellationToken cancellationToken = default);
}