namespace NexusAI.Infrastructure.Dataverse.Mapping;

public interface IDataverseMapper<TDomain, TEntity>
    where TDomain : class
    where TEntity : class
{
    TEntity ToEntity(TDomain domain);

    TDomain ToDomain(TEntity entity);
}