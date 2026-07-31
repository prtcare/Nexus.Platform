namespace NexusAI.Domain.Project;

public interface IProjectRepository
{
    Task AddAsync(
        Project project,
        CancellationToken cancellationToken = default);

    Task<Project?> GetAsync(
        ProjectId id,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Project project,
        CancellationToken cancellationToken = default);
}