namespace Nexus.Platform.Contracts.Identity;

public sealed record ResolvedIdentity
{
    public required string TenantId { get; init; }

    public required string ProductId { get; init; }

    public required string UserId { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];
}
