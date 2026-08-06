namespace NexusAI.Application.Agents;

public sealed class DummyAgent : IAgent
{
    public AgentType Type => AgentType.Developer;

    public Task<AgentResult> ExecuteAsync(
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[DummyAgent]");
        Console.WriteLine("Executing...");
        Console.WriteLine();

        return Task.FromResult(
            new AgentResult(
                true,
                "Dummy execution completed."));
    }
}