using NexusAI.Core.Abstractions;
using NexusAI.Domain.Snapshot;

namespace NexusAI.Application.Snapshot.Commands;

public sealed class CreateSnapshotHandler
{
    private readonly ISnapshotRepository _repository;
    private readonly IClock _clock;

    public CreateSnapshotHandler(
        ISnapshotRepository repository,
        IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<CreateSnapshotResult> HandleAsync(
        CreateSnapshotCommand command,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new NexusAI.Domain.Snapshot.Snapshot(
            SnapshotId.New(),
            command.BranchId,
            command.ConversationId,
            command.Name,
            command.State,
            _clock.UtcNow);

        await _repository.AddAsync(
            snapshot,
            cancellationToken);

        return new CreateSnapshotResult(
            snapshot.Id,
            snapshot.Name);
    }
}