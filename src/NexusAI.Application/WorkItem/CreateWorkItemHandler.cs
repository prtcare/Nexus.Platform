using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.WorkItem;

public sealed class CreateWorkItemHandler
{
    private readonly IWorkItemRepository _repository;

    public CreateWorkItemHandler(IWorkItemRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateWorkItemResult> HandleAsync(
        CreateWorkItemCommand command,
        CancellationToken cancellationToken = default)
    {
        var workItem = new Domain.WorkItem.WorkItem(
            WorkItemId.New(),
            command.ProjectId,
            command.Title,
            command.Type,
            DateTimeOffset.UtcNow);

        await _repository.AddAsync(workItem, cancellationToken);

        return new CreateWorkItemResult(workItem.Id);
    }
}