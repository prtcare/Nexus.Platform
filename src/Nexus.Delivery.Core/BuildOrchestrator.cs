using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>What to build, and how.</summary>
public sealed record BuildRequest
{
    public BuildRequest(
        BuildIdentity identity,
        ArtifactId artifactId,
        string sourceRoot,
        Func<string, IReadOnlyList<string>> buildCommandForOutputDirectory,
        string workingRoot,
        BuildTestEvidence tests,
        SecretScanEvidence secretScan,
        ProvenanceRecord provenance,
        IReadOnlyList<string>? runtimeConfigurationKeys = null,
        int reproducibilityRounds = 2)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(artifactId);
        ArgumentNullException.ThrowIfNull(buildCommandForOutputDirectory);
        ArgumentNullException.ThrowIfNull(tests);
        ArgumentNullException.ThrowIfNull(secretScan);
        ArgumentNullException.ThrowIfNull(provenance);

        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new ArgumentException("A build request must name the source root to build from.", nameof(sourceRoot));
        }

        if (string.IsNullOrWhiteSpace(workingRoot))
        {
            throw new ArgumentException("A build request must name a working root for staging and outputs.", nameof(workingRoot));
        }

        if (reproducibilityRounds < 2)
        {
            throw new ArgumentException(
                "At least two builds are required. One build agreeing with itself is not a reproducibility proof, and recording it as one is the false-green this stage exists to prevent.",
                nameof(reproducibilityRounds));
        }

        Identity = identity;
        ArtifactId = artifactId;
        SourceRoot = sourceRoot;
        BuildCommandForOutputDirectory = buildCommandForOutputDirectory;
        WorkingRoot = workingRoot;
        Tests = tests;
        SecretScan = secretScan;
        Provenance = provenance;
        RuntimeConfigurationKeys = [.. runtimeConfigurationKeys ?? []];
        ReproducibilityRounds = reproducibilityRounds;
    }

    public BuildIdentity Identity { get; }

    public ArtifactId ArtifactId { get; }

    /// <summary>The directory the build command runs in. For a path-independence proof this differs per round.</summary>
    public string SourceRoot { get; }

    /// <summary>
    /// Builds the command line for one round, given the directory the output must land in. A function
    /// rather than a fixed list because the output directory is chosen by the orchestrator, and because a
    /// command that could not name its own output would be writing somewhere ambient.
    /// </summary>
    public Func<string, IReadOnlyList<string>> BuildCommandForOutputDirectory { get; }

    public string WorkingRoot { get; }

    public BuildTestEvidence Tests { get; }

    public SecretScanEvidence SecretScan { get; }

    public ProvenanceRecord Provenance { get; }

    public IReadOnlyList<string> RuntimeConfigurationKeys { get; }

    /// <summary>Independent build rounds. Two by default; more is a stronger claim.</summary>
    public int ReproducibilityRounds { get; }

    /// <summary>Source root for a given round. Overridden by the caller for a path-independence proof.</summary>
    public Func<int, string>? SourceRootForRound { get; init; }
}

/// <summary>The result of a build run.</summary>
public sealed record BuildOutcome(
    bool BuildSucceeded,
    BuildManifest? Manifest,
    ReproducibilityEvidence Reproducibility,
    ArtifactDigest? ArtifactDigest,
    IReadOnlyList<string> Log)
{
    public static BuildOutcome Failed(ReproducibilityEvidence reproducibility, IReadOnlyList<string> log)
        => new(false, null, reproducibility, null, log);
}

/// <summary>
/// Builds an artifact, then builds it again and compares — because a reproducibility claim that was never
/// measured is a claim, not a proof.
///
/// <para>
/// <b>Why two rounds are structural rather than optional.</b> W8 established that this estate can produce
/// byte-identical builds, and W8E established how easily that property is lost — to build ORDER, of all
/// things, when the shared component was compiled into whichever tree built last. So the comparison is not
/// a check bolted onto the end; it is what produces the artifact's <see cref="ReproducibilityEvidence"/>,
/// and a build without it cannot be certified. There is no code path here that produces a manifest
/// claiming <c>ByteIdentical</c> from one build.
/// </para>
///
/// <para>
/// <b>Rounds can build from different source roots.</b> <see cref="BuildRequest.SourceRootForRound"/>
/// lets a caller point round 2 at a second copy of the source at a different absolute path, which turns
/// the comparison into a path-independence proof — the stronger property, and the one that matters when
/// the artifact was built on one machine and will be verified on another. Two builds at the same path can
/// agree by accident of incremental state; two builds at different paths cannot.
/// </para>
/// </summary>
public sealed class BuildOrchestrator
{
    private readonly IProcessRunner _runner;
    private readonly Func<ArtifactType, IArtifactPackager> _packagerFor;

