namespace NexusAI.Infrastructure.Dataverse;

public sealed class InMemoryDataverseContext : IDataverseContext
{
    private readonly Dictionary<Type, IList<object>> _tables = new();

    public IQueryable<T> Set<T>()
        where T : class
    {
        if (!_tables.TryGetValue(typeof(T), out var table))
        {
            table = new List<object>();
            _tables[typeof(T)] = table;
        }

        return table.Cast<T>().AsQueryable();
    }

    public Task AddAsync<T>(
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

    public Task UpdateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        return Task.CompletedTask;
    }
}