namespace NexusAI.Infrastructure.Dataverse.Common;

public interface IRepositoryMapper<TDomain, TEntity>
{
    TEntity ToEntity(TDomain domain);

    TDomain ToDomain(TEntity entity);
}