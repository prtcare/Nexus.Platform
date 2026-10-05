using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// A process runner that stands in for a real build: it writes a file into the output directory and reports
/// success. The output directory is the command's <b>last argument</b>, which is the convention the
/// orchestrator's command factory is given.
/// </summary>
internal sealed class StagingBuildRunner : IProcessRunner
{
    private readonly Func<int, string> _contentForRound;
    private readonly bool _failEverything;
    private int _invocations;

    internal StagingBuildRunner(Func<int, string>? contentForRound = null, bool failEverything = false)
    {
        _contentForRound = contentForRound ?? (_ => "identical build output");
        _failEverything = failEverything;
    }

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var round = _invocations++;

        if (_failEverything)
        {
            return Task.FromResult(new ProcessResult(1, string.Empty, "the build failed"));
        }

        var outputDirectory = arguments[^1];
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "app.dll"), _contentForRound(round));

        return Task.FromResult(new ProcessResult(0, "build succeeded", string.Empty));
    }
}

/// <summary>
/// The orchestrator: the reproducibility comparison that produces an artifact's evidence, and the rule that a
/// build without one cannot claim to be reproducible.
/// </summary>
public sealed class BuildOrchestratorTests : IDisposable
{
    private readonly string _workingRoot;
    private readonly string _sourceRoot;

