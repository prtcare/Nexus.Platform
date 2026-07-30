using NexusAI.Application.Abstractions;
using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;

namespace NexusAI.Application.Workspaces.Commands.CreateWorkspace;

public sealed class CreateWorkspaceHandler
    : ICommandHandler<CreateWorkspaceCommand, CreateWorkspaceResult>
{
    private readonly IWorkspaceRepository _repository;

    public CreateWorkspaceHandler(
        IWorkspaceRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateWorkspaceResult> HandleAsync(
        CreateWorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        var workspace = new Workspace(
            WorkspaceId.New(),
            command.Name,
            DateTimeOffset.UtcNow);

        await _repository.AddAsync(
            workspace,
            cancellationToken);

        return new CreateWorkspaceResult(
            workspace.Id,
            workspace.Name);
    }
}