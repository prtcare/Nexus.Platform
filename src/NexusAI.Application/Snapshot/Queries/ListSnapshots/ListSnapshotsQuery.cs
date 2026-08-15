using NexusAI.Domain.Branch;

namespace NexusAI.Application.Snapshot.Queries.ListSnapshots;

public sealed record ListSnapshotsQuery(
    BranchId BranchId);