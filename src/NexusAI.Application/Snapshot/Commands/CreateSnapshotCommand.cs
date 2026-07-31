using NexusAI.Domain.Branch;

namespace NexusAI.Application.Snapshot.Commands;

public sealed record CreateSnapshotCommand(
    BranchId BranchId,
    string Description);