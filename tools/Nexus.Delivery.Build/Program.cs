using System.Text;
using System.Text.Json;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;

namespace Nexus.Delivery.Build;

/// <summary>
/// The W9.2 build-and-certify driver.
///
/// <para>
/// Does one thing: takes a plan, and performs the whole governed sequence — derive the build identity from
/// the repository, scan the build's input set for secrets, build the artifact TWICE from two different paths
/// and compare, package it, write the canonical manifest, publish it to a local immutable store, and run the
/// certification gate and the release-ref assessment.
/// </para>
///
/// <para>
/// It deploys nothing and publishes nothing external. Publication to a shared registry is blocked pending
/// the Owner's credential-rotation record, and this tool has no code path that could do it — the only store
/// it knows is the local adapter it is given a root for.
/// </para>
///
/// <para>
/// Public rather than internal so that this assembly can be named in Platform's boundary suites, which are
/// documented as exhaustive over the Platform assemblies that ship. A tool that is part of the solution and
/// not named there is a hole in the neutrality checks — and this one reads a repository's files, so it is
/// among the last components that should be trusted on its own word.
/// </para>
/// </summary>
public static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: Nexus.Delivery.Build <plan.json>");
            return 2;
        }

        var plan = BuildPlan.Load(args[0]);
        var evidence = Directory.CreateDirectory(plan.EvidenceDirectory).FullName;
        var transcript = new StringBuilder();
        void Say(string line)
        {
            Console.WriteLine(line);
            transcript.AppendLine(line);
        }

        Say("=== NEXUS W9.2 BUILD AND CERTIFY ===");
        Say($"plan: {Path.GetFileName(args[0])}");
        Say($"unit: {plan.UnitId}   artifact: {plan.ArtifactName}@{plan.ArtifactVersion}");

        var runner = new ProcessRunner();
        var scanner = new BuildInputScanner();
        var store = new FileArtifactStore(plan.StoreRoot);

        // ---- 1. Governed source -------------------------------------------------------------------
        var commitResult = await runner.RunAsync("git", ["rev-parse", "HEAD"], plan.RepositoryRoot);
        if (!commitResult.Succeeded)
        {
            Say($"FAILED: could not read the commit of '{plan.RepositoryLabel}'.");
            return 1;
        }

        var commit = commitResult.StandardOutput.Trim();
        var statusResult = await runner.RunAsync("git", ["status", "--porcelain"], plan.RepositoryRoot);
        var isDirty = statusResult.StandardOutput.Trim().Length > 0;

        Say("");
        Say("--- governed source");
        Say($"  {plan.RepositoryLabel} @ {commit}  dirty={isDirty}");

        // ---- 1b. Source-root provenance, BEFORE the first compiler runs ---------------------------
        // W9.5 built this same source twice from two different absolute paths and got Divergent, because the
        // roots were `git archive` extractions with no Git metadata - so the SDK could not map source paths to
        // a stable prefix, and absolute paths reached the PDB. The refusal it produced was "Reproducibility is
        // Divergent", a statement about the OUTPUT, which sends a reader to look at the product. The condition
        // that actually failed is a statement about the INPUT and is checkable in advance.
        //
        // Checked here, before round 0, so a root that cannot carry provenance never reaches a compiler. A
        // refusal after the first round would mean bytes had already been produced from a source whose
        // identity was never established.
        var provenanceVerifier = new SourceRootProvenanceVerifier(runner);
        var admittedRoots = new List<string>(plan.SourceRoots.Length);

        foreach (var root in plan.SourceRoots)
        {
            var verdict = await provenanceVerifier.VerifyAsync(root, plan.RepositoryRoot, commit);

            if (!verdict.IsAdmitted)
            {
                Say($"  build root: {root}");
                Say($"    REFUSED [{verdict.RefusalReason.ToString().ToUpperInvariant()}]");
                Say($"    {verdict.Detail}");
                Say("");
                // The reason is the verdict's, not a constant. The first version of this line printed
                // SOURCE_PROVENANCE_UNVERIFIABLE whatever had actually failed, so a run refused for a commit
                // mismatch announced itself as a provenance absence - the same class of mistake as the digest
                // divergence this whole check exists to replace.
                Say($"RESULT: NOT_CERTIFIED [{verdict.RefusalReason}] - '{(verdict.IsGitWorkTree ? verdict.Commit ?? "(unknown)" : "no work tree")}'");

                return 1;
            }

            Say($"  build root: {root}   provenance OK (git work tree at {verdict.Commit}, clean, resolvable)");
            admittedRoots.Add(root);
        }

        // ---- 2. Toolchain and dependency lock state -----------------------------------------------
        var sdkResult = await runner.RunAsync("dotnet", ["--version"], plan.RepositoryRoot);
        var sdk = sdkResult.StandardOutput.Trim();
        var runtime = Environment.Version.ToString();

        var lockFiles = Directory
            .EnumerateFiles(plan.RepositoryRoot, "packages.lock.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !f.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
            .ToList();

        DependencyLockState dependencyLock;
        if (lockFiles.Count > 0)
        {
            var digests = lockFiles.Select(f =>
            {
                using var stream = File.OpenRead(f);
                return new LockFileDigest(plan.RepositoryLabel, Path.GetRelativePath(plan.RepositoryRoot, f).Replace('\\', '/'), ArtifactDigest.Compute(stream));
            }).ToList();

            dependencyLock = DependencyLockState.Locked(digests);
        }
        else
        {
            // Recorded as UNLOCKED, not as locked. W9.0 measured that this estate's .NET repositories restore
            // from floating PackageReference versions with no packages.lock.json anywhere, so the honest
            // record is that the .NET dependency graph is not pinned — and a reproducibility claim about this
            // build must be read with that in mind. An npm lock file exists in the web project and does not
            // pin the .NET restore, so its presence is noted rather than counted.
            var npmLock = File.Exists(Path.Combine(plan.RepositoryRoot, "apps", "web", "package-lock.json"));
            dependencyLock = DependencyLockState.Unlocked(
                "Searched for packages.lock.json recursively (excluding bin, obj, node_modules): none found. "
                + (npmLock ? "An npm package-lock.json exists for the web project and does not pin the .NET restore. " : string.Empty)
                + "The .NET dependency graph is therefore not pinned by a lock file.");
        }

        Say("");
        Say("--- toolchain and dependencies");
        Say($"  sdk={sdk} runtime={runtime} os={System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
        Say($"  dependency lock: {dependencyLock}");

        var identity = new BuildIdentity(
            DeploymentUnitId.Parse(plan.UnitId),
            [new SourceRevision(plan.RepositoryLabel, commit, isDirty)],
            plan.BuildDefinitionVersion,
            dependencyLock,
            new ToolchainIdentity(sdk, runtime, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier),
            plan.BuildConfiguration);

        Say($"  build id: {identity.BuildId}  (input digest {identity.InputDigest})");

        var identityViolations = BuildOnceGuard.InspectIdentity(identity);
        Say($"  build-once identity inspection: {(identityViolations.Count == 0 ? "clean" : string.Join("; ", identityViolations.Select(v => v.Detail)))}");

        // ---- 3. Repository-wide secret scan -------------------------------------------------------
        var inputSet = new BuildInputSet([.. plan.ScanSubjects.Select(s => s.ToSubject())]);
        var scanResult = scanner.Scan(inputSet);

        Say("");
        Say("--- secret scan over the build input set");
        foreach (var line in scanResult.Detail)
        {
            Say($"  {line}");
        }

        Say($"  verdict={scanResult.Evidence.Verdict} scanned={scanResult.Evidence.FilesScanned} skipped={scanResult.Evidence.FilesSkipped} "
            + $"activeFindings={scanResult.Evidence.FindingLocations.Count} quarantinedFindings={scanResult.Evidence.QuarantinedFindingLocations.Count}");

        // ---- 4. Tests -----------------------------------------------------------------------------
        var tests = await RunTestsAsync(runner, plan, Say);

        // ---- 5. Build twice, from two paths, and compare -------------------------------------------
        var artifactId = ArtifactId.For(DeploymentUnitId.Parse(plan.UnitId), plan.ResolveArtifactType(), plan.ArtifactName, plan.ArtifactVersion);

        var orchestrator = new BuildOrchestrator(runner, artifactType => artifactType == ArtifactType.Package
            ? new PreBuiltFilePackager()
            : new ZipDirectoryPackager(artifactType));

        var request = new BuildRequest(
            identity,
            artifactId,
            plan.SourceRoots[0],
            staging => ["dotnet", "publish", plan.ProjectPath, "-c", plan.BuildConfiguration, "-o", staging, "--nologo"],
            plan.WorkingRoot,
            tests,
            scanResult.Evidence,
            new ProvenanceRecord($"w9.2-proof-{commit[..8]}", $"local:{sdk}", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "w9.2"),
            plan.RuntimeConfigurationKeys,
            plan.SourceRoots.Length)
        {
            SourceRootForRound = round => plan.SourceRoots[Math.Min(round, plan.SourceRoots.Length - 1)]
        };

        Say("");
        Say("--- build");
        var outcome = await orchestrator.BuildAsync(request);
        foreach (var line in outcome.Log)
        {
            Say($"  {line}");
        }

        if (!outcome.BuildSucceeded || outcome.Manifest is null)
        {
            Say("FAILED: the build did not complete.");
            await File.WriteAllTextAsync(Path.Combine(evidence, "BUILD_FAILED.txt"), transcript.ToString());
            return 1;
        }

        var manifest = outcome.Manifest;
        var artifact = manifest.Artifacts[0];
        var packagedPath = Path.Combine(plan.WorkingRoot, "round-0", "artifacts", artifact.FileName);

        // ---- 6. Environment neutrality of the artifact -----------------------------------------------
        // Measured, not declared. Whether an environment was compiled in is invisible in the artifact — a
        // baked endpoint looks like any other value — so the plan's claim is allowed to ADD suspicion and is
        // never allowed to clear a finding this inspection produces.
        var neutrality = EnvironmentNeutralityInspector.Inspect(packagedPath, plan.ForbiddenArtifactMarkers);
        var requiresEnvironmentRebuild = plan.UnitRequiresEnvironmentSpecificRebuild || !neutrality.IsNeutral;

        Say("");
        Say("--- environment neutrality of the artifact (measured)");
        Say($"  inspected {neutrality.FilesInspected} file(s), skipped {neutrality.SkippedFiles.Count} as binary/irrelevant");
        foreach (var finding in neutrality.Findings)
        {
            Say($"  FINDING: {finding}");
        }

        Say($"  neutral: {neutrality.IsNeutral}");
        Say($"  plan declares an environment rebuild dependency: {plan.UnitRequiresEnvironmentSpecificRebuild}");
        Say($"  effective: requiresEnvironmentRebuild={requiresEnvironmentRebuild} "
            + "(a measured finding forces this; the plan cannot clear one)");

        // ---- 6. The canonical manifest --------------------------------------------------------------
        var canonical = BuildManifestCodec.Encode(manifest);
        var manifestPath = Path.Combine(evidence, "BUILD_MANIFEST.txt");
        await File.WriteAllTextAsync(manifestPath, canonical);

        Say("");
        Say("--- artifact");
        Say($"  id      : {artifact.ArtifactId}");
        Say($"  file    : {artifact.FileName}  ({artifact.SizeBytes} bytes)");
        Say($"  sha256  : {artifact.ContentDigest}");
        Say($"  manifest: {manifestPath}");
        Say($"  manifest digest: {BuildManifestCodec.ComputeManifestDigest(manifest)}");

        // ---- 7. Publish to the local immutable store ------------------------------------------------
        var publish = await store.PublishAsync(artifact, packagedPath);

        Say("");
        Say("--- local artifact store");
        Say($"  publish: accepted={publish.IsAccepted} alreadyPresent={publish.IsAlreadyPresent} reason={publish.RefusalReason}");
        if (publish.Entry is not null)
        {
            Say($"  stored : {publish.Entry.ArtifactId.Value} @ {publish.Entry.ContentDigest}");
        }

        // Re-publish the identical bytes: the idempotent path, and the only second write that is permitted.
        var republish = await store.PublishAsync(artifact, packagedPath);
        Say($"  re-publish identical bytes: accepted={republish.IsAccepted} alreadyPresent={republish.IsAlreadyPresent}");

        var verification = await store.VerifyHashAsync(artifact.ArtifactId, artifact.ContentDigest);
        Say($"  verify  : match={verification.IsMatch} storeIntact={verification.StoreContentIntact}");

        var byBuild = await store.ResolveByBuildIdAsync(identity.BuildId);
        Say($"  resolveByBuildId: {byBuild.Count} artifact(s)");

        // ---- 8. Certification ------------------------------------------------------------------------
        var certification = CertificationGate.Evaluate(new CertificationEvidence(
            manifest,
            [.. inputSet.ActiveInputs.Select(s => s.Label)],
            requiresEnvironmentRebuild,
            buildSucceeded: true));

        Say("");
        Say("--- certification");
        Say($"  verdict: {certification.Verdict}");
        foreach (var line in certification.Detail)
        {
            Say($"    {line}");
        }

        // ---- 9. Release-ref assessment ---------------------------------------------------------------
        IReadOnlySet<string> refsBefore;
        try
        {
            refsBefore = await GitReleaseRefPolicy.ObserveRefsAsync(runner, plan.RepositoryRoot);
        }
        catch (Exception ex)
        {
            refsBefore = new HashSet<string>(StringComparer.Ordinal) { $"unobserved:{ex.GetType().Name}" };
        }

        var releaseRefPolicy = new GitReleaseRefPolicy(runner, refsBefore);
        var releaseRef = await releaseRefPolicy.AssessAsync(plan.RepositoryLabel, plan.RepositoryRoot, commit);

        Say("");
        Say("--- release ref (the W9.3 prerequisite)");
        Say($"  eligible: {releaseRef.IsEligible}   refused: [{string.Join(", ", releaseRef.RefusalReasons)}]");
        foreach (var line in releaseRef.Detail)
        {
            Say($"    {line}");
        }

        // ---- 10. Machine-readable summary -----------------------------------------------------------
        var summary = new
        {
            unit = plan.UnitId,
            buildId = identity.BuildId.Value,
            inputDigest = identity.InputDigest.ToString(),
            commit,
            repositoryClean = !isDirty,
            sourceRoots = plan.SourceRoots,
            artifactId = artifact.ArtifactId.Value,
            artifactSha256 = artifact.ContentDigest.ToString(),
            artifactBytes = artifact.SizeBytes,
            manifestDigest = BuildManifestCodec.ComputeManifestDigest(manifest).ToString(),
            tests = new { plan.TestSuiteName, verdict = tests.Verdict.ToString(), tests.Total, tests.Passed, tests.Failed },
            secretScan = new
            {
                verdict = scanResult.Evidence.Verdict.ToString(),
                filesScanned = scanResult.Evidence.FilesScanned,
                activeFindings = scanResult.Evidence.FindingLocations.Count,
                quarantinedFindings = scanResult.Evidence.QuarantinedFindingLocations.Count,
                scannedSubjects = scanResult.Evidence.ScannedSubjectLabels,
                quarantinedSubjects = scanResult.Evidence.QuarantinedSubjectLabels
            },
            reproducibility = new
            {
                verdict = manifest.Reproducibility.Verdict.ToString(),
                buildsCompared = manifest.Reproducibility.BuildsCompared,
                digests = manifest.Reproducibility.ComparisonDigests.Select(d => d.ToString()).ToArray()
            },
            certification = new { verdict = certification.Verdict.ToString(), refusals = certification.RefusalReasons.Select(r => r.ToString()).ToArray() },
            releaseRef = new { eligible = releaseRef.IsEligible, refusals = releaseRef.RefusalReasons.Select(r => r.ToString()).ToArray() }
        };

        await File.WriteAllTextAsync(
            Path.Combine(evidence, "RUN_SUMMARY.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        await File.WriteAllTextAsync(Path.Combine(evidence, "RUN_TRANSCRIPT.txt"), transcript.ToString());

        Say("");
        Say(certification.IsCertified
            ? "RESULT: CERTIFIED_ARTIFACT_READY_FOR_RELEASE_BUNDLE"
            : $"RESULT: NOT_CERTIFIED [{string.Join(", ", certification.RefusalReasons)}]");

        return certification.IsCertified ? 0 : 1;
    }

    /// <summary>
    /// Runs the unit's tests and turns the result into typed evidence. A suite that does not run is recorded
    /// as NOT_RUN and a suite with no tests as NO_TESTS — both distinct from a pass, because the remedy for
    /// each is different and because reporting either as green is the defect this estate keeps re-finding.
    /// </summary>
    private static async Task<BuildTestEvidence> RunTestsAsync(IProcessRunner runner, BuildPlan plan, Action<string> say)
    {
        say("");
        say("--- tests");

        if (plan.TestCommand.Length == 0)
        {
            say("  no test command in the plan: NOT_RUN");
            return new BuildTestEvidence(plan.TestSuiteName, TestVerdict.NotRun, 0, 0, 0, 0);
        }

        var result = await runner.RunAsync(plan.TestCommand[0], [.. plan.TestCommand.Skip(1)], plan.RepositoryRoot);
        var output = result.StandardOutput + result.StandardError;

        // xUnit's console summary, e.g. "Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15"
        var match = System.Text.RegularExpressions.Regex.Match(
            output,
            @"(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)");

        if (!match.Success)
        {
            var noTests = output.Contains("No test is available", StringComparison.OrdinalIgnoreCase)
                       || output.Contains("No test matches", StringComparison.OrdinalIgnoreCase);

            say(noTests
                ? $"  '{plan.TestSuiteName}' reported no tests: NO_TESTS"
                : $"  '{plan.TestSuiteName}' produced no parsable result (exit {result.ExitCode}): NOT_RUN");

            return noTests
                ? new BuildTestEvidence(plan.TestSuiteName, TestVerdict.NoTests, 0, 0, 0, 0)
                : new BuildTestEvidence(plan.TestSuiteName, TestVerdict.NotRun, 0, 0, 0, 0);
        }

        var failed = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var passed = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        var skipped = int.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
        var total = int.Parse(match.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture);

        var verdict = failed > 0 ? TestVerdict.Failed : total > 0 ? TestVerdict.Passed : TestVerdict.NoTests;

        say($"  {plan.TestSuiteName}: {verdict}  total={total} passed={passed} failed={failed} skipped={skipped}");

        return new BuildTestEvidence(plan.TestSuiteName, verdict, total, passed, failed, skipped);
    }
}
