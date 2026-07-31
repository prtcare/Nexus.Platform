using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Commands;

public sealed class CreateBranchHandler
{
    private readonly IBranchRepository _repository;

    public CreateBranchHandler(
        IBranchRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateBranchResult> HandleAsync(
        CreateBranchCommand command,
        CancellationToken cancellationToken = default)
    {
        var branch = new NexusAI.Domain.Branch.Branch(
            BranchId.New(),
            command.ConversationId,
            command.Name,
            BranchStatus.Active,
            DateTimeOffset.UtcNow);

        await _repository.AddAsync(
            branch,
            cancellationToken);

        return new CreateBranchResult(branch.Id);
    }
}