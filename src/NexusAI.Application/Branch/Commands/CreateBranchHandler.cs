using NexusAI.Core.Abstractions;
using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Commands;

public sealed class CreateBranchHandler
{
    private readonly IBranchRepository _repository;
    private readonly IClock _clock;

    public CreateBranchHandler(
        IBranchRepository repository,
        IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<CreateBranchResult> HandleAsync(
        CreateBranchCommand command,
        CancellationToken cancellationToken = default)
    {
        var branch =
            new NexusAI.Domain.Branch.Branch(
                BranchId.New(),
                command.ConversationId,
                command.Name,
                command.Description,
                BranchStatus.Active,
                _clock.UtcNow);

        await _repository.AddAsync(
            branch,
            cancellationToken);

        return new CreateBranchResult(
            branch.Id,
            branch.Name);
    }
}