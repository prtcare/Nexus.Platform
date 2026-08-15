namespace NexusAI.Api.Endpoints.Branches;

public sealed record ListBranchResponse(
    Guid BranchId,
    string Name,
    string Description,
    int Status,
    DateTimeOffset CreatedAt);