using Nexus.Platform.Contracts.Models;

namespace Nexus.Platform.Contracts.Governance;

public sealed record AuditEntry
{
    public required InvocationIdentity Identity { get; init; }

    public required string Action { get; init; }

    public required bool Success { get; init; }

    public string? Detail { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
