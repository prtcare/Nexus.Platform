using Nexus.Platform.Contracts.Models;
using Nexus.Platform.Core.Models;
using Xunit;

namespace Nexus.Platform.Tests;

public sealed class ModelCatalogTests
{
    private static readonly ModelDescriptor Gpt4o = new(
        "gpt-4o",
        "OpenAI",
        ModelCapabilities.Chat | ModelCapabilities.ToolUse | ModelCapabilities.Streaming,
        ContextWindow: 128_000,
        CostPer1kIn: 0.0025m,
        CostPer1kOut: 0.010m,
        LatencyClass.Medium);

    private static readonly ModelDescriptor ClaudeSonnet = new(
        "claude-sonnet-4-5",
        "Anthropic",
        ModelCapabilities.Chat | ModelCapabilities.Reasoning | ModelCapabilities.ToolUse,
        ContextWindow: 200_000,
        CostPer1kIn: 0.003m,
        CostPer1kOut: 0.015m,
        LatencyClass.Medium);

    private static readonly ModelDescriptor GptMini = new(
        "gpt-4o-mini",
        "OpenAI",
        ModelCapabilities.Chat | ModelCapabilities.Streaming,
        ContextWindow: 128_000,
        CostPer1kIn: 0.00015m,
        CostPer1kOut: 0.0006m,
        LatencyClass.Low);

    private static readonly ModelDescriptor[] AllModels = [Gpt4o, ClaudeSonnet, GptMini];

    private sealed class StubSource(ModelDescriptor[] models) : IModelCatalogSource
    {
        public Task<IReadOnlyList<ModelDescriptor>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelDescriptor>>(models);
    }

    private static AggregatingModelCatalog CatalogWith(params IModelCatalogSource[] sources)
        => new(sources);

    private static async Task<IReadOnlyList<ModelDescriptor>> ListAsync(IModelCatalog query) =>
        await query.ListAsync(ModelQuery.Any);

    [Fact]
    public async Task ListAsync_AggregatesAllSources()
    {
        var catalog = CatalogWith(new StubSource([Gpt4o, ClaudeSonnet]), new StubSource([GptMini]));

        var result = await ListAsync(catalog);

        Assert.Equal(AllModels, result);
    }

    [Fact]
    public async Task ListAsync_NoQuery_ReturnsEveryDescriptor()
    {
        var catalog = CatalogWith(new StubSource(AllModels));

        var result = await ListAsync(catalog);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListAsync_FiltersByVendor_CaseInsensitive()
    {
        var catalog = CatalogWith(new StubSource(AllModels));
        var query = new ModelQuery { Vendor = "openai" };

        var result = await catalog.ListAsync(query);

        Assert.Equal([Gpt4o, GptMini], result);
    }

    [Fact]
    public async Task ListAsync_FiltersByRequiredCapabilities()
    {
        var catalog = CatalogWith(new StubSource(AllModels));
        var query = new ModelQuery { RequiredCapabilities = ModelCapabilities.Reasoning };

        var result = await catalog.ListAsync(query);

        Assert.Equal([ClaudeSonnet], result);
    }

    [Fact]
    public async Task ListAsync_FiltersByMinContextWindow()
    {
        var catalog = CatalogWith(new StubSource(AllModels));
        var query = new ModelQuery { MinContextWindow = 150_000 };

        var result = await catalog.ListAsync(query);

        Assert.Equal([ClaudeSonnet], result);
    }

    [Fact]
    public async Task ListAsync_FiltersByMaxCostPer1kIn()
    {
        var catalog = CatalogWith(new StubSource(AllModels));
        var query = new ModelQuery { MaxCostPer1kIn = 0.0005m };

        var result = await catalog.ListAsync(query);

        Assert.Equal([GptMini], result);
    }

    [Fact]
    public async Task ListAsync_CombinesMultipleFilters()
    {
        var catalog = CatalogWith(new StubSource(AllModels));
        var query = new ModelQuery
        {
            Vendor = "OpenAI",
            RequiredCapabilities = ModelCapabilities.Streaming,
            MinContextWindow = 100_000,
            MaxCostPer1kIn = 0.01m
        };

        var result = await catalog.ListAsync(query);

        Assert.Equal([Gpt4o, GptMini], result);
    }

    [Fact]
    public async Task ListAsync_NoSourceMatches_ReturnsEmpty()
    {
        var catalog = CatalogWith(new StubSource(AllModels));
        var query = new ModelQuery { Vendor = "Google" };

        var result = await catalog.ListAsync(query);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListAsync_EmptyCatalog_ReturnsEmpty()
    {
        var catalog = CatalogWith();

        var result = await ListAsync(catalog);

        Assert.Empty(result);
    }
}
