using NexusAI.Domain.Branch;

namespace NexusAI.Application.Branch.Queries.ListBranches;

public sealed class ListBranchesHandler
{
    private readonly IBranchRepository _repository;

    public ListBranchesHandler(
        IBranchRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ListBranchResult>> HandleAsync(
        ListBranchesQuery query,
        CancellationToken cancellationToken = default)
    {
        var branches =
            await _repository.ListByConversationAsync(
                query.ConversationId,
                cancellationToken);

        return branches
            .Select(branch =>
                new ListBranchResult(
                    branch.Id,
                    branch.Name,
                    branch.Description,
                    branch.Status,
                    branch.CreatedAt))
            .ToList();
    }
}