using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse;

public sealed class InMemoryDataverseContext : IDataverseContext
{
    private readonly Dictionary<Type, IList<object>> _tables = new();

    public Task CreateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (!_tables.TryGetValue(typeof(T), out var table))
        {
            table = new List<object>();
            _tables[typeof(T)] = table;
        }

        table.Add(entity);

        return Task.CompletedTask;
    }

    public Task<T?> RetrieveAsync<T>(
    Guid id,
    CancellationToken cancellationToken = default)
    where T : class
    {
        if (!_tables.TryGetValue(typeof(T), out var table))
        {
            return Task.FromResult<T?>(default);
        }

        var entity = table
            .Cast<T>()
            .FirstOrDefault(item =>
            {
                var property = item.GetType().GetProperty("Id");

                return property != null &&
                       property.PropertyType == typeof(Guid) &&
                       (Guid)property.GetValue(item)! == id;
            });

        return Task.FromResult(entity);
    }

    public Task<IReadOnlyList<TEntity>> RetrieveMultipleAsync<TEntity>(
    Func<TEntity, bool> predicate,
    CancellationToken cancellationToken = default)
    where TEntity : DataverseEntity
    {
        if (!_tables.TryGetValue(typeof(TEntity), out var table))
        {
            return Task.FromResult<IReadOnlyList<TEntity>>([]);
        }

        var result = table
            .Cast<TEntity>()
            .Where(predicate)
            .ToList();

        return Task.FromResult<IReadOnlyList<TEntity>>(result);
    }

    public Task UpdateAsync<T>(
    T entity,
    CancellationToken cancellationToken = default)
    where T : class
    {
        if (!_tables.TryGetValue(typeof(T), out var table))
        {
            return Task.CompletedTask;
        }

        var idProperty = typeof(T).GetProperty("Id");

        if (idProperty is null || idProperty.PropertyType != typeof(Guid))
        {
            return Task.CompletedTask;
        }

        var id = (Guid)idProperty.GetValue(entity)!;

        for (int i = 0; i < table.Count; i++)
        {
            var existing = table[i];

            var existingId = (Guid)idProperty.GetValue(existing)!;

            if (existingId == id)
            {
                table[i] = entity!;
                break;
            }
        }

        return Task.CompletedTask;
    }
}