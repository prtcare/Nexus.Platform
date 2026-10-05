using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Why a promotion attempt was refused by the build-once rules.
///
/// <para>
/// These are the three rules Task 3 requires a proof of, each as a typed refusal:
/// environment cannot change application bytes; changed bytes mean a new build; and a promotion may not
/// replace the artifact behind an existing release id.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BuildOnceRefusalReason
{
    None = 0,

    /// <summary>An environment label reached the build identity, so two environments would be two builds.</summary>
    EnvironmentInBuildIdentity,

    /// <summary>The promotion supplies its own artifact instead of naming a published one. A promotion moves bytes; it does not produce them.</summary>
    ArtifactSuppliedRatherThanReferenced,

    /// <summary>The bytes presented for an existing release id differ from the certified digest. This is a new build, not a promotion.</summary>
    DigestChangedForExistingReleaseId,

    /// <summary>The attempt asks for a rebuild to reach the target environment.</summary>
    EnvironmentSpecificRebuildRequested,

    /// <summary>The artifact is not certified, so it is not yet eligible to be promoted at all.</summary>
    ArtifactNotCertified,

    /// <summary>The certified build id does not match the artifact's own build id.</summary>
    CertifiedBuildMismatch
}

/// <summary>
/// One attempt to move an artifact towards an environment, as the build-once rules see it.
///
/// <para>
/// The shape of this record is itself the defence. A promotion names a <b>certified digest</b> and may
/// present the bytes it found; it cannot present bytes of its own choosing as the thing being promoted,
/// because <see cref="ArtifactSuppliedRatherThanReferenced"/> is what a run with no certified digest
/// produces. That is the difference between a promotion — which moves bytes someone else produced — and a
/// build, which produces them.
/// </para>
/// </summary>
public sealed record PromotionAttempt(
    BundleId ReleaseId,
    ArtifactId ArtifactId,
    BuildId CertifiedBuildId,
    ArtifactDigest? CertifiedDigest,
    ArtifactDigest? PresentedDigest,
    DeploymentEnvironmentId TargetEnvironment,
    bool RequestsRebuild,
    bool ArtifactIsCertified);

/// <summary>The verdict of the build-once rules.</summary>
public sealed record BuildOnceVerdict
{
    private BuildOnceVerdict(bool isPermitted, IReadOnlyList<BuildOnceRefusalReason> refusalReasons, IReadOnlyList<string> detail)
    {
        IsPermitted = isPermitted;
        RefusalReasons = refusalReasons;
        Detail = detail;
    }

    public bool IsPermitted { get; }

    public IReadOnlyList<BuildOnceRefusalReason> RefusalReasons { get; }

    public IReadOnlyList<string> Detail { get; }

    public bool RefusedBecause(BuildOnceRefusalReason reason) => RefusalReasons.Contains(reason);

    public static BuildOnceVerdict Permit() => new(true, [], ["Promotion may proceed."]);

    public static BuildOnceVerdict Refuse(params (BuildOnceRefusalReason Reason, string Detail)[] refusals)
    {
        if (refusals is null || refusals.Length == 0)
        {
            throw new ArgumentException("A refusal must name at least one typed reason.", nameof(refusals));
        }

        return new BuildOnceVerdict(
            false,
            [.. refusals.Select(r => r.Reason).Distinct().OrderBy(r => r)],
            [.. refusals.Select(r => r.Detail)]);
    }
}
