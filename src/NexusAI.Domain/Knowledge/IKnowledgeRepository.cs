using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Domain.Knowledge;

public interface IKnowledgeRepository
    : IRepository<Knowledge, KnowledgeId>
{
}