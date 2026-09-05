using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Platform.Contracts.Core;
using Nexus.Platform.Contracts.Models;
using Nexus.Platform.Core;
using Nexus.Platform.Core.Models;
using Nexus.Platform.Providers.OpenAI;

namespace Nexus.Platform.SmokeHost;

/// <summary>
/// Composes the platform the way a consuming host would - AddNexusPlatform +
/// AddOpenAIModelProvider, with the API key resolved from the set-openai-key.ps1
/// store into configuration - then runs a real chat turn through the routing gateway.
/// </summary>
public static class SmokeRunner
{
    public const string DefaultModelId = "openai:gpt-4.1-mini";

    public static bool KeyAvailable()
        => !string.IsNullOrWhiteSpace(
            new StoreSecretResolver().ResolveAsync("Platform:Providers:OpenAI:ApiKey").GetAwaiter().GetResult());

    public static ServiceProvider BuildServices()
    {
        var key = new StoreSecretResolver()
            .ResolveAsync("Platform:Providers:OpenAI:ApiKey").GetAwaiter().GetResult()
            ?? throw new InvalidOperationException(
                "No OpenAI API key found (set OPENAI_API_KEY or run set-openai-key.ps1).");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = key
            })
            .Build();

        var services = new ServiceCollection();
        services.AddNexusPlatform(configuration);
        services.AddOpenAIModelProvider(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>Runs one real chat turn through RoutingModelGateway (no mocks) and persists
    /// the assistant message to a durable record. Returns the record and the recorded usage.</summary>
    public static async Task<TurnResult> SendTurnAsync(
        string prompt, string? modelId = null, CancellationToken ct = default)
    {
        using var services = BuildServices();

        var gateway = services.GetRequiredService<IModelGateway>();
        var invocation = new ModelInvocation
        {
            ModelId = modelId ?? DefaultModelId,
            Messages = [new ModelMessage { Role = ModelRole.User, Content = prompt }],
            Identity = new InvocationIdentity("nexus-dev", "smoke", Guid.NewGuid().ToString("N"), "smoke-user")
        };

        var result = await gateway.InvokeAsync(invocation, ct);

        var record = ChatRecord.From(result, prompt);
        ChatStore.Save(record);

        var meter = (InMemoryUsageMeter)services.GetRequiredService<IUsageMeter>();
        return new TurnResult(record, meter.Records.ToList());
    }

    /// <summary>Retrieves the persisted assistant message for a record written by a prior process.</summary>
    public static string? ReadAssistantMessage(string id) => ChatStore.Load(id)?.AssistantContent;
}

public sealed record TurnResult(ChatRecord Record, IReadOnlyCollection<UsageRecord> UsageRecords);
