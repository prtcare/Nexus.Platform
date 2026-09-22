using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The repository-wide scan. W9.1's gate scans one repository; a release is built from several, so what is
/// tested here is the coverage and the classification, not the scanner — which already has its own
/// calibration set in <see cref="SecretScannerTests"/>.
/// </summary>
public sealed class BuildInputScannerTests : IDisposable
{
    private readonly string _root;

    public BuildInputScannerTests() => _root = Path.Combine(Path.GetTempPath(), "nexus-w92-scan-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Tree(string name, string fileName, string content)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
        return directory;
    }

    private static string CleanFile => "public class Thing { }\n";

    private static string LeakingFile => "api_key = \"" + TestData.RandomAlnum(32) + "\"\n";

    [Fact]
    public void AnActiveInputCarryingACredential_Blocks()
    {
        var subject = new ScanSubject("NEXUS/Platform", Tree("platform", "Program.cs", LeakingFile), ScanSubjectClass.ActiveBuildInput);

        var result = new BuildInputScanner().Scan(new BuildInputSet([subject]));

        Assert.Equal(SecretScanVerdict.Findings, result.Evidence.Verdict);
        Assert.False(result.Evidence.IsPassing);
        Assert.Single(result.Evidence.FindingLocations);
        Assert.Contains("NEXUS/Platform", result.Evidence.FindingLocations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ACleanActiveInput_Passes()
    {
        var subject = new ScanSubject("NEXUS/Platform", Tree("clean", "Program.cs", CleanFile), ScanSubjectClass.ActiveBuildInput);

        var result = new BuildInputScanner().Scan(new BuildInputSet([subject]));

        Assert.Equal(SecretScanVerdict.Clean, result.Evidence.Verdict);
        Assert.True(result.Evidence.IsPassing);
    }

    /// <summary>
    /// The separation Task 6 requires, asserted directly: a credential in superseded material that no build
    /// consumes is <b>recorded</b> and does not block. Skipping it would produce no record — which is the
    /// mistake W8F made when it concluded no committed secret existed — while blocking on it would make every
    /// build in the estate uncertifiable for a reason no build can fix.
    /// </summary>
    [Fact]
    public void AQuarantinedHistoricalInputCarryingACredential_IsRecordedAndDoesNotBlock()
    {
        var active = new ScanSubject("PRT/MarketSurvey", Tree("active", "Program.cs", CleanFile), ScanSubjectClass.ActiveBuildInput);
        var quarantined = new ScanSubject(
            "NEXUS/Forge (historical archive)",
            Tree("archived", "profile.ps1", LeakingFile),
            ScanSubjectClass.QuarantinedHistorical,
            "Superseded archival material; no build consumes it. Tracked separately as WH-1.");

        var result = new BuildInputScanner().Scan(new BuildInputSet([active, quarantined]));

        Assert.Equal(SecretScanVerdict.Clean, result.Evidence.Verdict);
        Assert.True(result.Evidence.IsPassing);

        // Recorded, in its own list, and NOT in the list that blocks.
        Assert.Empty(result.Evidence.FindingLocations);
        Assert.Single(result.Evidence.QuarantinedFindingLocations);
        Assert.Contains("NEXUS/Forge (historical archive)", result.Evidence.QuarantinedFindingLocations[0], StringComparison.Ordinal);

        // And the exclusion itself is visible in the record.
        Assert.Contains("NEXUS/Forge (historical archive)", result.Evidence.QuarantinedSubjectLabels);
    }

    [Fact]
    public void TheEvidenceRecordsWhichSubjectsWereCovered()
    {
        var result = new BuildInputScanner().Scan(new BuildInputSet([
            new ScanSubject("A", Tree("a", "f.cs", CleanFile), ScanSubjectClass.ActiveBuildInput),
            new ScanSubject("B", Tree("b", "f.cs", CleanFile), ScanSubjectClass.ActiveBuildInput)
        ]));

        Assert.Equal(["A", "B"], result.Evidence.ScannedSubjectLabels.OrderBy(l => l, StringComparer.Ordinal));
        Assert.Equal(2, result.Evidence.FilesScanned);
    }

    [Fact]
    public void AnUnreadableActiveSubject_IsIncomplete_NotClean()
    {
        var result = new BuildInputScanner().Scan(new BuildInputSet([
            new ScanSubject("missing", Path.Combine(_root, "not-here"), ScanSubjectClass.ActiveBuildInput)
        ]));

        Assert.Equal(SecretScanVerdict.Incomplete, result.Evidence.Verdict);
        Assert.False(result.Evidence.IsPassing);
    }

    /// <summary>A finding outranks an incomplete read: a known problem is more actionable than an unknown one.</summary>
    [Fact]
    public void FindingsOutrankIncomplete()
    {
        var result = new BuildInputScanner().Scan(new BuildInputSet([
            new ScanSubject("leaky", Tree("leaky", "f.cs", LeakingFile), ScanSubjectClass.ActiveBuildInput),
            new ScanSubject("missing", Path.Combine(_root, "not-here"), ScanSubjectClass.ActiveBuildInput)
        ]));

        Assert.Equal(SecretScanVerdict.Findings, result.Evidence.Verdict);
    }

    [Fact]
    public void ABuildInputSet_RefusesToBeEmpty()
    {
        // An empty set would scan nothing and report clean.
        Assert.Throws<ArgumentException>(() => new BuildInputSet([]));
    }

    [Fact]
    public void ABuildInputSet_RefusesDuplicateLabels()
    {
        Assert.Throws<ArgumentException>(() => new BuildInputSet([
            new ScanSubject("A", _root, ScanSubjectClass.ActiveBuildInput),
            new ScanSubject("A", _root, ScanSubjectClass.ActiveBuildInput)
        ]));
    }

    [Fact]
    public void AQuarantinedSubject_MustStateWhyItIsUnreachable()
    {
        Assert.Throws<ArgumentException>(() => new ScanSubject("X", _root, ScanSubjectClass.QuarantinedHistorical));
        Assert.Throws<ArgumentException>(() => new ScanSubject("X", _root, ScanSubjectClass.QuarantinedHistorical, "  "));
    }

    [Fact]
    public void TheScannerIsNotVacuouslyCleanOrBlocking()
    {
        var clean = new BuildInputScanner().Scan(new BuildInputSet([
            new ScanSubject("clean", Tree("c1", "f.cs", CleanFile), ScanSubjectClass.ActiveBuildInput)]));

        var leaking = new BuildInputScanner().Scan(new BuildInputSet([
            new ScanSubject("leaky", Tree("c2", "f.cs", LeakingFile), ScanSubjectClass.ActiveBuildInput)]));

        Assert.Equal(SecretScanVerdict.Clean, clean.Evidence.Verdict);
        Assert.Equal(SecretScanVerdict.Findings, leaking.Evidence.Verdict);
    }
}

/// <summary>A process runner that answers from a scripted map, so git behaviour can be tested without git.</summary>
internal sealed class ScriptedProcessRunner : IProcessRunner
{
    private readonly Func<string, IReadOnlyList<string>, ProcessResult> _respond;

    internal ScriptedProcessRunner(Func<string, IReadOnlyList<string>, ProcessResult> respond) => _respond = respond;

    internal List<string> Invocations { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        Invocations.Add($"{fileName} {string.Join(' ', arguments)}");
        return Task.FromResult(_respond(fileName, arguments));
    }
}

/// <summary>
/// The release-ref mechanism. Task 11 requires the mechanism to exist and forbids fabricating a release, so
/// the honest implementation refuses everything today — and these tests prove it refuses for the right,
/// distinct reasons rather than by being unimplemented.
/// </summary>
public sealed class ReleaseRefPolicyTests
{
    private static IReadOnlySet<string> Refs(params string[] refs) => refs.ToHashSet(StringComparer.Ordinal);

    private static GitReleaseRefPolicy Policy(Func<string, IReadOnlyList<string>, ProcessResult> respond, params string[] observed)
        => new(new ScriptedProcessRunner(respond), Refs(observed.Length > 0 ? observed : ["refs/heads/main"]));

    /// <summary>
    /// A responder that answers each git sub-command the way real git would. Keyed on the sub-command,
    /// because a responder that answers every command with the same result makes the for-each-ref call fail
    /// and the policy then reports "no release ref exists" for a repository that has one — which is how the
    /// first draft of these two tests passed for the wrong reason.
    /// </summary>
    private static Func<string, IReadOnlyList<string>, ProcessResult> GitResponder(string refs, string tip, bool containsCommit)
        => (_, args) => args[0] switch
        {
            "for-each-ref" => new ProcessResult(0, refs, string.Empty),
            "rev-parse" => new ProcessResult(0, tip + "\n", string.Empty),
            "merge-base" => containsCommit
                ? new ProcessResult(0, string.Empty, string.Empty)
                : new ProcessResult(1, string.Empty, "not an ancestor"),
            _ => new ProcessResult(1, string.Empty, "unexpected command")
        };

    [Fact]
    public async Task NoReleaseRef_RefusesWithNoReleaseRefExists()
    {
        var policy = Policy((_, _) => new ProcessResult(0, "refs/heads/main\nrefs/heads/w9.2/build-artifact\n", string.Empty));

        var assessment = await policy.AssessAsync("NEXUS/Platform", "D:\\NEXUS\\Platform", BuildTestData.CommitA);

        Assert.False(assessment.IsEligible);
        Assert.True(assessment.RefusedBecause(ReleaseRefRefusalReason.NoReleaseRefExists));
        Assert.Equal(ReleaseRefProtectionStatus.Absent, assessment.Descriptor!.ProtectionStatus);
    }

    /// <summary>
    /// The shortcut this refuses. Creating a branch called <c>release</c> and pointing it at the commit would
    /// turn a governance requirement into a naming convention, so a ref that did not exist when the stage
    /// began is refused as fabricated.
    /// </summary>
    [Fact]
    public async Task ARefCreatedForTheAssessment_IsRefusedAsFabricated()
    {
        // The ref exists now, but was not in the set observed before the assessment.
        var policy = Policy((_, _) => new ProcessResult(0, "refs/heads/main\nrefs/heads/release\n", string.Empty), "refs/heads/main");

        var assessment = await policy.AssessAsync("NEXUS/Platform", "D:\\NEXUS\\Platform", BuildTestData.CommitA);

        Assert.False(assessment.IsEligible);
        Assert.True(assessment.RefusedBecause(ReleaseRefRefusalReason.FabricatedRefNotPermitted));
        Assert.False(assessment.Descriptor!.ObservedBeforeCertification);
    }

    [Fact]
    public async Task AReleaseRefThatDoesNotContainTheBuildCommit_IsRefused()
    {
        var policy = Policy(
            GitResponder("refs/heads/main\nrefs/heads/release\n", BuildTestData.CommitB, containsCommit: false),
            "refs/heads/main",
            "refs/heads/release");

        var assessment = await policy.AssessAsync("NEXUS/Platform", "D:\\NEXUS\\Platform", BuildTestData.CommitA);

        Assert.False(assessment.IsEligible);
        Assert.True(assessment.RefusedBecause(ReleaseRefRefusalReason.RefDoesNotContainBuildCommit));
    }

    /// <summary>
    /// The case that matters most for the Owner's D2 ruling. A release ref exists and contains the commit —
    /// and the assessment still refuses, because protection was not observed. Server-side ruleset protection
    /// cannot be seen from a checkout, and this stage does not assume it.
    /// </summary>
    [Fact]
    public async Task AReleaseRefWithNoVerifiableProtection_IsRefusedRatherThanAssumed()
    {
        var policy = Policy(
            GitResponder("refs/heads/main\nrefs/heads/release\n", BuildTestData.CommitA, containsCommit: true),
            "refs/heads/main",
            "refs/heads/release");

        var assessment = await policy.AssessAsync("NEXUS/Platform", "D:\\NEXUS\\Platform", BuildTestData.CommitA);

        Assert.False(assessment.IsEligible);
        Assert.True(assessment.RefusedBecause(ReleaseRefRefusalReason.ServerSideProtectionUnverified));
        Assert.True(assessment.RefusedBecause(ReleaseRefRefusalReason.RefNotProtected));
        Assert.True(assessment.Descriptor!.ObservedBeforeCertification);
        Assert.Equal(ReleaseRefProtectionStatus.RequiresServerSideVerification, assessment.Descriptor.ProtectionStatus);
    }

    [Fact]
    public void APolicyWithoutThePreAssessmentRefSet_IsRefusedAtConstruction()
    {
        // Without it, a ref created to pass the check cannot be told from one that already existed — a
        // guard that cannot fail.
        Assert.Throws<ArgumentException>(() => new GitReleaseRefPolicy(new ScriptedProcessRunner((_, _) => new ProcessResult(0, string.Empty, string.Empty)), new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task ThePolicyIsNotVacuouslyRefusing()
    {
        // Two assessments with the same policy shape and different repository states produce *different*
        // reasons, which is what distinguishes an implemented mechanism from an unimplemented one.
        var absent = await Policy((_, _) => new ProcessResult(0, "refs/heads/main\n", string.Empty))
            .AssessAsync("R", _root, BuildTestData.CommitA);

        var fabricated = await Policy((_, _) => new ProcessResult(0, "refs/heads/main\nrefs/heads/release\n", string.Empty), "refs/heads/main")
            .AssessAsync("R", _root, BuildTestData.CommitA);

        Assert.NotEqual(absent.RefusalReasons, fabricated.RefusalReasons);
    }

    private static readonly string _root = Path.GetTempPath();
}
