using Nexus.Platform.Contracts.Models;

namespace Nexus.Platform.Contracts.Governance;

public sealed record UsageRecord
{
    public required InvocationIdentity Identity { get; init; }

    public required string ModelId { get; init; }

    public required ModelUsage Usage { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }
}
