using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Commands.UpdateBranch;

public sealed class UpdateBranchHandler
{
    private readonly IBranchRepository _repository;

    public UpdateBranchHandler(
        IBranchRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateBranchResult?> HandleAsync(
        UpdateBranchCommand command,
        CancellationToken cancellationToken = default)
    {
        var branch = await _repository.GetAsync(
            command.BranchId,
            cancellationToken);

        if (branch is null)
        {
            return null;
        }

        branch.Rename(command.Name);
        branch.UpdateDescription(command.Description);
        branch.ChangeStatus(command.Status);

        await _repository.UpdateAsync(
            branch,
            cancellationToken);

        return new UpdateBranchResult(
            branch.Id,
            branch.Name,
            branch.Description,
            branch.Status);
    }
}