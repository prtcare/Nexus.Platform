namespace NexusAI.Api.Endpoints.Branches;

public sealed record CreateBranchResponse(
    Guid BranchId,
    string Name);