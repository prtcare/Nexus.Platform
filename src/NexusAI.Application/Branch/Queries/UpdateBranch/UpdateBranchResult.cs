using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Commands.UpdateBranch;

public sealed record UpdateBranchResult(
    BranchId BranchId,
    string Name,
    string Description,
    BranchStatus Status);