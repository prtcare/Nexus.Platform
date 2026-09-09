namespace Nexus.Platform.Contracts.Models;

public sealed record ModelInvocationResult
{
    public required bool Success { get; init; }

    public ModelMessage? Message { get; init; }

    public string? Error { get; init; }

    public ModelUsage Usage { get; init; } = ModelUsage.Zero;

    public string? ModelUsed { get; init; }
}
