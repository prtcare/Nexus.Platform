using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Queries.GetBranch;

public sealed record GetBranchQuery(
    BranchId BranchId);