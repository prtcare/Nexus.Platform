using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Knowledge;

namespace NexusAI.Application.Knowledge.Commands;

public sealed record CreateKnowledgeCommand(
    WorkspaceId WorkspaceId,
    string Title,
    string Content,
    KnowledgeSource Source);