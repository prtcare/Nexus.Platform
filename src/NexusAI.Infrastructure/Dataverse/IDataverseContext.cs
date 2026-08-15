using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse;

public interface IDataverseContext
{
    Task CreateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<T?> RetrieveAsync<T>(
        Guid id,
        CancellationToken cancellationToken = default)
        where T : class;

    Task UpdateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<IReadOnlyList<TEntity>> RetrieveMultipleAsync<TEntity>(
    Func<TEntity, bool> predicate,
    CancellationToken cancellationToken = default)
    where TEntity : DataverseEntity;
}