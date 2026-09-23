namespace Nexus.Delivery.Contracts;

/// <summary>
/// Identity of a release. <b>Derived, never assigned</b> — the same discipline as <see cref="BuildId"/>,
/// for the same reason.
///
/// <para>
/// A release id a pipeline invents from a counter, a date or a run number changes when nothing about the
/// release changed, and stays the same when everything did. Either property alone is enough to make it
/// useless as a promotion key: W9.1's promotion proof is a digest comparison across environments, and a
/// release whose id is not a function of its contents cannot support that comparison.
/// </para>
///
/// <para>
/// <b>What the digest is over, and why that is the whole design.</b> The input is
/// <see cref="ReleaseIdentity.CanonicalForm"/> — unit, version, build id, release reference name, source
/// commits and the immutable artifact identities. <b>No environment appears in it, and none can</b>,
/// because no environment appears in the identity. That is what makes "the same ReleaseId is promoted
/// through environments" true by construction rather than by discipline: promotion changes the
/// environment, and the environment is not an input to this value, so promotion cannot change it.
/// </para>
///
/// <para>
/// <c>rel-</c> + 16 lowercase hex characters = 64 bits of that digest, with the full digest carried
/// alongside for equality and evidence. The abbreviation is for reading in a log; the digest is what a
/// record is compared on.
/// </para>
/// </summary>
public sealed record ReleaseId
{
    /// <summary>Characters from the input digest used in the readable form. Matches <see cref="BuildId.ShortFormHexLength"/> so the two ids read alike.</summary>
    public const int ShortFormHexLength = 16;

    public const string Prefix = "rel-";

    private ReleaseId(string value, ArtifactDigest? inputDigest)
    {
        Value = value;
        InputDigest = inputDigest;
    }

    /// <summary>The readable form, e.g. <c>rel-4c81be0a2fd97e35</c>.</summary>
    public string Value { get; }

    /// <summary>
    /// The full digest of the release identity, when this id was derived from it. <b>Null when the id was
    /// parsed from its wire form</b> — a reader holds the abbreviation and not the inputs, and inventing a
    /// digest from the abbreviation would make a parsed id compare unequal to the id it came from. Same
    /// defect and same remedy as <see cref="BuildId.InputDigest"/>.
    /// </summary>
    public ArtifactDigest? InputDigest { get; }

    /// <summary>Derives the release id from the canonical digest of a release's identity.</summary>
    public static ReleaseId FromInputDigest(ArtifactDigest inputDigest)
    {
        ArgumentNullException.ThrowIfNull(inputDigest);
        return new ReleaseId(Prefix + inputDigest.Hex[..ShortFormHexLength], inputDigest);
    }

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.StartsWith(Prefix, StringComparison.Ordinal)
           && value.Length == Prefix.Length + ShortFormHexLength
           && value[Prefix.Length..].All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'));

    /// <summary>
    /// Reads an id from its wire form. The result equals the id it was derived from, because equality is
    /// by <see cref="Value"/> — the readable form is the identity.
    /// </summary>
    public static ReleaseId Parse(string value)
        => IsValid(value)
            ? new ReleaseId(value, null)
            : throw new ArgumentException(
                $"Not a release id (expected '{Prefix}' + {ShortFormHexLength} lowercase hex characters, e.g. '{Prefix}4c81be0a2fd97e35').",
                nameof(value));

    /// <summary>Equality is by the readable form, so an id survives a round trip through a registry or a bundle.</summary>
    public bool Equals(ReleaseId? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;
}
