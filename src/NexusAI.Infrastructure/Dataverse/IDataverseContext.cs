namespace NexusAI.Infrastructure.Dataverse;

public interface IDataverseContext
{
    IQueryable<T> Set<T>() where T : class;

    Task AddAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class;

    Task UpdateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class;
}