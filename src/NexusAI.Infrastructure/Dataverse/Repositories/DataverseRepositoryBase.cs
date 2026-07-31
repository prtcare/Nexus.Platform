using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public abstract class DataverseRepositoryBase<TDomain, TEntity>
    where TDomain : class
    where TEntity : class
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

    protected async Task UpdateEntityAsync(
        TDomain domain,
        CancellationToken cancellationToken = default)
    {
        var entity = Mapper.ToEntity(domain);

        await Client.Context.UpdateAsync(
            entity,
            cancellationToken);
    }
}