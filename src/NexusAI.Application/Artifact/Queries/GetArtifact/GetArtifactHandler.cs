using NexusAI.Domain.Artifact;

namespace NexusAI.Application.Artifact.Queries.GetArtifact;

public sealed class GetArtifactHandler
{
    private readonly IArtifactRepository _repository;

    public GetArtifactHandler(
        IArtifactRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetArtifactResult?> HandleAsync(
        GetArtifactQuery query,
        CancellationToken cancellationToken = default)
    {
        var artifact = await _repository.GetAsync(
            query.ArtifactId,
            cancellationToken);

        if (artifact is null)
        {
            return null;
        }

        return new GetArtifactResult(
            artifact.Id,
            artifact.WorkItemId,
            artifact.Name,
            artifact.Type,
            artifact.Content,
            artifact.CreatedAt);
    }
}
