using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Why a client's runtime configuration was refused.
///
/// <para>
/// The owner of these reasons is the <b>client, at startup</b>. Task 4 requires that a missing or invalid
/// runtime configuration produce a typed startup/config failure — not a blank page, not a request to
/// <c>undefined/api</c>, and not a silent fallback to whatever the artifact was built with. A fallback is
/// the specific failure this replaces: MarketSurvey's shipped bundle contains
/// <c>http://127.0.0.1:5080</c> as a fallback endpoint, which means a misconfigured production deploy
/// would not fail — it would try to reach the build machine's loopback address.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuntimeConfigurationRefusalReason
{
    None = 0,

    /// <summary>No runtime configuration was found at all.</summary>
    ConfigurationMissing,

    /// <summary>The configuration is not parseable.</summary>
    ConfigurationMalformed,

    /// <summary>The schema version is absent, unreadable, or not one this artifact understands.</summary>
    SchemaVersionUnsupported,

    /// <summary>A required configuration key is absent.</summary>
    RequiredKeyMissing,

    /// <summary>A configured value is blank or otherwise unusable.</summary>
    ValueInvalid,

    /// <summary>
    /// A value is shaped like a credential. A client bundle is public by construction — every byte in it
    /// can be read by any user — so this is refused rather than warned about.
    /// </summary>
    SecretValuePresent,

    /// <summary>A production environment was given a non-TLS endpoint.</summary>
    InsecureEndpointInProduction,

    /// <summary>The environment label is not one of the ratified identifiers.</summary>
    EnvironmentNotRatified
}

/// <summary>
/// The configuration a built client artifact reads at run time, from outside itself.
///
/// <para>
/// <b>The whole point is that this is not in the bundle.</b> W9.0 found the Experience client's
/// <c>VITE_*</c> values inlined at build time and MarketSurvey's <c>API_INTERNAL_URL</c> resolved into
/// <c>.next/routes-manifest.json</c> — both baked, both meaning a per-environment rebuild. This record is
/// the thing that replaces them: supplied beside the artifact, versioned, validated, and never something
/// the artifact had to be built knowing.
/// </para>
///
/// <para>
/// <b>It cannot hold a secret.</b> Not by convention — every value is validated by
/// <see cref="CredentialShape"/> on construction, and a credential-shaped value is refused with
/// <see cref="RuntimeConfigurationRefusalReason.SecretValuePresent"/>. This is the security half of the
/// runtime-configuration pattern and it is the half that is easy to get wrong: moving configuration out
/// of the build is only safe if what moves out is also safe to publish, and everything a browser receives
/// is published.
/// </para>
/// </summary>
public sealed record ClientRuntimeConfiguration
{
    /// <summary>The schema version this contract defines. Bumped when the shape changes.</summary>
    public const int CurrentSchemaVersion = 1;

    public const int MinSchemaVersion = 1;

    public const int MaxSchemaVersion = 1;

    public ClientRuntimeConfiguration(
        int schemaVersion,
        string environmentLabel,
        IReadOnlyDictionary<string, string> endpoints,
        IReadOnlyDictionary<string, string>? publicSettings = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        SchemaVersion = schemaVersion;
        EnvironmentLabel = environmentLabel ?? string.Empty;
        Endpoints = new Dictionary<string, string>(endpoints, StringComparer.Ordinal);

        // Public settings are validated the same way as endpoints. A client bundle is public, so there is
        // no such thing as a private value here — only values that are safe to publish and values that
        // are refused.
        PublicSettings = publicSettings is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(publicSettings, StringComparer.Ordinal);
    }

    public int SchemaVersion { get; }

    /// <summary>Which environment this configuration describes — e.g. <c>ENV-DEV</c>. Supplied, never inferred.</summary>
    public string EnvironmentLabel { get; }

    /// <summary>Service endpoints by logical name, e.g. <c>api</c> → <c>https://api.test.example</c>.</summary>
    public IReadOnlyDictionary<string, string> Endpoints { get; }

    /// <summary>Non-secret, publicly-readable settings.</summary>
    public IReadOnlyDictionary<string, string> PublicSettings { get; }

    /// <summary>Every value-shaped key in the configuration, for the validator to refuse.</summary>
    public IEnumerable<KeyValuePair<string, string>> AllValues => Endpoints.Concat(PublicSettings);
}

/// <summary>The outcome of validating a runtime configuration.</summary>
public sealed record RuntimeConfigurationValidation
{
    private RuntimeConfigurationValidation(
        ClientRuntimeConfiguration? configuration,
        IReadOnlyList<RuntimeConfigurationRefusalReason> refusalReasons,
        IReadOnlyList<string> detail)
    {
        Configuration = configuration;
        RefusalReasons = refusalReasons;
        Detail = detail;
    }

    public ClientRuntimeConfiguration? Configuration { get; }

    public IReadOnlyList<RuntimeConfigurationRefusalReason> RefusalReasons { get; }

    /// <summary>One line per refusal. Names the key, never prints a value.</summary>
    public IReadOnlyList<string> Detail { get; }

    public bool IsValid => RefusalReasons.Count == 0;

    public bool RefusedBecause(RuntimeConfigurationRefusalReason reason) => RefusalReasons.Contains(reason);

    public static RuntimeConfigurationValidation Valid(ClientRuntimeConfiguration configuration)
        => new(configuration, [], ["Runtime configuration valid."]);

    public static RuntimeConfigurationValidation Invalid(
        IReadOnlyList<RuntimeConfigurationRefusalReason> reasons,
        IReadOnlyList<string> detail)
    {
        if (reasons is null || reasons.Count == 0)
        {
            throw new ArgumentException("An invalid result must name at least one typed reason.", nameof(reasons));
        }

        return new RuntimeConfigurationValidation(null, [.. reasons.Distinct().OrderBy(r => r)], detail ?? []);
    }
}

/// <summary>
/// Where a client's runtime configuration comes from. Supplied beside the artifact — a static file on the
/// same origin, or an injected document — never compiled into it.
///
/// <para>
/// This is an interface so the client-side loading policy (how many times to retry, whether to cache)
/// stays out of the contract, and so the validator can be tested without a browser.
/// </para>
/// </summary>
public interface IClientRuntimeConfigurationSource
{
    /// <summary>Returns the raw configuration text, or null when nothing is available.</summary>
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);
}
