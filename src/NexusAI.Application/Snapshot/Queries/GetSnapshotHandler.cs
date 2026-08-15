using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Queries.GetSnapshot;

public sealed class GetSnapshotHandler
{
    private readonly ISnapshotRepository _repository;

    public GetSnapshotHandler(
        ISnapshotRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetSnapshotResult?> HandleAsync(
        GetSnapshotQuery query,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _repository.GetAsync(
            query.SnapshotId,
            cancellationToken);

        if (snapshot is null)
        {
            return null;
        }

        return new GetSnapshotResult(
            snapshot.Id,
            snapshot.BranchId,
            snapshot.ConversationId,
            snapshot.Name,
            snapshot.State,
            snapshot.CreatedAt);
    }
}