namespace NexusAI.Core.Agents;

public interface IAgentRegistry
{
    IReadOnlyCollection<AgentMetadata> GetAgents();
}