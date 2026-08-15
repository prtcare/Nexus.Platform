using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Knowledge;

namespace NexusAI.Application.Knowledge.Queries.GetKnowledge;

public sealed record GetKnowledgeResult(
    KnowledgeId KnowledgeId,
    WorkspaceId WorkspaceId,
    string Title,
    string Content,
    KnowledgeType Type,
    DateTimeOffset CreatedAt);