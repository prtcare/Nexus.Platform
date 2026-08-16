using NexusAI.Domain.Artifact;

namespace NexusAI.Application.Artifact.Queries.ListArtifacts;

public sealed class ListArtifactsHandler
{
    private readonly IArtifactRepository _repository;

    public ListArtifactsHandler(
        IArtifactRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ListArtifactResult>> HandleAsync(
        ListArtifactsQuery query,
        CancellationToken cancellationToken = default)
    {
        var artifacts =
            await _repository.ListByWorkItemAsync(
                query.WorkItemId,
                cancellationToken);

        return artifacts
            .Select(artifact =>
                new ListArtifactResult(
                    artifact.Id,
                    artifact.Name,
                    artifact.Type,
                    artifact.CreatedAt))
            .ToList();
    }
}
