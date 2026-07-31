using NexusAI.Domain.Knowledge;

namespace NexusAI.Application.Adr.Commands;

public sealed record CreateAdrCommand(
    KnowledgeId KnowledgeId,
    string Title,
    string Decision);