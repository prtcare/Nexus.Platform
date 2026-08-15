using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.WorkItem;

public sealed class UpdateWorkItemHandler
{
    private readonly IWorkItemRepository _repository;

    public UpdateWorkItemHandler(
        IWorkItemRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateWorkItemResult?> HandleAsync(
        UpdateWorkItemCommand command,
        CancellationToken cancellationToken = default)
    {
        var workItem = await _repository.GetAsync(
            command.WorkItemId,
            cancellationToken);

        if (workItem is null)
        {
            return null;
        }

        workItem.UpdateTitle(command.Title);
        workItem.UpdateDescription(command.Description);
        workItem.ChangeType(command.Type);
        workItem.ChangeStatus(command.Status);

        await _repository.UpdateAsync(
            workItem,
            cancellationToken);

        return new UpdateWorkItemResult(
            workItem.Id);
    }
}