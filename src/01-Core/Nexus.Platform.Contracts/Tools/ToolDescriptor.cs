namespace Nexus.Platform.Contracts.Tools;

public sealed record ToolDescriptor
{
    public required string ToolId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required SideEffectClass SideEffect { get; init; }

    public string? ParameterSchemaJson { get; init; }
}
