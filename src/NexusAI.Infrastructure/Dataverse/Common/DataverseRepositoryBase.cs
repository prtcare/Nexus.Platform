using NexusAI.Domain.Common;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Common;

public abstract class DataverseRepositoryBase<TDomain, TEntity, TId>
    : IRepository<TDomain, TId>
    where TDomain : class
    where TEntity : DataverseEntity
{
    protected DataverseRepositoryBase(
        IDataverseClient client,
        IRepositoryMapper<TDomain, TEntity> mapper)
    {
        Client = client;
        Mapper = mapper;
    }

    protected IDataverseClient Client { get; }

    protected IRepositoryMapper<TDomain, TEntity> Mapper { get; }

    protected async Task CreateAsync(
        TDomain domain,
        CancellationToken cancellationToken = default)
    {
        var entity = Mapper.ToEntity(domain);

        await Client.Context.CreateAsync(
            entity,
            cancellationToken);
    }

    protected async Task<TEntity?> RetrieveAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await Client.Context.RetrieveAsync<TEntity>(
            id,
            cancellationToken);
    }
    protected async Task<TDomain?> RetrieveDomainAsync(
    Guid id,
    CancellationToken cancellationToken = default)
    {
        var entity = await Client.Context.RetrieveAsync<TEntity>(
            id,
            cancellationToken);

        if (entity is null)
        {
            return default;
        }

        return Mapper.ToDomain(entity);
    }
    protected async Task UpdateEntityAsync(
        TDomain domain,
        CancellationToken cancellationToken = default)
    {
        var entity = Mapper.ToEntity(domain);

        await Client.Context.UpdateAsync(
            entity,
            cancellationToken);
    }

    public abstract Task AddAsync(
        TDomain domain,
        CancellationToken cancellationToken = default);

    public abstract Task<TDomain?> GetAsync(
        TId id,
        CancellationToken cancellationToken = default);

    public abstract Task UpdateAsync(
        TDomain domain,
        CancellationToken cancellationToken = default);

    protected async Task<IReadOnlyList<TDomain>> RetrieveMultipleDomainAsync(
    Func<TEntity, bool> predicate,
    CancellationToken cancellationToken = default)
    {
        var entities = await Client.Context.RetrieveMultipleAsync(
            predicate,
            cancellationToken);

        return entities
            .Select(Mapper.ToDomain)
            .ToList();
    }

    protected async Task<IReadOnlyList<TDomain>> RetrieveMultipleDomainAsync(
    string filterAttributeName,
    Guid filterValue,
    Func<TEntity, bool> predicate,
    CancellationToken cancellationToken = default)
    {
        var entities = await Client.Context.RetrieveMultipleAsync(
            filterAttributeName,
            filterValue,
            predicate,
            cancellationToken);

        return entities
            .Select(Mapper.ToDomain)
            .ToList();
    }
}