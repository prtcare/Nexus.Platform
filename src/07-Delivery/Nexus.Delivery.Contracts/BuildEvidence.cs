using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// The verdict of the tests a build ran.
///
/// <para>
/// <b><see cref="NoTests"/> and <see cref="NotRun"/> are separate states from <see cref="Passed"/> and
/// from each other, and both block certification.</b> The distinction is not pedantry — this estate has
/// recorded it three times. W8F found a Platform test project that referenced xUnit, contained zero test
/// methods and exited 0 with no summary; and a suite that could not build and was therefore never
/// executed. Reporting either as a pass is precisely the failure the estate keeps re-finding, so the
/// vocabulary here makes "there was nothing to run" and "it did not run" say what they are.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestVerdict
{
    /// <summary>Tests ran and all passed.</summary>
    Passed,

    /// <summary>Tests ran and at least one failed.</summary>
    Failed,

    /// <summary>The suite exists but was not executed. Never a pass.</summary>
    NotRun,

    /// <summary>The suite was discovered and contains no tests. Never a pass.</summary>
    NoTests
}

/// <summary>What tests a build ran, and how they ended.</summary>
public sealed record BuildTestEvidence
{
    public BuildTestEvidence(string suiteName, TestVerdict verdict, int total, int passed, int failed, int skipped)
    {
        if (string.IsNullOrWhiteSpace(suiteName))
        {
            throw new ArgumentException("Test evidence must name the suite.", nameof(suiteName));
        }

        if (total < 0 || passed < 0 || failed < 0 || skipped < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "Test counts cannot be negative.");
        }

        if (passed + failed + skipped > total)
        {
            throw new ArgumentException("Test counts exceed the total.", nameof(passed));
        }

        // A verdict that contradicts its own counts is a record that cannot be trusted either way.
        if (verdict == TestVerdict.Passed && failed > 0)
        {
            throw new ArgumentException("A passing suite cannot contain failures.", nameof(verdict));
        }

        if (verdict == TestVerdict.Failed && failed == 0)
        {
            throw new ArgumentException("A failing suite must record at least one failure.", nameof(verdict));
        }

        if (verdict == TestVerdict.NoTests && total != 0)
        {
            throw new ArgumentException("A NO_TESTS verdict must record zero tests.", nameof(verdict));
        }

        if (verdict == TestVerdict.NotRun && total != 0)
        {
            throw new ArgumentException("A NOT_RUN verdict must record no results.", nameof(verdict));
        }

        SuiteName = suiteName;
        Verdict = verdict;
        Total = total;
        Passed = passed;
        Failed = failed;
        Skipped = skipped;
    }

    public string SuiteName { get; }

    public TestVerdict Verdict { get; }

    public int Total { get; }

    public int Passed { get; }

    public int Failed { get; }

    public int Skipped { get; }

    /// <summary>True only for a suite that actually ran and actually passed.</summary>
    public bool IsGreen => Verdict == TestVerdict.Passed;
}

/// <summary>Whether two builds of identical governed inputs produced identical bytes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReproducibilityVerdict
{
    /// <summary>Every artifact's digest matched across repeated builds.</summary>
    ByteIdentical,

    /// <summary>At least one artifact differed. The build is not reproducible and cannot be certified.</summary>
    Divergent,

    /// <summary>The comparison was not performed. Never treated as reproducible.</summary>
    NotPerformed
}

/// <summary>
/// The reproducibility result, with the evidence that produced it.
///
/// <para>
/// <see cref="ComparisonDigests"/> carries one digest per independent build, so the claim is checkable
/// against the artifacts rather than taken on trust. W8 established that this estate can produce
/// byte-identical builds, and W8E established how easily that property is lost to build ORDER rather than
/// to anything about the source — which is why the comparison records how many builds were compared and
/// what each produced, not merely that they agreed.
/// </para>
/// </summary>
public sealed record ReproducibilityEvidence
{
    public ReproducibilityEvidence(
        ReproducibilityVerdict verdict,
        int buildsCompared,
        IReadOnlyList<ArtifactDigest> comparisonDigests,
        string? note = null)
    {
        if (buildsCompared < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(buildsCompared), buildsCompared, "The number of builds compared cannot be negative.");
        }

        if (verdict == ReproducibilityVerdict.ByteIdentical && buildsCompared < 2)
        {
            throw new ArgumentException(
                "A byte-identical verdict requires at least two builds to compare. One build agreeing with itself is not reproducibility.",
                nameof(buildsCompared));
        }

        if (verdict == ReproducibilityVerdict.ByteIdentical && comparisonDigests.Distinct().Count() > 1)
        {
            throw new ArgumentException(
                "A byte-identical verdict cannot carry differing digests.",
                nameof(comparisonDigests));
        }

        Verdict = verdict;
        BuildsCompared = buildsCompared;
        ComparisonDigests = [.. comparisonDigests];
        Note = note;
    }

    public ReproducibilityVerdict Verdict { get; }

    /// <summary>How many independent builds were compared. A one-build comparison is not a comparison.</summary>
    public int BuildsCompared { get; }

    /// <summary>One digest per build compared, for the artifact under test.</summary>
    public IReadOnlyList<ArtifactDigest> ComparisonDigests { get; }

    public string? Note { get; }

    public bool IsProven => Verdict == ReproducibilityVerdict.ByteIdentical && BuildsCompared >= 2;

    public static ReproducibilityEvidence NotPerformed(string note)
        => new(ReproducibilityVerdict.NotPerformed, 0, [], note);
}

/// <summary>
/// Who built it, and on what. Provenance is what makes a manifest attributable rather than merely descriptive.
/// </summary>
public sealed record ProvenanceRecord
{
    public ProvenanceRecord(string builderRunId, string builderImageId, string hostOperatingSystem, string? builtBy = null)
    {
        if (string.IsNullOrWhiteSpace(builderRunId))
        {
            throw new ArgumentException("A provenance record must be attributable to a run.", nameof(builderRunId));
        }

        if (string.IsNullOrWhiteSpace(hostOperatingSystem))
        {
            throw new ArgumentException("A provenance record must name the host operating system.", nameof(hostOperatingSystem));
        }

        BuilderRunId = builderRunId;
        BuilderImageId = builderImageId;
        HostOperatingSystem = hostOperatingSystem;
        BuiltBy = string.IsNullOrWhiteSpace(builtBy) ? null : builtBy;
    }

    /// <summary>The pipeline run, or the local run identifier for a proof build.</summary>
    public string BuilderRunId { get; }

    /// <summary>
    /// Identifies the build definition instance that ran — for a containerised build, the image digest;
    /// for a local build, a descriptor of the toolchain invocation. Never a credential.
    /// </summary>
    public string BuilderImageId { get; }

    public string HostOperatingSystem { get; }

    /// <summary>Optional actor identity. Name-shaped; a credential-shaped value is refused.</summary>
    public string? BuiltBy { get; }
}
