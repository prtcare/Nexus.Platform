namespace NexusAI.Api.Endpoints.Branches;

public sealed record UpdateBranchRequest(
    string Name,
    string Description,
    int Status);