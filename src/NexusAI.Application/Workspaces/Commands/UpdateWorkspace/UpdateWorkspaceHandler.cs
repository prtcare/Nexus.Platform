using NexusAI.Domain.Workspace;

namespace NexusAI.Application.Workspaces.Commands.UpdateWorkspace;

public sealed class UpdateWorkspaceHandler
{
    private readonly IWorkspaceRepository _repository;

    public UpdateWorkspaceHandler(
        IWorkspaceRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateWorkspaceResult?> HandleAsync(
        UpdateWorkspaceCommand command,
        CancellationToken cancellationToken = default)
    {
        var workspace = await _repository.GetAsync(
            command.WorkspaceId,
            cancellationToken);

        if (workspace is null)
        {
            return null;
        }

        workspace.Rename(command.Name);
        workspace.ChangeOwner(command.Owner);
        workspace.ChangeDescription(command.Description);

        await _repository.UpdateAsync(
            workspace,
            cancellationToken);

        return new UpdateWorkspaceResult(
            workspace.Id,
            workspace.Name,
            workspace.Owner,
            workspace.Description);
    }
}