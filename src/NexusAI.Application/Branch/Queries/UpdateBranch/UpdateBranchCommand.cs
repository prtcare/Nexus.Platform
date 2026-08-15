using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Commands.UpdateBranch;

public sealed record UpdateBranchCommand(
    BranchId BranchId,
    string Name,
    string Description,
    BranchStatus Status);