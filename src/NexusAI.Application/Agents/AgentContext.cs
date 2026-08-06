using ExecutionContextModel = NexusAI.Application.Execution.ExecutionContext;

namespace NexusAI.Application.Agents;

public sealed record AgentContext(
    ExecutionContextModel ExecutionContext,
    AgentType AgentType);