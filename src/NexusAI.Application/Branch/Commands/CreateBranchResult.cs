using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Commands;

public sealed record CreateBranchResult(
    BranchId BranchId,
    string Name);