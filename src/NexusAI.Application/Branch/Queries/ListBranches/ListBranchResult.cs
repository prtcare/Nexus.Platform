using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Queries.ListBranches;

public sealed record ListBranchResult(
    BranchId BranchId,
    string Name,
    string Description,
    BranchStatus Status,
    DateTimeOffset CreatedAt);