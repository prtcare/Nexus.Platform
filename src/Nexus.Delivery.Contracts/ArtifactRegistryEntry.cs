using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>Why a registry refused a write.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegistryRefusalReason
{
    None = 0,

    /// <summary>The bundle id is already present and the registry is write-once. A changed artifact is a NEW bundle, never an overwrite.</summary>
    BundleIdAlreadyExists,

    /// <summary>A digest in the manifest does not match the artifact presented.</summary>
    DigestMismatch,

    /// <summary>The requested store path escapes the registry root.</summary>
    PathEscapesRegistryRoot,

    /// <summary>The write failed at the storage layer.</summary>
    StorageFailure,

    /// <summary>A bundle with this id exists and is quarantined; it may not be replaced while the quarantine holds.</summary>
    BundleIsQuarantined
}

/// <summary>
/// A stored bundle's entry: its identity and where it is, never its contents.
///
/// <para>
/// The entry carries <see cref="ManifestDigest"/> so that a promoter can verify it received the
/// manifest the registry stored, without re-reading the store's private layout. That is the assertion
/// the promotion proof rests on.
/// </para>
/// </summary>
public sealed record ArtifactRegistryEntry(
    BundleId BundleId,
    ArtifactDigest ManifestDigest,
    DateTimeOffset RegisteredAt,
    IReadOnlyList<ReleaseArtifact> Artifacts,
    bool IsQuarantined = false,
    string? QuarantineReason = null);

/// <summary>The outcome of a register attempt. Refusal is an ordinary result, not an exception.</summary>
public sealed record RegistrationOutcome
{
    private RegistrationOutcome(bool isAccepted, ArtifactRegistryEntry? entry, RegistryRefusalReason refusalReason, string? detail)
    {
        IsAccepted = isAccepted;
        Entry = entry;
        RefusalReason = refusalReason;
        Detail = detail;
    }

    public bool IsAccepted { get; }

    public ArtifactRegistryEntry? Entry { get; }

    public RegistryRefusalReason RefusalReason { get; }

    /// <summary>Operator-facing detail. Must never contain a secret value.</summary>
    public string? Detail { get; }

    public static RegistrationOutcome Accepted(ArtifactRegistryEntry entry)
        => new(true, entry, RegistryRefusalReason.None, null);

    public static RegistrationOutcome Refused(RegistryRefusalReason reason, string detail)
        => new(false, null, reason, detail);
}
