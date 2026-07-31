using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Domain.Adr;

public interface IAdrRepository
    : IRepository<Adr, AdrId>
{
}