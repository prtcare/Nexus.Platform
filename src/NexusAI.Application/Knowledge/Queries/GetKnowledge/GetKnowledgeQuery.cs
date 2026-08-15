using NexusAI.Domain.Knowledge;

namespace NexusAI.Application.Knowledge.Queries.GetKnowledge;

public sealed record GetKnowledgeQuery(
    KnowledgeId KnowledgeId);