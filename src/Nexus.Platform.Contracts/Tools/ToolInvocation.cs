using Nexus.Platform.Contracts.Models;

namespace Nexus.Platform.Contracts.Tools;

public sealed record ToolInvocation
{
    public required string ToolId { get; init; }

    public string ArgumentsJson { get; init; } = "{}";

    public required InvocationIdentity Identity { get; init; }
}
