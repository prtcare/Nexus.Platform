using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Commands.UpdateSnapshot;

public sealed class UpdateSnapshotHandler
{
    private readonly ISnapshotRepository _repository;

    public UpdateSnapshotHandler(
        ISnapshotRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateSnapshotResult?> HandleAsync(
        UpdateSnapshotCommand command,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _repository.GetAsync(
            command.SnapshotId,
            cancellationToken);

        if (snapshot is null)
        {
            return null;
        }

        snapshot.Rename(command.Name);
        snapshot.UpdateState(command.State);

        await _repository.UpdateAsync(
            snapshot,
            cancellationToken);

        return new UpdateSnapshotResult(
            snapshot.Id,
            snapshot.Name,
            snapshot.State);
    }
}