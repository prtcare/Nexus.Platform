using NexusAI.Domain.Branch;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class BranchDataverseRepository
    : DataverseRepositoryBase<Branch, BranchEntity, BranchId>,
      IBranchRepository
{
    public BranchDataverseRepository(
        IDataverseClient client,
        IRepositoryMapper<Branch, BranchEntity> mapper)
        : base(client, mapper)
    {
    }

    public override Task AddAsync(
        Branch branch,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(branch, cancellationToken);
    }

    public override Task<Branch?> GetAsync(
        BranchId id,
        CancellationToken cancellationToken = default)
    {
        return RetrieveDomainAsync(id.Value, cancellationToken);
    }

    public override Task UpdateAsync(
        Branch branch,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(branch, cancellationToken);
    }
}