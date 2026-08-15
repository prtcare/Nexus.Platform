namespace NexusAI.Api.Endpoints.WorkItems;

public sealed record CreateWorkItemRequest(
    string Title,
    string Description,
    int Type);