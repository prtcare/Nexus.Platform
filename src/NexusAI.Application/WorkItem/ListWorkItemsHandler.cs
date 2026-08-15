using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.WorkItem;

public sealed class ListWorkItemsHandler
{
    private readonly IWorkItemRepository _repository;

    public ListWorkItemsHandler(
        IWorkItemRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ListWorkItemsResult>> HandleAsync(
        ListWorkItemsQuery query,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.ListByProjectAsync(
            query.ProjectId,
            cancellationToken);

        return items
            .Select(item => new ListWorkItemsResult(
                item.Id,
                item.ProjectId,
                item.Title,
                item.Description,
                item.Type,
                item.Status,
                item.CreatedAt))
            .ToList();
    }
}