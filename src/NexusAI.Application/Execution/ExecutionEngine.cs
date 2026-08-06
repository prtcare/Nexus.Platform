using NexusAI.Domain.Project;

namespace NexusAI.Application.Execution;

public sealed class ExecutionEngine : IExecutionEngine
{
    public Task<ExecutionResult> ExecuteAsync(
    ExecutionContext context,
    CancellationToken cancellationToken = default)
    {
        Console.WriteLine();
        Console.WriteLine("Execution");
        Console.WriteLine("---------");

        Console.WriteLine(
            $"Executing project {context.ProjectId.Value}");

        var result = new ExecutionResult(
            Success: true,
            ExecutedWorkItems: 0);

        return Task.FromResult(result);
    }
}