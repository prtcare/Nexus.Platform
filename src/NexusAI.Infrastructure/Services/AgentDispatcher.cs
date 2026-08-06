using NexusAI.Application.Agents;
using NexusAI.Application.Execution;

namespace NexusAI.Infrastructure.Services;

public sealed class AgentDispatcher : IAgentDispatcher
{
    private readonly IEnumerable<IAgent> _agents;

    public AgentDispatcher(IEnumerable<IAgent> agents)
    {
        _agents = agents;
    }

    public async Task<AgentResult> DispatchAsync(
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var agent = _agents.FirstOrDefault(a => a.Type == context.AgentType);

        if (agent is null)
        {
            throw new InvalidOperationException(
                $"No agent registered for '{context.AgentType}'.");
        }

        return await agent.ExecuteAsync(
            context,
            cancellationToken);
    }
}