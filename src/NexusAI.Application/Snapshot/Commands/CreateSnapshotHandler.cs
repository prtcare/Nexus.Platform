using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Commands;

public sealed class CreateSnapshotHandler
{
    private readonly ISnapshotRepository _repository;

    public CreateSnapshotHandler(
        ISnapshotRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateSnapshotResult> HandleAsync(
        CreateSnapshotCommand command,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new NexusAI.Domain.Snapshot.Snapshot(
            SnapshotId.New(),
            command.BranchId,
            command.Description,
            SnapshotStatus.Draft,
            DateTimeOffset.UtcNow);

        await _repository.AddAsync(
            snapshot,
            cancellationToken);

        return new CreateSnapshotResult(
            snapshot.Id);
    }
}