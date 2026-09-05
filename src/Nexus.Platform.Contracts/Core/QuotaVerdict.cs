namespace Nexus.Platform.Contracts.Governance;

public sealed record QuotaVerdict
{
    public required bool Allowed { get; init; }

    public string? Reason { get; init; }

    public static QuotaVerdict Allow() => new() { Allowed = true };

    public static QuotaVerdict Deny(string reason) => new() { Allowed = false, Reason = reason };
}
