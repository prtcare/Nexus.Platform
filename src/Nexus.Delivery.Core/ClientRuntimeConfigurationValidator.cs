using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// A typed startup/configuration failure. Thrown when a client cannot obtain a usable runtime
/// configuration — the requirement that a missing or invalid configuration fails loudly instead of
/// producing a page that quietly talks to the wrong place.
/// </summary>
public sealed class RuntimeConfigurationException : Exception
{
    public RuntimeConfigurationException(RuntimeConfigurationValidation validation)
        : base("The runtime configuration is unusable: " + string.Join(" | ", validation.Detail))
    {
        Validation = validation;
    }

    public RuntimeConfigurationValidation Validation { get; }

    public IReadOnlyList<RuntimeConfigurationRefusalReason> RefusalReasons => Validation.RefusalReasons;
}

/// <summary>
/// Validates a client's runtime configuration.
///
/// <para>
/// <b>The secret check is the one that makes this pattern safe rather than merely convenient.</b> Moving
/// configuration out of the build is only an improvement if what moves out is safe to publish, and a
/// client bundle is public by construction — <c>CONFIGURATION_STANDARDS.md</c> §5.1 states it exactly:
/// every frontend variable "is inlined into JavaScript that any user can read … there is no such thing as
/// a private frontend configuration value". A runtime configuration document is fetched by that same
/// browser, so it inherits the property. Any value shaped like a credential is therefore refused outright
/// rather than warned about.
/// </para>
///
/// <para>
/// <b>No fallbacks.</b> There is no default endpoint, and a missing value is a refusal rather than an
/// empty string. The shipped MarketSurvey bundle carries <c>http://127.0.0.1:5080</c> as its fallback
/// endpoint, which means a misconfigured deployment does not fail — it tries to reach the build machine's
/// loopback address, and the symptom is a timeout rather than a configuration error. That is the failure
/// mode this replaces.
/// </para>
/// </summary>
public sealed class ClientRuntimeConfigurationValidator
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IReadOnlyList<string> _requiredEndpointKeys;
    private readonly bool _production;

    /// <param name="requiredEndpointKeys">
    /// The endpoint names the client cannot start without. Required and non-empty: a validator told to
    /// require nothing would pass every configuration, including one with no endpoints at all.
    /// </param>
    /// <param name="production">
    /// Whether the target environment is production. Used only for the TLS rule — endpoints in development
    /// and test may legitimately be plain HTTP on a private address.
    /// </param>
    public ClientRuntimeConfigurationValidator(IReadOnlyList<string> requiredEndpointKeys, bool production)
    {
        ArgumentNullException.ThrowIfNull(requiredEndpointKeys);

        if (requiredEndpointKeys.Count == 0)
        {
            throw new ArgumentException(
                "A validator with no required endpoints would accept any configuration, including an empty one.",
                nameof(requiredEndpointKeys));
        }

        _requiredEndpointKeys = [.. requiredEndpointKeys];
        _production = production;
    }

    /// <summary>
    /// Parses and validates the raw text. Null or blank text is a typed <c>ConfigurationMissing</c>
    /// refusal, not an exception — absence is an expected condition that the caller reports.
    /// </summary>
    public RuntimeConfigurationValidation Parse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return RuntimeConfigurationValidation.Invalid(
                [RuntimeConfigurationRefusalReason.ConfigurationMissing],
                ["No runtime configuration was found beside the artifact."]);
        }

        RuntimeConfigurationDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RuntimeConfigurationDto>(rawText, Options);
        }
        catch (JsonException ex)
        {
            return RuntimeConfigurationValidation.Invalid(
                [RuntimeConfigurationRefusalReason.ConfigurationMalformed],
                [$"The runtime configuration is not valid JSON: {ex.Message}"]);
        }

        if (dto is null)
        {
            return RuntimeConfigurationValidation.Invalid(
                [RuntimeConfigurationRefusalReason.ConfigurationMalformed],
                ["The runtime configuration parsed to nothing."]);
        }

        var configuration = new ClientRuntimeConfiguration(
            dto.SchemaVersion,
            dto.Environment ?? string.Empty,
            dto.Endpoints ?? new Dictionary<string, string>(),
            dto.PublicSettings);

        return Validate(configuration);
    }

    /// <summary>Reads from a source and validates. Never throws for a *missing* source; throws the typed exception only on request.</summary>
    public async Task<RuntimeConfigurationValidation> LoadAsync(
        IClientRuntimeConfigurationSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? raw;
        try
        {
            raw = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            return RuntimeConfigurationValidation.Invalid(
                [RuntimeConfigurationRefusalReason.ConfigurationMissing],
                [$"The runtime configuration could not be read: {ex.GetType().Name}."]);
        }

        return Parse(raw);
    }

    /// <summary>Loads and throws the typed failure when the configuration is unusable.</summary>
    public async Task<ClientRuntimeConfiguration> LoadOrFailAsync(
        IClientRuntimeConfigurationSource source,
        CancellationToken cancellationToken = default)
    {
        var validation = await LoadAsync(source, cancellationToken).ConfigureAwait(false);

        return validation.IsValid
            ? validation.Configuration!
            : throw new RuntimeConfigurationException(validation);
    }

    /// <summary>Validates an already-parsed configuration.</summary>
    public RuntimeConfigurationValidation Validate(ClientRuntimeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var reasons = new List<RuntimeConfigurationRefusalReason>();
        var detail = new List<string>();

        if (configuration.SchemaVersion < ClientRuntimeConfiguration.MinSchemaVersion
            || configuration.SchemaVersion > ClientRuntimeConfiguration.MaxSchemaVersion)
        {
            reasons.Add(RuntimeConfigurationRefusalReason.SchemaVersionUnsupported);
            detail.Add(
                $"Schema version {configuration.SchemaVersion} is not understood "
                + $"(this contract accepts {ClientRuntimeConfiguration.MinSchemaVersion}–{ClientRuntimeConfiguration.MaxSchemaVersion}). "
                + "A configuration written for a different contract version must not be guessed at.");
        }

        if (!DeploymentEnvironmentId.TryParse(configuration.EnvironmentLabel, out _))
        {
            reasons.Add(RuntimeConfigurationRefusalReason.EnvironmentNotRatified);
            detail.Add($"'{configuration.EnvironmentLabel}' is not a ratified environment identifier.");
        }

        foreach (var key in _requiredEndpointKeys)
        {
            if (!configuration.Endpoints.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                reasons.Add(RuntimeConfigurationRefusalReason.RequiredKeyMissing);
                detail.Add($"The required endpoint '{key}' is absent.");
            }
        }

        foreach (var pair in configuration.AllValues)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                reasons.Add(RuntimeConfigurationRefusalReason.ValueInvalid);
                detail.Add($"'{pair.Key}' is blank.");
                continue;
            }

            // The security check. A client bundle is public, so a credential-shaped value here is not a
            // warning about a bad practice — it is a credential being published to every user.
            if (CredentialShape.LooksLikeCredentialValue(pair.Value)
                || ConnectionStringCarriesCredentialLikeValue(pair.Value))
            {
                reasons.Add(RuntimeConfigurationRefusalReason.SecretValuePresent);
                detail.Add($"'{pair.Key}' carries a credential-shaped value. It is refused, and its value is not reproduced here.");
            }
        }

        if (_production)
        {
            foreach (var pair in configuration.Endpoints)
            {
                if (pair.Value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add(RuntimeConfigurationRefusalReason.InsecureEndpointInProduction);
                    detail.Add($"Endpoint '{pair.Key}' is plain HTTP in a production environment.");
                }
            }
        }

        return reasons.Count == 0
            ? RuntimeConfigurationValidation.Valid(configuration)
            : RuntimeConfigurationValidation.Invalid([.. reasons], detail);
    }

    /// <summary>
    /// A value carrying an embedded credential, such as a connection string or a URL with userinfo. The
    /// shape test alone would miss <c>https://user:pass@host</c>, which is a credential inside a value
    /// that otherwise reads as an endpoint.
    /// </summary>
    private static bool ConnectionStringCarriesCredentialLikeValue(string value)
    {
        var at = value.IndexOf('@');
        if (at > 0)
        {
            var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd >= 0 && at > schemeEnd + 3 && value[(schemeEnd + 3)..at].Contains(':'))
            {
                return true;
            }
        }

        var marker = value.IndexOf("Password=", StringComparison.OrdinalIgnoreCase);
        return marker >= 0;
    }

    private sealed record RuntimeConfigurationDto(
        [property: JsonPropertyOrder(0)] int SchemaVersion,
        [property: JsonPropertyOrder(1)] string? Environment,
        [property: JsonPropertyOrder(2)] Dictionary<string, string>? Endpoints,
        [property: JsonPropertyOrder(3)] Dictionary<string, string>? PublicSettings);
}
