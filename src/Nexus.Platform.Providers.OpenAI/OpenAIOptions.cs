namespace Nexus.Platform.Providers.OpenAI;

/// <summary>
/// Configuration for the OpenAI provider. W5E: this type no longer holds a credential.
///
/// BEFORE (W4L-201's stated blocking reason, verbatim):
///     "REQUIRES_HUMAN_SECRET_ROTATION - OpenAIOptions carries an ApiKey property and the
///      provider is the credential consumer (D-14, Owner decision #7)"
/// The whole of that block rested on the <c>ApiKey</c> property below, which held the
/// secret VALUE as a plain bound string. That is also the anti-pattern
/// docs/CONFIGURATION_STANDARDS.md forbids: "Credentials are resolved, not passed."
///
/// AFTER: the property is <see cref="ApiKeyRef"/> and it holds a secret REFERENCE NAME,
/// resolved at the point of use through the neutral Nexus.Platform.Contracts.Secrets
/// .ISecretResolver contract. The reference is committed; the value never is. This is the
/// naming convention CONFIGURATION_STANDARDS.md already records as approved -- "a secret
/// reference key ends in Ref -- ApiKeyRef, SigningKeyRef, ConnectionStringRef".
///
/// The reference NAME is opaque to this assembly. Deciding WHICH reference this provider
/// uses is AI provider configuration, which V3 rule 3 places in the AI Head -- not here.
/// </summary>
public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    /// <summary>
    /// The NAME of a secret reference -- never a secret value. Bound from configuration
    /// key <c>OpenAI:ApiKeyRef</c> and resolved through <c>ISecretResolver</c>.
    /// </summary>
    public string ApiKeyRef { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4.1";
}