    public BuildOrchestratorTests()
    {
        _workingRoot = Path.Combine(Path.GetTempPath(), "nexus-w92-orch-" + Guid.NewGuid().ToString("N"));
        _sourceRoot = Path.Combine(_workingRoot, "source");
        Directory.CreateDirectory(_sourceRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingRoot))
        {
            Directory.Delete(_workingRoot, recursive: true);
        }
    }

    public static BuildOrchestrator Orchestrator(IProcessRunner runner)
        => new(runner, artifactType => artifactType == ArtifactType.Package
            ? new PreBuiltFilePackager()
            : new ZipDirectoryPackager(artifactType));

    public static BuildRequest Request(
        BuildIdentity? identity = null,
        string? workingRoot = null,
        string? sourceRoot = null,
        Func<int, string>? sourceRootForRound = null,
        int rounds = 2)
    {
        var id = identity ?? BuildTestData.Identity();
        var artifactId = ArtifactId.For(id.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.0");
        var artifact = BuildTestData.Artifact(id);

        return new BuildRequest(
            id,
            artifactId,
            sourceRoot ?? Path.GetTempPath(),
            staging => ["dotnet", "publish", "app.csproj", "-o", staging],
            workingRoot ?? Path.GetTempPath(),
            BuildTestData.GreenTests(),
            BuildTestData.CleanScan(),
            BuildTestData.Provenance,
            ["apiBaseUrl"],
            rounds)
        {
            SourceRootForRound = sourceRootForRound
        };
    }

    [Fact]
    public async Task TwoIdenticalBuilds_ProduceAByteIdenticalVerdict_AndACompleteManifest()
    {
        var outcome = await Orchestrator(new StagingBuildRunner())
            .BuildAsync(Request(workingRoot: _workingRoot, sourceRoot: _sourceRoot));

        Assert.True(outcome.BuildSucceeded);
        Assert.NotNull(outcome.Manifest);
        Assert.Equal(ReproducibilityVerdict.ByteIdentical, outcome.Reproducibility.Verdict);
        Assert.Equal(2, outcome.Reproducibility.BuildsCompared);
        Assert.True(outcome.Reproducibility.IsProven);
        Assert.Single(outcome.Reproducibility.ComparisonDigests.Distinct());
        Assert.NotNull(outcome.ArtifactDigest);
        Assert.Empty(outcome.Manifest!.MissingRequiredElements());
    }

    /// <summary>
    /// The divergent case, and the reason the comparison records every digest rather than only the verdict: a
    /// build that differs must be reportable, not averaged or ignored.
    /// </summary>
    [Fact]
    public async Task TwoDivergentBuilds_ProduceADivergentVerdict()
    {
        var outcome = await Orchestrator(new StagingBuildRunner(round => $"output for round {round}"))
            .BuildAsync(Request(workingRoot: _workingRoot, sourceRoot: _sourceRoot));

        Assert.True(outcome.BuildSucceeded);
        Assert.Equal(ReproducibilityVerdict.Divergent, outcome.Reproducibility.Verdict);
        Assert.False(outcome.Reproducibility.IsProven);
        Assert.Equal(2, outcome.Reproducibility.ComparisonDigests.Distinct().Count());
    }

    [Fact]
    public async Task AFailedBuild_ProducesNoReproducibilityClaim()
    {
        var outcome = await Orchestrator(new StagingBuildRunner(failEverything: true))
            .BuildAsync(Request(workingRoot: _workingRoot, sourceRoot: _sourceRoot));

        Assert.False(outcome.BuildSucceeded);
        Assert.Null(outcome.Manifest);
        Assert.Equal(ReproducibilityVerdict.NotPerformed, outcome.Reproducibility.Verdict);
        Assert.Contains(outcome.Log, line => line.Contains("build failed", StringComparison.Ordinal));
    }

    /// <summary>
    /// The rule that makes a "reproducible" claim mean something: one build agreeing with itself is not a
    /// comparison, and the request refuses to pretend otherwise.
    /// </summary>
    [Fact]
    public void ABuildRequest_RequiresAtLeastTwoRounds()
    {
        Assert.Throws<ArgumentException>(() => Request(workingRoot: _workingRoot, sourceRoot: _sourceRoot, rounds: 1));
    }

    /// <summary>
    /// Path independence. Each round builds from a different absolute source root, and the artifact digest
    /// must still match — the property that matters when an artifact is built on one machine and verified on
    /// another. Two builds at the same path can agree by accident of incremental state; two at different
    /// paths cannot.
    /// </summary>
    [Fact]
    public async Task RoundsFromDifferentSourceRoots_ProduceAnIdenticalArtifact()
    {
        var first = Path.Combine(_workingRoot, "copy-a");
        var second = Path.Combine(_workingRoot, "copy-b-with-a-different-length");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        var outcome = await Orchestrator(new StagingBuildRunner())
            .BuildAsync(Request(
                workingRoot: _workingRoot,
                sourceRoot: first,
                sourceRootForRound: round => round == 0 ? first : second));

        Assert.Equal(ReproducibilityVerdict.ByteIdentical, outcome.Reproducibility.Verdict);
        Assert.Contains(outcome.Log, line => line.Contains("copy-b", StringComparison.Ordinal));
    }

    /// <summary>
    /// Round isolation. A stale file left in a staging directory from a previous round would be packaged and
    /// compared against a fresh one, and the comparison would report agreement that means nothing.
    /// </summary>
    [Fact]
    public async Task EachRoundStartsFromAFreshOutputDirectory()
    {
        var stale = Path.Combine(_workingRoot, "round-1", "staging", "stale.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "left over from a previous run");

        var outcome = await Orchestrator(new StagingBuildRunner())
            .BuildAsync(Request(workingRoot: _workingRoot, sourceRoot: _sourceRoot));

        // Identical verdict despite the stale file: it did not survive into the round's output.
        Assert.Equal(ReproducibilityVerdict.ByteIdentical, outcome.Reproducibility.Verdict);
        Assert.False(File.Exists(stale));
    }

    /// <summary>
    /// End to end through the gate: a real orchestrator run with green evidence produces an artifact the
    /// certification gate will certify — so the two halves are wired to each other, not merely both present.
    /// </summary>
    [Fact]
    public async Task AnOrchestratedBuild_Certifies_WithGreenEvidence()
    {
        var outcome = await Orchestrator(new StagingBuildRunner())
            .BuildAsync(Request(workingRoot: _workingRoot, sourceRoot: _sourceRoot));

        var decision = CertificationGate.Evaluate(new CertificationEvidence(
            outcome.Manifest!,
            [.. outcome.Manifest!.SecretScan.ScannedSubjectLabels],
            unitRequiresEnvironmentSpecificRebuild: false,
            buildSucceeded: outcome.BuildSucceeded));

        Assert.True(decision.IsCertified);
        Assert.Equal(outcome.Manifest.BuildId, decision.BuildId);
    }

    [Fact]
    public async Task AnOrchestratedBuild_ThatIsNotReproducible_IsRefusedByTheGate()
    {
        var outcome = await Orchestrator(new StagingBuildRunner(round => $"output for round {round}"))
            .BuildAsync(Request(workingRoot: _workingRoot, sourceRoot: _sourceRoot));

        var decision = CertificationGate.Evaluate(new CertificationEvidence(
            outcome.Manifest!,
            [.. outcome.Manifest!.SecretScan.ScannedSubjectLabels],
            unitRequiresEnvironmentSpecificRebuild: false,
            buildSucceeded: true));

        Assert.False(decision.IsCertified);
        Assert.True(decision.RefusedBecause(CertificationRefusalReason.ReproducibilityNotProven));
    }
}
