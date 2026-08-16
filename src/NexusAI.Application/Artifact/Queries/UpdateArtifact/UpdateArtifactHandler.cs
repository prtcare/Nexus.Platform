using NexusAI.Domain.Artifact;

namespace NexusAI.Application.Artifact.Commands.UpdateArtifact;

public sealed class UpdateArtifactHandler
{
    private readonly IArtifactRepository _repository;

    public UpdateArtifactHandler(
        IArtifactRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateArtifactResult?> HandleAsync(
        UpdateArtifactCommand command,
        CancellationToken cancellationToken = default)
    {
        var artifact = await _repository.GetAsync(
            command.ArtifactId,
            cancellationToken);

        if (artifact is null)
        {
            return null;
        }

        artifact.Rename(command.Name);
        artifact.ChangeType(command.Type);
        artifact.UpdateContent(command.Content);

        await _repository.UpdateAsync(
            artifact,
            cancellationToken);

        return new UpdateArtifactResult(
            artifact.Id,
            artifact.Name,
            artifact.Type,
            artifact.Content);
    }
}
