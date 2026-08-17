namespace Nexus.Platform.Contracts.Tools;

public sealed record ToolResult
{
    public required bool Success { get; init; }

    public string? OutputJson { get; init; }

    public string? Error { get; init; }
}
