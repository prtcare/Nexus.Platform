using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Queries.GetBranch;

public sealed class GetBranchHandler
{
    private readonly IBranchRepository _repository;

    public GetBranchHandler(
        IBranchRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetBranchResult?> HandleAsync(
        GetBranchQuery query,
        CancellationToken cancellationToken = default)
    {
        var branch = await _repository.GetAsync(
            query.BranchId,
            cancellationToken);

        if (branch is null)
        {
            return null;
        }

        return new GetBranchResult(
            branch.Id,
            branch.ConversationId,
            branch.Name,
            branch.Description,
            branch.Status,
            branch.CreatedAt);
    }
}