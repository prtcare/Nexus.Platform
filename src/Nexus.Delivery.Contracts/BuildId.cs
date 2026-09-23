namespace Nexus.Delivery.Contracts;

/// <summary>
/// Identity of a build. <b>Derived, never assigned.</b>
///
/// <para>
/// A build id that a pipeline invents — a timestamp, a counter, a run number — is an identity that
/// changes when nothing else does, and that is the property which breaks the one rule this stage exists
/// to establish: two builds of identical governed inputs must be the same build. This type can only be
/// constructed from a digest of those inputs, so there is no way to give the same inputs two identities
/// or to give different inputs one.
/// </para>
///
/// <para>
/// The value is short enough to read in a log and carries a full digest alongside it, so no information
/// is lost to the abbreviation. <c>bld-</c> + 16 lowercase hex characters = 64 bits of the input digest,
/// with the full digest available on the identity for equality and evidence.
/// </para>
/// </summary>
public sealed record BuildId
{
    /// <summary>Characters from the input digest used in the readable form. 64 bits — collision-safe for the lifetime of any estate.</summary>
    public const int ShortFormHexLength = 16;

    public const string Prefix = "bld-";

    private BuildId(string value, ArtifactDigest? inputDigest)
    {
        Value = value;
        InputDigest = inputDigest;
    }

    /// <summary>The readable form, e.g. <c>bld-3f9a1c07e5b24d18</c>.</summary>
    public string Value { get; }

    /// <summary>
    /// The full digest of the governed inputs, when this id was derived from them. <b>Null when the id was
    /// parsed from its wire form</b>, because a reader holds the abbreviation and not the inputs.
    ///
    /// <para>
    /// This member was originally non-nullable and <c>Parse</c> fabricated a digest by padding the short
    /// form with zeros — which meant a parsed id never compared equal to the id it came from, and a store
    /// lookup that round-tripped an id through disk returned an entry whose <c>BuildId</c> silently differed
    /// from the one that was published. That is the defect a nullable member and value equality fix: an
    /// absent digest is now absent rather than invented.
    /// </para>
    /// </summary>
    public ArtifactDigest? InputDigest { get; }

    /// <summary>Derives the build id from the canonical digest of a build's governed inputs.</summary>
    public static BuildId FromInputDigest(ArtifactDigest inputDigest)
    {
        ArgumentNullException.ThrowIfNull(inputDigest);

        return new BuildId(Prefix + inputDigest.Hex[..ShortFormHexLength], inputDigest);
    }

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.StartsWith(Prefix, StringComparison.Ordinal)
           && value.Length == Prefix.Length + ShortFormHexLength
           && value[Prefix.Length..].All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'));

    /// <summary>
    /// Reads an id from its wire form. The result equals the id it was derived from, because
    /// <see cref="BuildId"/>'s equality is by <see cref="Value"/> — the readable form is the identity, and
    /// the input digest is derivation detail that a reader may not have.
    /// </summary>
    public static BuildId Parse(string value)
        => IsValid(value)
            ? new BuildId(value, null)
            : throw new ArgumentException(
                $"Not a build id (expected '{Prefix}' + {ShortFormHexLength} lowercase hex characters, e.g. '{Prefix}3f9a1c07e5b24d18').",
                nameof(value));

    /// <summary>Equality is by the readable form, so an id survives a round trip through a store or a manifest.</summary>
    public bool Equals(BuildId? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;
}
