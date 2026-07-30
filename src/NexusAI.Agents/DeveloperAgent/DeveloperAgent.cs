using NexusAI.Core.Agents;

namespace NexusAI.Agents.Developer;

public sealed class DeveloperAgent : IAgent
{
    public AgentMetadata Metadata => new()
    {
        Id = "developer",
        Name = "Developer",
        Description = "Software development assistant"
    };

    public Task RunAsync(
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Running {Metadata.Name} Agent");
        return Task.CompletedTask;
    }
}