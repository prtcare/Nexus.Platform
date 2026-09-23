using System.Globalization;
using System.Text;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// A contract library this release pins, and the version it pins.
///
/// <para>
/// Carried because a service and the contract it was compiled against are versioned against each other:
/// promoted independently, an environment can hold a combination that never existed in a build. W9.1's
/// <see cref="ReleaseBundle.SharedContracts"/> carries the bytes; this carries the <i>name and version</i>,
/// which is what a consumer can actually read and reason about before deployment.
/// </para>
/// </summary>
public sealed record ContractVersion
{
    public ContractVersion(string contractName, string version, string? sourceRepository = null)
    {
        if (string.IsNullOrWhiteSpace(contractName))
        {
            throw new ArgumentException("A contract version must name the contract.", nameof(contractName));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A contract version must name the version.", nameof(version));
        }

        ContractName = contractName;
        Version = version;
        SourceRepository = sourceRepository;
    }

    public string ContractName { get; }

    public string Version { get; }

    public string? SourceRepository { get; }

    /// <summary>Canonical, digest-stable rendering.</summary>
    internal void AppendCanonicalForm(StringBuilder builder)
        => builder.Append(ContractName).Append('\0')
                  .Append(Version).Append('\0')
                  .Append(SourceRepository ?? string.Empty).Append('\0');
}

/// <summary>
/// What a release points at as the evidence for its own claims.
///
/// <para>
/// <b>References, not copies.</b> The release does not restate the build manifest, the test run or the
/// scan; it names them and records their verdicts. Copying them would create a second copy of a fact that
/// already has one home, and the two copies would eventually disagree — which is the drift this estate has
/// paid for repeatedly. A digest reference cannot drift silently: it either resolves to the same bytes or
/// it does not resolve.
/// </para>
///
/// <para>
/// The verdicts <i>are</i> restated, because they are what the certification gate must judge without
/// following a reference that may no longer be reachable. A release that recorded only a pointer would
/// make its own readiness depend on the availability of a file.
/// </para>
/// </summary>
public sealed record ReleaseEvidence
{
    public ReleaseEvidence(
        ArtifactDigest buildManifestDigest,
        string buildManifestReference,
        string testSuiteName,
        TestVerdict testVerdict,
        int testsTotal,
        int testsPassed,
        SecretScanVerdict secretScanVerdict,
        IReadOnlyList<string> secretScanSubjectLabels,
        ReproducibilityVerdict reproducibilityVerdict,
        int buildsCompared,
        string builderRunId)
    {
        ArgumentNullException.ThrowIfNull(buildManifestDigest);
        ArgumentNullException.ThrowIfNull(secretScanSubjectLabels);

        if (string.IsNullOrWhiteSpace(buildManifestReference))
        {
            throw new ArgumentException(
                "A release must name where its build manifest is, not only its digest. A digest with no location is an unretrievable claim.",
                nameof(buildManifestReference));
        }

        if (string.IsNullOrWhiteSpace(testSuiteName))
        {
            throw new ArgumentException("Release evidence must name the test suite.", nameof(testSuiteName));
        }

        if (string.IsNullOrWhiteSpace(builderRunId))
        {
            throw new ArgumentException("Release evidence must be attributable to a build run.", nameof(builderRunId));
        }

        if (secretScanSubjectLabels.Count == 0)
        {
            throw new ArgumentException(
                "Release evidence must name the subjects the scan covered. A scan that covered nothing has established nothing.",
                nameof(secretScanSubjectLabels));
        }

        BuildManifestDigest = buildManifestDigest;
        BuildManifestReference = buildManifestReference;
        TestSuiteName = testSuiteName;
        TestVerdict = testVerdict;
        TestsTotal = testsTotal;
        TestsPassed = testsPassed;
        SecretScanVerdict = secretScanVerdict;
        SecretScanSubjectLabels = [.. secretScanSubjectLabels.OrderBy(l => l, StringComparer.Ordinal)];
        ReproducibilityVerdict = reproducibilityVerdict;
        BuildsCompared = buildsCompared;
        BuilderRunId = builderRunId;
    }

