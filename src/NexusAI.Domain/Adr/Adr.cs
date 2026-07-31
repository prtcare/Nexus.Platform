using NexusAI.Domain.Common;
using NexusAI.Domain.Knowledge;

namespace NexusAI.Domain.Adr;

public sealed class Adr : Entity<AdrId>
{
    public Adr(
        AdrId id,
        KnowledgeId knowledgeId,
        string title,
        string decision,
        AdrStatus status,
        DateTimeOffset createdAt)
        : base(id)
    {
        KnowledgeId = knowledgeId;
        Title = title;
        Decision = decision;
        Status = status;
        CreatedAt = createdAt;
    }

    public KnowledgeId KnowledgeId { get; }

    public string Title { get; private set; }

    public string Decision { get; private set; }

    public AdrStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public void Accept()
    {
        Status = AdrStatus.Accepted;
    }

    public void Deprecate()
    {
        Status = AdrStatus.Deprecated;
    }

    public void Supersede()
    {
        Status = AdrStatus.Superseded;
    }
}