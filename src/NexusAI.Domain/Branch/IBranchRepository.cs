using NexusAI.Domain.Common;
using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Domain.Branch;

public interface IBranchRepository
    : IRepository<Branch, BranchId>
{
}