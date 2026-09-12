using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Platform.Contracts.Core;
using Nexus.Platform.Contracts.Models;
using Nexus.Platform.Contracts.Secrets;
using Nexus.Platform.Core;
using Nexus.Platform.Core.Models;
using Nexus.Platform.Providers.OpenAI;

namespace Nexus.Platform.SmokeHost;

/// <summary>
/// Composes the platform the way a consuming host would - AddNexusPlatform +
/// AddOpenAIModelProvider, with the API key obtained through the neutral
/// ISecretResolver boundary at the point of use - then runs a real chat turn through
/// the routing gateway.
///
/// W5E: before the secret-boundary extraction this class resolved the key EAGERLY and
/// wrote the VALUE into IConfiguration as ["OpenAI:ApiKey"]. That is the credential-in-
/// configuration anti-pattern docs/CONFIGURATION_STANDARDS.md forbids. It now publishes
/// only the reference NAME, and binds the store-backed resolver at the composition root.
/// </summary>
public static class SmokeRunner
{
    public const string DefaultModelId = "openai:gpt-4.1-mini";

    /// <summary>
    /// The secret REFERENCE NAME for the OpenAI key. This is an opaque locator, not a
    /// value: in this host's resolver namespace it is the dotted key inside the
    /// set-openai-key.ps1 store, and in <c>EnvironmentSecretResolver</c>'s namespace it
    /// would be an environment-variable name. The contract is neutral about which.
    /// </summary>
    public const string SecretReference = "Platform:Providers:OpenAI:ApiKey";

    public static bool KeyAvailable()
        => !string.IsNullOrWhiteSpace(
            new StoreSecretResolver().ResolveAsync(SecretReference).GetAwaiter().GetResult());

    public static ServiceProvider BuildServices()
    {
        if (!KeyAvailable())
        {
            throw new InvalidOperationException(
                "No OpenAI API key found (set OPENAI_API_KEY or run set-openai-key.ps1).");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // The REFERENCE, never the value.
                ["OpenAI:ApiKeyRef"] = SecretReference
            })
            .Build();

        var services = new ServiceCollection();
        services.AddNexusPlatform(configuration);

        // Composition-root binding -- the documented IQuotaPolicy/PermissiveQuotaPolicy
        // pattern: the port lives in neutral Nexus.Platform.Contracts, the host chooses
        // the implementation. StoreSecretResolver is the credential-adjacent adapter
        // (it reads the local user-secrets store), which is exactly why it is bound HERE,
        // in the sample host, and deliberately NOT in neutral CORE.
        services.AddSingleton<ISecretResolver>(new StoreSecretResolver());

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
