using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Knowledge;

namespace NexusAI.Application.Knowledge.Queries.ListKnowledge;

public sealed record ListKnowledgeResult(
    KnowledgeId KnowledgeId,
    WorkspaceId WorkspaceId,
    string Title,
    KnowledgeType Type,
    DateTimeOffset CreatedAt);