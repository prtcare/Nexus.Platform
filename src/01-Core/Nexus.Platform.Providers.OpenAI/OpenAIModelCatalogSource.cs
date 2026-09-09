using Nexus.Platform.Contracts.Models;
using Nexus.Platform.Core.Models;

namespace Nexus.Platform.Providers.OpenAI;

public sealed class OpenAIModelCatalogSource : IModelCatalogSource
{
    private static readonly IReadOnlyList<ModelDescriptor> Descriptors =
    [
        new ModelDescriptor(
            "openai:gpt-4.1",
            "openai",
            ModelCapabilities.Chat | ModelCapabilities.Reasoning | ModelCapabilities.ToolUse |
            ModelCapabilities.Streaming | ModelCapabilities.StructuredOutput | ModelCapabilities.LongContext,
            1_047_576,
            0.002m,
            0.008m,
            LatencyClass.Medium),

        new ModelDescriptor(
            "openai:gpt-4.1-mini",
            "openai",
            ModelCapabilities.Chat | ModelCapabilities.ToolUse | ModelCapabilities.Streaming |
            ModelCapabilities.StructuredOutput,
            1_047_576,
            0.0004m,
            0.0016m,
            LatencyClass.Low),

        new ModelDescriptor(
            "openai:gpt-4o",
            "openai",
            ModelCapabilities.Chat | ModelCapabilities.Vision | ModelCapabilities.ToolUse |
            ModelCapabilities.Streaming,
            128_000,
            0.0025m,
            0.01m,
            LatencyClass.Medium)
    ];

    public Task<IReadOnlyList<ModelDescriptor>> ListAsync(CancellationToken ct = default)
        => Task.FromResult(Descriptors);
}
