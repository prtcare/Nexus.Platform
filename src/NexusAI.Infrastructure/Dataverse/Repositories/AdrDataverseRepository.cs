using NexusAI.Domain.Adr;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;
using NexusAI.Infrastructure.Dataverse.Mapping;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class AdrDataverseRepository
    : DataverseRepositoryBase<Adr, AdrEntity, AdrId>,
      IAdrRepository
{
    public AdrDataverseRepository(
        IDataverseClient client,
        IRepositoryMapper<Adr, AdrEntity> mapper)
        : base(client, mapper)
    {
    }

    public override Task AddAsync(
        Adr domain,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(domain, cancellationToken);
    }

    public override Task<Adr?> GetAsync(
        AdrId id,
        CancellationToken cancellationToken = default)
    {
        return RetrieveDomainAsync(id.Value, cancellationToken);
    }

    public override Task UpdateAsync(
        Adr domain,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(domain, cancellationToken);
    }
}