namespace NexusAI.Infrastructure.Dataverse.Common;

public interface IRepository<TDomain, TId>
{
    Task AddAsync(
        TDomain domain,
        CancellationToken cancellationToken = default);

    Task<TDomain?> GetAsync(
        TId id,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        TDomain domain,
        CancellationToken cancellationToken = default);
}