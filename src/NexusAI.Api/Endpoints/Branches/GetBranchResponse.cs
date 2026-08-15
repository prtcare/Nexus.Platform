namespace NexusAI.Api.Endpoints.Branches;

public sealed record GetBranchResponse(
    Guid BranchId,
    Guid ConversationId,
    string Name,
    string Description,
    int Status,
    DateTimeOffset CreatedAt);