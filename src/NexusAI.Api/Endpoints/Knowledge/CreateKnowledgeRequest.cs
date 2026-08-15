using NexusAI.Domain.Knowledge;

namespace NexusAI.Api.Endpoints.Knowledge;

public sealed record CreateKnowledgeRequest(
    string Title,
    string Content,
    KnowledgeType Type);