using Nexus.Platform.Contracts.Tools;

namespace Nexus.Platform.Contracts.Models;

public sealed record ModelInvocation
{
    public required string ModelId { get; init; }

    public required IReadOnlyList<ModelMessage> Messages { get; init; }

    public IReadOnlyList<ToolDescriptor> Tools { get; init; } = [];

    public decimal? MaxCost { get; init; }

    public required InvocationIdentity Identity { get; init; }
}
