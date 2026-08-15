using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Queries.ListSnapshots;

public sealed class ListSnapshotsHandler
{
    private readonly ISnapshotRepository _repository;

    public ListSnapshotsHandler(
        ISnapshotRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ListSnapshotResult>> HandleAsync(
        ListSnapshotsQuery query,
        CancellationToken cancellationToken = default)
    {
        var snapshots =
            await _repository.ListByBranchAsync(
                query.BranchId,
                cancellationToken);

        return snapshots
            .Select(snapshot =>
                new ListSnapshotResult(
                    snapshot.Id,
                    snapshot.Name,
                    snapshot.State,
                    snapshot.CreatedAt))
            .ToList();
    }
}