    public BuildOrchestrator(IProcessRunner runner, Func<ArtifactType, IArtifactPackager> packagerFor)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _packagerFor = packagerFor ?? throw new ArgumentNullException(nameof(packagerFor));
    }

    public async Task<BuildOutcome> BuildAsync(BuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var log = new List<string>();
        var digests = new List<ArtifactDigest>();
        PackagedArtifact? firstArtifact = null;
        var allRoundsSucceeded = true;

        var packager = _packagerFor(request.ArtifactId.Type);

        for (var round = 0; round < request.ReproducibilityRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var roundRoot = Path.Combine(request.WorkingRoot, $"round-{round}");
            var staging = Path.Combine(roundRoot, "staging");
            var artifacts = Path.Combine(roundRoot, "artifacts");

            // A fresh output directory per round. Anything left over from a previous round would let a
            // stale file be compared against a fresh one and counted as agreement.
            if (Directory.Exists(roundRoot))
            {
                Directory.Delete(roundRoot, recursive: true);
            }

            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(artifacts);

            var sourceRoot = request.SourceRootForRound?.Invoke(round) ?? request.SourceRoot;
            log.Add($"round {round}: source={sourceRoot}");

            var command = request.BuildCommandForOutputDirectory(staging);
            log.Add($"round {round}: {string.Join(' ', command)}");

            var build = await _runner.RunAsync(command[0], [.. command.Skip(1)], sourceRoot, cancellationToken)
                .ConfigureAwait(false);

            log.Add($"round {round}: exit={build.ExitCode}");

            if (!build.Succeeded)
            {
                allRoundsSucceeded = false;
                log.Add($"round {round}: build failed — {FirstLines(build.StandardError, 12)}");
                break;
            }

            var packaged = await packager.PackageAsync(
                new PackageRequest(request.ArtifactId, request.Identity.BuildId, staging, artifacts),
                cancellationToken).ConfigureAwait(false);

            if (!packaged.IsPackaged || packaged.Artifact is null)
            {
                allRoundsSucceeded = false;
                log.Add($"round {round}: packaging refused — {packaged.RefusalReason}");
                break;
            }

            log.Add($"round {round}: {packaged.Artifact.FileName} {packaged.Artifact.ContentDigest} ({packaged.Artifact.SizeBytes} bytes)");

            digests.Add(packaged.Artifact.ContentDigest);
            firstArtifact ??= packaged.Artifact;
        }

        if (!allRoundsSucceeded || firstArtifact is null)
        {
            return BuildOutcome.Failed(
                ReproducibilityEvidence.NotPerformed("The build did not complete in every round, so no comparison was possible."),
                log);
        }

        var identical = digests.Distinct().Count() == 1;

        var reproducibility = new ReproducibilityEvidence(
            identical ? ReproducibilityVerdict.ByteIdentical : ReproducibilityVerdict.Divergent,
            digests.Count,
            digests,
            identical
                ? null
                : "Repeated builds of identical governed inputs produced different bytes. Report every digest; do not average or ignore.");

        log.Add($"reproducibility: {reproducibility.Verdict} across {reproducibility.BuildsCompared} build(s)");

        var manifest = new BuildManifest(
            BuildManifest.CurrentSchemaVersion,
            request.Identity,
            [firstArtifact],
            request.Tests,
            request.SecretScan,
            reproducibility,
            request.Provenance,
            DateTimeOffset.UtcNow,
            request.RuntimeConfigurationKeys);

        return new BuildOutcome(true, manifest, reproducibility, firstArtifact.ContentDigest, log);
    }

    private static string FirstLines(string text, int count)
        => string.Join(" / ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(count));
}