    /// <summary>Digest of the build manifest this release was assembled from.</summary>
    public ArtifactDigest BuildManifestDigest { get; }

    /// <summary>Where that manifest lives. Evidence, not identity — two locations holding the same bytes describe the same build.</summary>
    public string BuildManifestReference { get; }

    public string TestSuiteName { get; }

    public TestVerdict TestVerdict { get; }

    public int TestsTotal { get; }

    public int TestsPassed { get; }

    public SecretScanVerdict SecretScanVerdict { get; }

    /// <summary>The subjects the scan actually covered, so a reader can tell coverage from a claim of it.</summary>
    public IReadOnlyList<string> SecretScanSubjectLabels { get; }

    public ReproducibilityVerdict ReproducibilityVerdict { get; }

    public int BuildsCompared { get; }

    public string BuilderRunId { get; }

    internal void AppendCanonicalForm(StringBuilder builder)
    {
        builder.Append(BuildManifestDigest).Append('\0')
               .Append(BuildManifestReference).Append('\0')
               .Append(TestSuiteName).Append('\0')
               .Append(TestVerdict).Append('\0')
               .Append(TestsTotal.ToString(CultureInfo.InvariantCulture)).Append('\0')
               .Append(TestsPassed.ToString(CultureInfo.InvariantCulture)).Append('\0')
               .Append(SecretScanVerdict).Append('\0')
               .Append(ReproducibilityVerdict).Append('\0')
               .Append(BuildsCompared.ToString(CultureInfo.InvariantCulture)).Append('\0')
               .Append(BuilderRunId).Append('\0');

        foreach (var label in SecretScanSubjectLabels)
        {
            builder.Append(label).Append('\0');
        }
    }
}

/// <summary>
/// The reference a release holds to the governed work it was performed for, for reverse navigation.
///
/// <para>
/// TASK 11 requires <c>ReleaseId → artifact → build → source commit → originating governed work</c> to be
/// walkable backwards. The middle hops live in the build manifest, which the release already points at.
/// This record carries the one hop nothing else can: <b>the governed work the release was performed for</b>.
/// Nothing in a build records why it ran, so the release is the first place the chain can be joined to a
/// piece of governed work at all.
/// </para>
///
/// <para>
/// <b>On the name.</b> This is <c>GovernedWorkReference</c> and not <c>WorkItem</c>: <c>WorkItem</c> is a
/// forbidden type name in any Platform assembly, because it is a Product-Core concept and Platform must not
/// be able to express Product-Core structure. The forbidden-name check matches the type name exactly, and
/// the intent is served by not introducing the concept — which this type does not.
/// </para>
/// </summary>
public sealed record GovernedWorkReference
{
    public GovernedWorkReference(string workReference, string? title = null, string? series = null)
    {
        if (!IsUsable(workReference))
        {
            throw new ArgumentException(
                "A governed work reference must be name-shaped, non-empty and at most 120 characters (e.g. 'WI-09-3.1').",
                nameof(workReference));
        }

        WorkReference = workReference;
        Title = title;
        Series = series;
    }

    /// <summary>
    /// The governed work's identifier — a roadmap id such as <c>WI-09-3.1</c>, an Owner decision id, or a
    /// work item number. Name-shaped, so a credential cannot be recorded here by accident.
    /// </summary>
    public string WorkReference { get; }

    public string? Title { get; }

    public string? Series { get; }

    private static bool IsUsable(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 120
           && CredentialShape.IsNameShaped(value);

    internal void AppendCanonicalForm(StringBuilder builder)
        => builder.Append(WorkReference).Append('\0')
                  .Append(Title ?? string.Empty).Append('\0')
                  .Append(Series ?? string.Empty).Append('\0');
}
