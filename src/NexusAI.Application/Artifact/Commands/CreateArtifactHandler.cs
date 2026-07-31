using NexusAI.Domain.Artifact;

namespace NexusAI.Application.Artifact.Commands;

public sealed class CreateArtifactHandler
{
    private readonly IArtifactRepository _repository;

    public CreateArtifactHandler(
        IArtifactRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateArtifactResult> HandleAsync(
        CreateArtifactCommand command,
        CancellationToken cancellationToken = default)
    {
        var artifact = new NexusAI.Domain.Artifact.Artifact(
            ArtifactId.New(),
            command.WorkItemId,
            command.Name,
            command.Type,
            command.Content,
            DateTimeOffset.UtcNow);

        await _repository.AddAsync(
            artifact,
            cancellationToken);

        return new CreateArtifactResult(
            artifact.Id);
    }
}