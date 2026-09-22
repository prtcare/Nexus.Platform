using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Why an artifact was refused certification. Typed, on the same principle as W9.1's
/// <see cref="DeploymentRefusalReason"/>: a refusal that cannot name itself cannot be distinguished from
/// a bug, and a gate whose reasons are free text drifts into prose the moment two people write them.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CertificationRefusalReason
{
    None = 0,

    /// <summary>No governed source revision is recorded, so nothing ties the artifact to a commit.</summary>
    GovernedSourceLineageMissing,

    /// <summary>A source working tree had uncommitted changes: the commit does not describe the bytes.</summary>
    SourceWorkingTreeIsDirty,

    /// <summary>The build itself did not succeed.</summary>
    BuildFailed,

    /// <summary>Tests ran and failed. Distinct from the two below because the remedy differs.</summary>
    TestsFailed,

    /// <summary>Tests exist but were not executed. Never a pass.</summary>
    TestsNotRun,

    /// <summary>A suite was discovered with no tests in it. Never a pass.</summary>
    TestsAbsent,

    /// <summary>The secret scan found something in an active build input.</summary>
    SecretScanFindingsInActiveInput,

    /// <summary>The secret scan could not complete, so the input set has not been established as clean.</summary>
    SecretScanIncomplete,

    /// <summary>The secret scan did not cover the whole build input set.</summary>
    SecretScanIncompleteCoverage,

    /// <summary>An artifact carries no content hash.</summary>
    ArtifactHashMissing,

    /// <summary>The manifest is missing one or more required elements.</summary>
    ManifestIncomplete,

    /// <summary>Repeated builds did not produce identical bytes, or the comparison was not performed.</summary>
    ReproducibilityNotProven,

    /// <summary>
    /// The unit still requires an environment-specific rebuild — a value that varies by environment is
    /// compiled in rather than supplied at run time.
    /// </summary>
    EnvironmentSpecificRebuildDependency,

    /// <summary>The artifact's bytes were found to differ by environment.</summary>
    ArtifactNotEnvironmentNeutral,

    /// <summary>A build id could not be derived from the recorded inputs.</summary>
    BuildIdentityIndeterminate
}

/// <summary>The certification verdict. There is exactly one passing value.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CertificationVerdict
{
    /// <summary>The artifact may become input to a Release Bundle.</summary>
    ArtifactCertified,

    /// <summary>Refused, with typed reasons. Ordinary and recorded.</summary>
    Refused
}

/// <summary>The outcome of asking the certification gate.</summary>
public sealed record CertificationDecision
{
    private CertificationDecision(
        CertificationVerdict verdict,
        BuildId? buildId,
        IReadOnlyList<CertificationRefusalReason> refusalReasons,
        IReadOnlyList<string> detail)
    {
        Verdict = verdict;
        BuildId = buildId;
        RefusalReasons = refusalReasons;
        Detail = detail;
    }

    public CertificationVerdict Verdict { get; }

    public BuildId? BuildId { get; }

    public IReadOnlyList<CertificationRefusalReason> RefusalReasons { get; }

    /// <summary>Human-readable detail, one line per reason. Must never contain a secret value.</summary>
    public IReadOnlyList<string> Detail { get; }

    public bool IsCertified => Verdict == CertificationVerdict.ArtifactCertified;

    public bool RefusedBecause(CertificationRefusalReason reason) => RefusalReasons.Contains(reason);

    public static CertificationDecision Certified(BuildId buildId)
        => new(CertificationVerdict.ArtifactCertified, buildId, [], ["ARTIFACT_CERTIFIED"]);

    public static CertificationDecision Refuse(
        BuildId? buildId,
        IReadOnlyList<CertificationRefusalReason> reasons,
        IReadOnlyList<string> detail)
    {
        if (reasons is null || reasons.Count == 0)
        {
            throw new ArgumentException(
                "A refusal must name at least one typed reason. An unnamed refusal is indistinguishable from a bug.",
                nameof(reasons));
        }

        return new CertificationDecision(
            CertificationVerdict.Refused,
            buildId,
            [.. reasons.Distinct().OrderBy(r => r)],
            detail ?? []);
    }

    public override string ToString() => IsCertified
        ? $"ARTIFACT_CERTIFIED {BuildId}"
        : $"REFUSED [{string.Join(", ", RefusalReasons)}]";
}
