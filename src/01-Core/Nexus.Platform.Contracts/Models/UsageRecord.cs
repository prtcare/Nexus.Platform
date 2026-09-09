using Nexus.Platform.Contracts.Core;

namespace Nexus.Platform.Contracts.Models;

public sealed record UsageRecord
{
    public required InvocationIdentity Identity { get; init; }

    public required string ModelId { get; init; }

    public required ModelUsage Usage { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }
}
