namespace Nexus.Delivery.Contracts;

/// <summary>
/// A reference to a secret — <b>the name of where the value lives, never the value</b>.
///
/// <para>
/// This type is the mechanical form of a rule the estate already states in prose.
/// <c>CONFIGURATION_STANDARDS.md</c> §6: <i>"configuration holds a reference to a secret;
/// ISecretResolver holds the path to its value; neither holds the value"</i>, and <i>"any secret
/// value in any appsettings file is a defect, in every environment"</i>. W9.0 confirmed the
/// estate's exemplar already obeys it — the committed <c>ApiKeyRef</c> is the env-var
/// <b>name</b> <c>NEXUS_OPENAI_API_KEY</c>, resolved through <c>EnvironmentSecretResolver</c>.
/// </para>
///
/// <para>
/// Making it a type rather than a <c>string</c> means the rule is enforced where the record is
/// constructed, so a value cannot reach a manifest, a bundle or a lineage record by being passed as
/// a reference. Invariant I-7 (state machine) and L-1 (lineage) both reduce to this constructor.
/// </para>
/// </summary>
public sealed record SecretReference
{
    private SecretReference(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Builds a reference. Refuses a credential-shaped value outright — this is the one guard whose
    /// failure could not be undone, because a value written into an immutable record is permanent.
    /// </summary>
    public static SecretReference Parse(string value)
    {
        if (!TryParse(value, out var reference))
        {
            throw new ArgumentException(
                "A secret reference must be NAME-shaped (letters, digits, '_', '-', '.', '/', ':'; "
                + "no unbroken letter/digit run of 20 or more). It must never be the secret VALUE. "
                + "Example: 'NEXUS_PROVIDER_CREDENTIAL_REF'.",
                nameof(value));
        }

        return reference!;
    }

    public static bool TryParse(string? value, out SecretReference? reference)
    {
        reference = null;

        if (!CredentialShape.IsNameShaped(value))
        {
            return false;
        }

        reference = new SecretReference(value!);
        return true;
    }

    public override string ToString() => Value;
}
