using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.WorkItem;

public sealed class GetWorkItemHandler
{
    private readonly IWorkItemRepository _repository;

    public GetWorkItemHandler(
        IWorkItemRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetWorkItemResult?> HandleAsync(
        GetWorkItemQuery query)
    {
        var workItem = await _repository.GetAsync(query.WorkItemId);

        if (workItem is null)
        {
            return null;
        }

        return new GetWorkItemResult(
            workItem.Id,
            workItem.ProjectId,
            workItem.Title,
            workItem.Description,
            workItem.Type,
            workItem.Status,
            workItem.CreatedAt);
    }
}