namespace Nexus.Platform.Contracts.Models;

public sealed record ModelMessage
{
    public required ModelRole Role { get; init; }

    public required string Content { get; init; }
}
