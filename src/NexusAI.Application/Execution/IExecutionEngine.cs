using NexusAI.Domain.Project;

namespace NexusAI.Application.Execution;

public interface IExecutionEngine
{
    Task<ExecutionResult> ExecuteAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default);
}