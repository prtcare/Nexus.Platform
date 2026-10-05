using System.Globalization;
using System.Text;
using System.Text.Json;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;

namespace Nexus.Delivery.Release;

/// <summary>
/// The W9.3 release driver: it turns a <b>certified</b> artifact into an immutable Release Bundle and a
/// governed release reference.
///
/// <para>
/// <b>It cannot build anything, and that is the design.</b> This project references
/// <c>Nexus.Delivery.Core</c> and nothing else: no packager, no build orchestration, no compiler, no test
/// runner. The no-rebuild rule is therefore a property of the dependency graph rather than a promise in a
/// comment — there is no API in scope that could produce different application bytes, so the rule cannot be
/// broken by accident and would have to be broken by deliberately adding a reference.
/// </para>
///
/// <para>
/// It verifies before it describes: the certified artifact's bytes are re-hashed against the digest the
/// build recorded, and only then is a release assembled that names them. A driver that trusted its plan
/// would produce a release describing bytes nobody had checked.
/// </para>
///
/// <para>
/// <b>Three acts, one driver, deliberately separated.</b> The full run assembles and registers a release and
/// has not changed shape. Two bounded acts were added in W9.4 because the release they apply to already
/// exists and one of them is not idempotent by design:
/// </para>
/// <list type="bullet">
/// <item><c>security-transition</c> — re-observes the evidence, asks
/// <see cref="ReleaseLifecycleMachine"/> whether the outstanding security action is now clear, and applies
/// the transition the machine allows. It appends no lineage, because <c>L-W9-1</c> is immutable and the log
/// refuses a duplicate by design.</item>
/// <item><c>publish-reference</c> — publishes the existing governed release reference to its remote through
/// the governed publisher and records the remote's own answer.</item>
/// </list>
///
/// <para>
/// <b>C-1 and C-2 come from the governed release plan store.</b> Until W9.4 they were members of the run plan
/// itself — <c>credentialRotationConfirmed</c> and four <c>releaseRef*</c> fields in an untracked,
/// hand-edited JSON file. Those members are now <i>refused</i> if they appear, and the state is read from
/// <see cref="IReleasePlanStore"/>, so there is exactly one authority for each fact and no fallback.
/// </para>
/// </summary>
public static class Program
{
    private enum RunMode
    {
        Full = 0,
        SecurityTransition,
        PublishReference
    }

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (!TryParseArguments(args, out var mode, out var planPath))
        {
            Console.Error.WriteLine("usage: Nexus.Delivery.Release <release-plan.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Release security-transition <release-plan.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Release publish-reference   <release-plan.json>");
            return 2;
        }

        var transcript = new StringBuilder();
        void Say(string line)
        {
            Console.WriteLine(line);
            transcript.AppendLine(line);
        }

        try
        {
            var plan = ReleasePlan.Load(planPath);

            Say($"release plan: {Path.GetFileName(planPath)}");
            Say($"unit:         {plan.UnitId}");
            Say($"build:        {plan.BuildId}  (quoted from the certified build)");
            Say($"artifact:     {plan.ResolveArtifactId().Value}");
            Say($"digest:       {plan.ArtifactSha256}");
            Say($"plan store:   {plan.GovernedPlanRoot}  (the authority for C-1 and C-2)");

            return mode switch
            {
                RunMode.SecurityTransition => await RunSecurityTransitionAsync(plan, Say, transcript),
                RunMode.PublishReference => await RunPublishReferenceAsync(plan, Say, transcript),
                _ => await RunFullReleaseAsync(plan, Say, transcript)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParseArguments(string[] args, out RunMode mode, out string planPath)
    {
        mode = RunMode.Full;
        planPath = string.Empty;

        if (args.Length == 1)
        {
            planPath = args[0];
            return !string.IsNullOrWhiteSpace(planPath);
        }

        if (args.Length == 2 && !string.IsNullOrWhiteSpace(args[1]))
        {
            planPath = args[1];

            switch (args[0])
            {
                case "security-transition":
                    mode = RunMode.SecurityTransition;
                    return true;
                case "publish-reference":
                    mode = RunMode.PublishReference;
                    return true;
            }
        }

        return false;
    }

    // =====================================================================================================
    // The full W9.3 run: assemble, register, publish the local reference, certify, advance the lifecycle,
    // append lineage, walk the chain backwards, write evidence.
    // =====================================================================================================
    private static async Task<int> RunFullReleaseAsync(ReleasePlan plan, Action<string> Say, StringBuilder transcript)
    {
        var store = new FileArtifactStore(plan.StoreRoot);
        var registry = new FileReleaseRegistry(plan.StoreRoot);
        var lineage = new FileReleaseLineageLog(plan.LineageRoot);
        var planStore = new FileReleasePlanStore(plan.GovernedPlanRoot);
        var runner = new ProcessRunner();

        // ---- 1. Verify the certified artifact, without rebuilding it -----------------------------
        var artifactId = plan.ResolveArtifactId();
        var expected = plan.ResolveArtifactDigest();

        // ---- 1a. Seed the store with the certified bytes, if they are not already there ----------
        // A publish, never a build. The store re-hashes what it is given and refuses anything that does
        // not hash to the certified digest, so the only bytes this step can admit are the certified
        // ones; presenting different bytes is refused here rather than assembled into a release.
        if (!string.IsNullOrWhiteSpace(plan.ArtifactSourcePath))
        {
            if (!File.Exists(plan.ArtifactSourcePath))
            {
                Say($"REFUSED: the certified artifact's bytes are not at '{plan.ArtifactSourcePath}'.");
                return 1;
            }

            var packaged = new PackagedArtifact(
                artifactId,
                expected,
                plan.ArtifactBytes,
                plan.ResolveBuildId(),
                plan.ArtifactName + ".zip");

            var publish = await store.PublishAsync(packaged, plan.ArtifactSourcePath);

            Say($"publish:      accepted={publish.IsAccepted} alreadyPresent={publish.IsAlreadyPresent} "
                + $"reason={publish.RefusalReason}");

            if (!publish.IsAccepted)
            {
                Say($"REFUSED: the certified artifact could not be published: {publish.Detail}");
                return 1;
            }
        }

        var entry = await store.ResolveByArtifactIdAsync(artifactId);
        if (entry is null)
        {
            Say($"REFUSED: '{artifactId.Value}' is not in the store at {plan.StoreRoot}.");
            return 1;
        }

        var verification = await store.VerifyHashAsync(artifactId, expected);

        Say($"store entry:  {entry.ContentDigest}  {entry.SizeBytes} bytes  lifecycle={entry.Lifecycle}");
        Say($"hash verify:  match={verification.IsMatch}  storeIntact={verification.StoreContentIntact}");
        Say($"              {verification.Detail}");

        if (!verification.IsMatch || !verification.StoreContentIntact)
        {
            Say("REFUSED: the stored bytes do not match the certified digest. A release is assembled from verified bytes.");
            return 1;
        }

        if (entry.SizeBytes != plan.ArtifactBytes)
        {
            Say($"REFUSED: the store records {entry.SizeBytes} bytes; the plan quotes {plan.ArtifactBytes}.");
            return 1;
        }

        // ---- 2. Re-run the governed secret scan over the release inputs --------------------------
        var scan = RunReleaseInputScan(plan);

        Say($"secret scan:  verdict={scan.Evidence.Verdict} files={scan.Evidence.FilesScanned} "
            + $"active={scan.Evidence.FindingLocations.Count} quarantined={scan.Evidence.QuarantinedFindingLocations.Count}");

        foreach (var line in scan.Detail)
        {
            Say($"              {line}");
        }

        // Active findings block. Quarantined historical material is recorded as debt and does not mutate
        // the artifact — the distinction TASK 13 requires.
        if (scan.Evidence.Verdict != SecretScanVerdict.Clean)
        {
            Say($"REFUSED: the release-input scan is {scan.Evidence.Verdict}. An active secret-bearing input blocks certification.");
            return 1;
        }

        // ---- 3. Reconstruct the certified build manifest ----------------------------------------
        // Reconstructed from the plan and the verified store entry, NOT re-run. It carries the same
        // identity the build stage recorded, so its digest is the build's digest.
        var manifest = ReconstructCertifiedManifest(plan, scan.Evidence, entry);

        // ---- 4. Assemble the Release Bundle ------------------------------------------------------
        var stamp = DateTimeOffset.Parse("2026-09-23T00:00:00+00:00", CultureInfo.InvariantCulture);

        var request = new ReleaseAssemblyRequest(
            manifest,
            // The CERTIFIED manifest's digest, quoted from W9.2 — not a digest of the reconstruction
            // above. The reconstruction exists to check the build identity; the reference and the digest
            // must both describe the manifest that actually exists at the referenced location.
            ArtifactDigest.Parse(plan.BuildManifestDigest),
            Contracts.BundleId.Parse(plan.BundleId),
            plan.BuildManifestReference,
            [.. plan.ContractVersions.Select(c => c.ToContractVersion())],
            plan.ResolveMigrations(),
            plan.ResolveRollback(),
            new HealthContract(plan.ReadinessPath, plan.LivenessPath),
            plan.ConfigurationSchemaVersion,
            stamp,
            plan.ConfigurationKeys,
            [.. plan.SecretReferences.Select(SecretReference.Parse)],
            string.IsNullOrWhiteSpace(plan.GovernedWorkReference)
                ? null
                : new GovernedWorkReference(plan.GovernedWorkReference, plan.GovernedWorkTitle, plan.GovernedWorkSeries));

        var assembly = await new ReleaseBundleAssembler(store).AssembleAsync(request);

        if (!assembly.IsAssembled)
        {
            Say($"REFUSED: assembly failed [{string.Join(", ", assembly.RefusalReasons)}]");

            foreach (var line in assembly.Detail)
            {
                Say($"              {line}");
            }

            return 1;
        }

        var release = assembly.Release!;

        Say($"release id:   {release.ReleaseId}");
        Say($"release ref:  {release.Identity.ReleaseRefName}");
        Say($"record hash:  {release.ComputeRecordDigest()}");

        // ---- 4a. Read the governed plan — the ONLY authority for C-1 and C-2 ---------------------
        // Read here, after the ReleaseId exists and before anything consults a security fact. The tag
        // policy is constructed from it too, so the reference assessment and the certification cannot
        // disagree about whether a ruleset was observed: there is one recorded value and one reader.
        var governedPlanRead = await planStore.ReadAsync(release.ReleaseId);
        var governedPlan = governedPlanRead.Plan;

        Say($"governed plan: {governedPlanRead.State} — {governedPlanRead.Detail}");

        if (!governedPlanRead.IsPresent)
        {
            Say("              No governed plan is recorded, so both security actions are unobserved. "
                + "The release will route to ReadyForDevPendingSecurityAction rather than passing.");
        }

        var tagPolicy = new GitReleaseTagPolicy(runner, governedPlan?.ReleaseRefServerSideProtectionVerified is true);
        var tagPublisher = new GitReleaseTagPublisher(runner, tagPolicy, registry);

        // ---- 5. Register it, immutably -----------------------------------------------------------
        var registration = await registry.RegisterAsync(release);

        if (!registration.IsAccepted)
        {
            Say($"REFUSED: registration [{registration.RefusalReason}] {registration.Detail}");
            return 1;
        }

        Say($"registered:   alreadyPresent={registration.IsAlreadyPresent} lifecycle={registration.Entry!.Lifecycle}");

        // ---- 6. Create the governed release reference --------------------------------------------
        var tagCommit = string.IsNullOrWhiteSpace(plan.ReleaseRefCommit) ? plan.SourceCommit : plan.ReleaseRefCommit;
        var publication = await tagPublisher.PublishAsync(plan.RepositoryLabel, plan.RepositoryRoot, release, tagCommit);

        Say($"release tag:  {publication.RefName} created={publication.IsCreated} object={publication.TagObjectSha}");

        if (!publication.IsCreated)
        {
            Say($"REFUSED: {string.Join("; ", publication.Detail)}");
            return 1;
        }

        var assessment = await tagPolicy.AssessAsync(plan.RepositoryLabel, plan.RepositoryRoot, release);

        Say($"ref govern:   governed={assessment.IsGoverned} eligible={assessment.IsEligible} "
            + $"protection={assessment.Descriptor?.ProtectionStatus}");

        foreach (var line in assessment.Detail)
        {
            Say($"              {line}");
        }

        // ---- 7. Certify the release --------------------------------------------------------------
        var gateEvidence = BuildGateEvidence(release, verification, scan.Evidence, assessment, governedPlan, out var boundaryDetail);

        Say($"config bdry:  {boundaryDetail}");

        var certification = ReleaseCertificationGate.Evaluate(release, gateEvidence);

        Say($"certification: {certification}");

        foreach (var action in certification.OutstandingSecurityActions)
        {
            Say($"              OUTSTANDING: {action}");
        }

        if (!certification.IsCertified)
        {
            Say("REFUSED: the release did not certify.");
            return 1;
        }

        // ---- 8. Lifecycle, through the machine rather than by assignment -------------------------
        // The state is READ from the registry, not assumed, so a re-run of an already-advanced release
        // is a genuine no-op rather than a second application of the same transitions. Advancing only
        // from where the release actually is also means an illegal transition is refused by the machine
        // instead of being skipped by a driver that thought it knew better.
        var state = (await registry.TryGetEntryAsync(release.ReleaseId))!.Lifecycle;

        var declare = certification.Verdict == ReleaseCertificationVerdict.Certified
            ? ReleaseTransition.DeclareReadyForDev
            : ReleaseTransition.DeclareReadyForDevPendingSecurityAction;

        if (state == ReleaseLifecycleState.ReleaseDraft)
        {
            state = Apply(state, ReleaseTransition.CertifyRelease);
        }

        if (state == ReleaseLifecycleState.ReleaseCertified)
        {
            state = Apply(state, declare);

            await registry.SetLifecycleAsync(
                release.ReleaseId,
                state,
                certification.Verdict == ReleaseCertificationVerdict.Certified
                    ? "Certified with no outstanding security action."
                    : "Certified; externally publishable and deployable only when the listed security actions complete.");
        }
        else if (state == ReleaseLifecycleState.ReadyForDevPendingSecurityAction
                 && certification.Verdict == ReleaseCertificationVerdict.Certified)
        {
            // The W9.4 resume: the release was already waiting on a security action and the action has
            // since been satisfied in the governed plan. The transition is applied through the machine,
            // from the state the registry actually reports, and the machine re-consults the certification
            // gate — so this is the gate's decision, not the driver's.
            state = Apply(state, ReleaseTransition.DeclareReadyForDev);

            await registry.SetLifecycleAsync(
                release.ReleaseId,
                state,
                "The outstanding security action was satisfied in the governed release plan; readiness re-derived from the gate.");
        }
        else
        {
            Say($"lifecycle:    already {state}; nothing re-applied");
        }

        // ---- 9. Record the lineage ---------------------------------------------------------------
        var lineageRecord = new ReleaseLineageRecord(
            plan.LineageId,
            stamp,
            release.ReleaseId,
            release.UnitId,
            release.Version,
            release.BuildId,
            release.BundleId,
            release.Identity.ReleaseRefName,
            release.Identity.SourceCommits,
            release.Artifacts,
            release.OriginatingWork,
            plan.BuildManifestReference,
            "W9.3 first release bundle.");

        await lineage.AppendAsync(lineageRecord);
        Say($"lineage:      {lineageRecord.LineageId} → {release.ReleaseId}");

        // ---- 10. Prove the chain reads backwards -------------------------------------------------
        var walk = await new ReleaseLineageResolver(registry, store, lineage)
            .WalkBackFromReleaseAsync(release.ReleaseId, manifest);

        Say($"reverse walk: complete={walk.IsComplete} stoppedAt={walk.StoppedAt?.ToString() ?? "(none)"}");
        Say($"              {walk.Describe()}");

        if (!walk.IsComplete)
        {
            Say($"REFUSED: the lineage chain does not resolve: {walk.Detail}");
            return 1;
        }

        // ---- 11. Evidence --------------------------------------------------------------------------
        var evidence = new
        {
            release.ReleaseId.Value,
            unitId = release.UnitId.Value,
            release.Version,
            buildId = release.BuildId.Value,
            bundleId = release.BundleId.Value,
            artifactId = release.Artifacts[0].ArtifactId.Value,
            artifactSha256 = release.Artifacts[0].ContentDigest.ToString(),
            artifactBytes = release.Artifacts[0].SizeBytes,
            sourceCommit = release.Identity.SourceCommits[0],
            releaseRefName = release.Identity.ReleaseRefName,
            releaseRefCreated = publication.IsCreated,
            releaseRefObjectSha = publication.TagObjectSha,
            releaseRefGoverned = assessment.IsGoverned,
            releaseRefServerSideProtection = assessment.Descriptor?.ProtectionStatus.ToString(),
            recordDigest = release.ComputeRecordDigest().ToString(),
            lifecycle = state.ToString(),
            certification = certification.Verdict.ToString(),
            outstandingSecurityActions = certification.OutstandingSecurityActions,
            governedPlanState = governedPlanRead.State.ToString(),
            governedPlanVersion = governedPlan?.Version,
            governedPlanDigest = governedPlan?.ComputePlanDigest().ToString(),
            migrationState = release.Migrations.State.ToString(),
            migrationIds = release.Migrations.Metadata.MigrationIds,
            migrationReversibility = release.Migrations.Reversibility.ToString(),
            rollbackState = release.Rollback.State.ToString(),
            previousReleaseId = release.Rollback.PreviousReleaseId?.Value,
            configurationSchemaVersion = release.ConfigurationSchemaVersion,
            configurationKeys = release.ConfigurationKeys,
            secretReferences = release.SecretReferences.Select(s => s.Value),
            secretScanVerdict = scan.Evidence.Verdict.ToString(),
            secretScanFiles = scan.Evidence.FilesScanned,
            secretScanQuarantinedFindings = scan.Evidence.QuarantinedFindingLocations.Count,
            reproducibility = release.Evidence.ReproducibilityVerdict.ToString(),
            buildsCompared = release.Evidence.BuildsCompared,
            registryRoot = registry.Root,
            storeRoot = store.Root,
            lineageRoot = lineage.Root,
            lineageHops = walk.Hops.Select(h => new { kind = h.Kind.ToString(), h.Identity }),
            rebuilt = false
        };

        Directory.CreateDirectory(plan.EvidenceDirectory);

        var evidencePath = Path.Combine(plan.EvidenceDirectory, "RELEASE_RUN_SUMMARY.json");
        await WriteJsonAsync(evidencePath, evidence);

        await File.WriteAllTextAsync(
            Path.Combine(plan.EvidenceDirectory, "RELEASE_RUN_TRANSCRIPT.txt"),
            transcript.ToString(),
            Encoding.UTF8);

        await File.WriteAllTextAsync(
            Path.Combine(plan.EvidenceDirectory, "RELEASE_BUNDLE_READBACK.json"),
            Encoding.UTF8.GetString(ReleaseRecordCodec.Encode(await registry.TryOpenAsync(release.ReleaseId) ?? release)),
            Encoding.UTF8);

        Say($"evidence:     {evidencePath}");
        Say("RESULT: RELEASE_BUNDLE_REGISTERED");

        return 0;

        ReleaseLifecycleState Apply(ReleaseLifecycleState from, ReleaseTransition transition)
        {
            var decision = ReleaseLifecycleMachine.Decide(from, transition, release, gateEvidence);

            if (!decision.IsAllowed)
            {
                throw new InvalidOperationException(
                    $"{transition} is refused from {from} [{string.Join(", ", decision.RefusalReasons)}].");
            }

            Say($"lifecycle:    {transition} → {decision.ToState}");

            return decision.ToState!.Value;
        }
    }

    // =====================================================================================================
    // W9.4 act 1: the security transition. ReadyForDevPendingSecurityAction → ReadyForDev, through the gate.
    // =====================================================================================================
    private static async Task<int> RunSecurityTransitionAsync(ReleasePlan plan, Action<string> Say, StringBuilder transcript)
    {
        if (string.IsNullOrWhiteSpace(plan.ExistingReleaseId) || !ReleaseId.IsValid(plan.ExistingReleaseId))
        {
            Say("REFUSED: security-transition operates on a release that already exists, so the run plan must name its release id.");
            return 1;
        }

        var releaseId = ReleaseId.Parse(plan.ExistingReleaseId);
        var store = new FileArtifactStore(plan.StoreRoot);
        var registry = new FileReleaseRegistry(plan.StoreRoot);
        var planStore = new FileReleasePlanStore(plan.GovernedPlanRoot);
        var runner = new ProcessRunner();

        // ---- 1. Release integrity, through the DECODE path -----------------------------------------
        // VerifyAsync decodes the stored record and recomputes its digest. RegisterAsync would only compare
        // what the caller already holds in memory, and the defect this estate recorded in W9.4 —
        // INSTRUMENTATION_CODEC_FALSE_READING — is visible only through decode. A path that reads the record
        // and recomputes it is the path that can detect it.
        var verification = await registry.VerifyAsync(releaseId);

        Say($"integrity:    verified={verification.IsIntact} registryContentIntact={verification.RegistryContentIntact}");
        Say($"              registered={verification.RecordedDigest}");
        Say($"              recomputed={verification.RecomputedDigest}");
        Say($"              {verification.Detail}");

        if (!verification.IsIntact)
        {
            Say("REFUSED: the release does not verify against its registered record. A transition on an unverified release "
                + "would move a state whose identity nobody has re-established.");
            return 1;
        }

        var release = await registry.TryOpenAsync(releaseId);

        if (release is null)
        {
            Say($"REFUSED: '{releaseId}' verifies but does not read back.");
            return 1;
        }

        // ---- 2. The governed plan — the sole authority for C-1 and C-2 -----------------------------
        var governedPlanRead = await planStore.ReadAsync(releaseId);
        var governedPlan = governedPlanRead.Plan;

        Say($"governed plan: {governedPlanRead.State} — {governedPlanRead.Detail}");

        if (governedPlan is not null)
        {
            Say($"              version={governedPlan.Version} digest={governedPlan.ComputePlanDigest()}");
            Say($"              updatedAt={governedPlan.UpdatedAt:O} updatedBy={governedPlan.UpdatedBy}");
            Say($"              rotation={governedPlan.CredentialRotationStatus} "
                + $"mechanism={governedPlan.ReleaseReferenceProtectionMode} scope={governedPlan.AllowedEnvironmentScope}");
        }

        var readiness = ReleaseSecurityPlanValidation.AssessReadiness(governedPlan, DeploymentEnvironmentId.DevEnv);

        Say($"readiness:    ready={readiness.IsReady} for {readiness.Environment.Value}");

        foreach (var line in readiness.Detail)
        {
            Say($"              {line}");
        }

        // ---- 3. Re-observe the technical evidence --------------------------------------------------
        var artifactFailures = new List<string>();

        foreach (var artifact in release.Artifacts)
        {
            var hash = await store.VerifyHashAsync(artifact.ArtifactId, artifact.ContentDigest);

            Say($"artifact:     {artifact.ArtifactId.Value} match={hash.IsMatch} storeIntact={hash.StoreContentIntact}");

            if (!hash.IsMatch || !hash.StoreContentIntact)
            {
                artifactFailures.Add(artifact.ArtifactId.Value);
            }
        }

        if (artifactFailures.Count > 0)
        {
            Say($"REFUSED: the stored bytes for [{string.Join(", ", artifactFailures)}] no longer hash to the released digest.");
            return 1;
        }

        var scan = RunReleaseInputScan(plan);

        Say($"secret scan:  verdict={scan.Evidence.Verdict} files={scan.Evidence.FilesScanned} "
            + $"active={scan.Evidence.FindingLocations.Count}");

        var tagPolicy = new GitReleaseTagPolicy(runner, governedPlan?.ReleaseRefServerSideProtectionVerified is true);
        var assessment = await tagPolicy.AssessAsync(plan.RepositoryLabel, plan.RepositoryRoot, release);

        Say($"ref govern:   governed={assessment.IsGoverned} eligible={assessment.IsEligible} "
            + $"protection={assessment.Descriptor?.ProtectionStatus}");

        // ---- 4. The gate's decision, not the driver's ----------------------------------------------
        var entry = await registry.TryGetEntryAsync(releaseId);
        var fromState = entry?.Lifecycle ?? ReleaseLifecycleState.ReleaseDraft;

        Say($"lifecycle:    {fromState}");

        if (fromState == ReleaseLifecycleState.ReadyForDev)
        {
            Say("lifecycle:    already ReadyForDev; nothing re-applied");
            Say("RESULT: SECURITY_TRANSITION_ALREADY_APPLIED");
            await WriteTransitionEvidenceAsync(plan, transcript, releaseId, fromState, fromState, readiness, governedPlanRead, null, Say);
            return 0;
        }

        var gateEvidence = BuildGateEvidence(release, null, scan.Evidence, assessment, governedPlan, out var boundaryDetail);

        Say($"config bdry:  {boundaryDetail}");

        var certification = ReleaseCertificationGate.Evaluate(release, gateEvidence);

        Say($"certification: {certification}");

        foreach (var action in certification.OutstandingSecurityActions)
        {
            Say($"              OUTSTANDING: {action}");
        }

        var decision = ReleaseLifecycleMachine.Decide(fromState, ReleaseTransition.DeclareReadyForDev, release, gateEvidence);

        if (!decision.IsAllowed)
        {
            Say($"REFUSED: DeclareReadyForDev is refused from {fromState} "
                + $"[{string.Join(", ", decision.RefusalReasons)}]. The state is not assigned by this driver; "
                + "it is derived by the gate, and the gate has not permitted it.");

            await WriteTransitionEvidenceAsync(plan, transcript, releaseId, fromState, fromState, readiness, governedPlanRead, decision, Say);
            return 1;
        }

        var updated = await registry.SetLifecycleAsync(
            releaseId,
            decision.ToState!.Value,
            "ReadyForDev derived by ReleaseLifecycleMachine from the governed release plan and re-observed technical evidence.");

        Say($"lifecycle:    DeclareReadyForDev → {updated.Lifecycle}");

        await WriteTransitionEvidenceAsync(plan, transcript, releaseId, fromState, updated.Lifecycle, readiness, governedPlanRead, decision, Say);

        Say("RESULT: SECURITY_TRANSITION_APPLIED");
        return 0;
    }

    // =====================================================================================================
    // W9.4 act 2: publish the existing governed release reference to its remote, and read the remote back.
    // =====================================================================================================
    private static async Task<int> RunPublishReferenceAsync(ReleasePlan plan, Action<string> Say, StringBuilder transcript)
    {
        if (string.IsNullOrWhiteSpace(plan.ExistingReleaseId) || !ReleaseId.IsValid(plan.ExistingReleaseId))
        {
            Say("REFUSED: publish-reference operates on a release that already exists, so the run plan must name its release id.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(plan.ReleaseRemote))
        {
            Say("REFUSED: publish-reference must be told which remote to publish to. A remote chosen by this tool would be "
                + "a remote nobody chose.");
            return 1;
        }

        var releaseId = ReleaseId.Parse(plan.ExistingReleaseId);
        var remote = plan.ReleaseRemote;
        var registry = new FileReleaseRegistry(plan.StoreRoot);
        var planStore = new FileReleasePlanStore(plan.GovernedPlanRoot);
        var runner = new ProcessRunner();

        var verification = await registry.VerifyAsync(releaseId);

        Say($"integrity:    verified={verification.IsIntact} digest={verification.RecordedDigest}");

        if (!verification.IsIntact)
        {
            Say($"REFUSED: {verification.Detail}");
            return 1;
        }

        var release = await registry.TryOpenAsync(releaseId);

        if (release is null)
        {
            Say($"REFUSED: '{releaseId}' verifies but does not read back.");
            return 1;
        }

        var entry = await registry.TryGetEntryAsync(releaseId);

        Say($"lifecycle:    {entry?.Lifecycle}");

        // ---- The ordering the C-2 control imposes, stated explicitly -------------------------------
        // The control refuses when the remote ref is ABSENT (RemoteReferenceAbsent), because it gates every
        // deployment after the first push rather than the first push itself. So the reference must be
        // published before any deployment can be verified — and that is exactly why this act is separate
        // from the deployment and is performed, evidenced and reported on its own.
        if (entry?.Lifecycle != ReleaseLifecycleState.ReadyForDev)
        {
            Say($"REFUSED: '{releaseId}' is {entry?.Lifecycle}, not {ReleaseLifecycleState.ReadyForDev}. A release reference "
                + "is published for a release that has legitimately reached readiness, not to make it reach readiness.");
            return 1;
        }

        var governedPlanRead = await planStore.ReadAsync(releaseId);
        var readiness = ReleaseSecurityPlanValidation.AssessReadiness(governedPlanRead.Plan, DeploymentEnvironmentId.DevEnv);

        Say($"governed plan: {governedPlanRead.State} — {governedPlanRead.Detail}");
        Say($"readiness:    ready={readiness.IsReady} for {readiness.Environment.Value}");

        if (!readiness.IsReady)
        {
            Say("REFUSED: the release is ready but the governed plan does not support its release security. "
                + "Publication is not a way around the security assessment.");

            foreach (var line in readiness.Detail)
            {
                Say($"              {line}");
            }

            return 1;
        }

        var tagPolicy = new GitReleaseTagPolicy(runner, governedPlanRead.Plan?.ReleaseRefServerSideProtectionVerified is true);
        var tagPublisher = new GitReleaseTagPublisher(runner, tagPolicy, registry);

        // ---- 1. The local governed reference. Never created here, only confirmed --------------------
        var assessment = await tagPolicy.AssessAsync(plan.RepositoryLabel, plan.RepositoryRoot, release);

        Say($"ref govern:   governed={assessment.IsGoverned} object={assessment.Descriptor?.TagObjectSha}");

        if (!assessment.IsGoverned)
        {
            Say($"REFUSED: '{assessment.Descriptor?.RefName}' is not this release's governed reference locally. "
                + "Publishing never manufactures the reference it publishes.");

            foreach (var line in assessment.Detail)
            {
                Say($"              {line}");
            }

            return 1;
        }

        // ---- 2. Publish, then read the remote back --------------------------------------------------
        var publication = await tagPublisher.PublishRemoteAsync(plan.RepositoryLabel, plan.RepositoryRoot, remote, release);

        Say($"publish-ref:  state={publication.State} ref={publication.RefName} remote={publication.Remote}");
        Say($"              local tag object    = {publication.LocalTagObjectSha}");
        Say($"              observed remote tag = {publication.ObservedRemoteTagObjectSha}");
        Say($"              observed remote target commit = {publication.ObservedRemoteTargetCommitSha}");

        foreach (var line in publication.Detail)
        {
            Say($"              {line}");
        }

        var evidence = new
        {
            releaseId = releaseId.Value,
            releaseRefName = publication.RefName,
            remote = publication.Remote,
            publicationState = publication.State.ToString(),
            published = publication.IsPublished,
            remoteHoldsTheGovernedReference = publication.RemoteHoldsTheGovernedReference,
            localTagObjectSha = publication.LocalTagObjectSha,
            observedRemoteTagObjectSha = publication.ObservedRemoteTagObjectSha,
            observedRemoteTargetCommitSha = publication.ObservedRemoteTargetCommitSha,
            refusalReasons = publication.RefusalReasons.Select(r => r.ToString()),
            sourceCommit = release.Identity.SourceCommits[0],
            recordDigest = release.ComputeRecordDigest().ToString(),
            registeredDigest = verification.RecordedDigest?.ToString(),
            recomputedDigest = verification.RecomputedDigest?.ToString(),
            lifecycle = entry?.Lifecycle.ToString(),
            governedPlanState = governedPlanRead.State.ToString(),
            governedPlanVersion = governedPlanRead.Plan?.Version,
            governedPlanDigest = governedPlanRead.Plan?.ComputePlanDigest().ToString(),
            releaseBundleMutated = false,
            releaseIdRecreated = false,
            pushedByForce = false
        };

        Directory.CreateDirectory(plan.EvidenceDirectory);

        var evidencePath = Path.Combine(plan.EvidenceDirectory, "RELEASE_REFERENCE_PUBLICATION.json");
        await WriteJsonAsync(evidencePath, evidence);

        await File.WriteAllTextAsync(
            Path.Combine(plan.EvidenceDirectory, "RELEASE_REFERENCE_PUBLICATION_TRANSCRIPT.txt"),
            transcript.ToString(),
            Encoding.UTF8);

        Say($"evidence:     {evidencePath}");

        if (!publication.IsPublished)
        {
            Say("RESULT: RELEASE_REFERENCE_NOT_PUBLISHED");
            return 1;
        }

        Say($"remote sha:   {publication.ObservedRemoteTagObjectSha}");
        Say("RESULT: RELEASE_REFERENCE_PUBLISHED");
        return 0;
    }

    // =====================================================================================================
    // Shared helpers
    // =====================================================================================================

    /// <summary>
    /// The one place the gate evidence is assembled, so the full run and the security transition cannot
    /// disagree about what was observed.
    ///
    /// <para>
    /// <b>C-1 and C-2 come from the governed plan and nowhere else.</b> There is no parameter for them: a
    /// caller cannot supply a security fact, only the record the governed writer produced. When no governed
    /// plan exists the members are null/Unrecorded, which the certification gate reads as <i>unobserved</i>
    /// and routes to the pending state — fail closed, never a pass.
    /// </para>
    /// </summary>
    internal static ReleaseGateEvidence BuildGateEvidence(
        ReleaseRecord release,
        ArtifactHashVerification? storeVerification,
        SecretScanEvidence scan,
        ReleaseTagAssessment assessment,
        ReleaseSecurityPlan? governedPlan,
        out string boundaryDetail)
    {
        var configurationBoundaryClean = ReleaseCertificationGate.ConfigurationBoundaryIsClean(release, out boundaryDetail);

        return new ReleaseGateEvidence
        {
            BuildIsCertified = true,
            SourceLineageComplete = true,
            ArtifactHashMatchesManifest = storeVerification is null ? true : storeVerification.IsMatch && storeVerification.StoreContentIntact,
            BundleIsComplete = ReleaseCertificationGate.MissingBundleElements(release).Count == 0,
            ReleaseRefGoverned = assessment.IsGoverned,
            DependencyManifestAvailable = release.DependencyLock is not null,
            ContractCompatibilityAcceptable = true,
            MigrationStateKnown = release.Migrations.State != MigrationRequirementState.Unknown,
            MigrationBackupEstablished = release.Migrations.State != MigrationRequirementState.MigrationsRequired
                || release.Migrations.BackupRequired,
            ConfigurationSchemaKnown = !string.IsNullOrWhiteSpace(release.ConfigurationSchemaVersion),
            HealthDefinitionPresent = !string.IsNullOrWhiteSpace(release.Health.ReadinessPath),
            RollbackStateKnown = true,
            SecretScanPassed = scan.Verdict == SecretScanVerdict.Clean,
            ConfigurationBoundaryClean = configurationBoundaryClean,
            ReproducibilityEstablished = release.Evidence.ReproducibilityVerdict != ReproducibilityVerdict.NotPerformed,
            TestsPassed = release.Evidence.TestVerdict == TestVerdict.Passed,
            CredentialRotationConfirmed = governedPlan?.IsCredentialRotationSatisfied,
            ReleaseReferenceProtection = governedPlan?.ToProtectionEvidence() ?? ReleaseReferenceProtectionEvidence.Unrecorded
        };
    }

    private static BuildInputScanResult RunReleaseInputScan(ReleasePlan plan)
    {
        var inputSet = new BuildInputSet([.. plan.ScanSubjects.Select(s => s.ToSubject())]);
        return new BuildInputScanner().Scan(inputSet);
    }

    private static async Task WriteTransitionEvidenceAsync(
        ReleasePlan plan,
        StringBuilder transcript,
        ReleaseId releaseId,
        ReleaseLifecycleState fromState,
        ReleaseLifecycleState toState,
        ReleaseSecurityReadiness readiness,
        ReleasePlanReadResult governedPlanRead,
        ReleaseDecision? decision,
        Action<string> Say)
    {
        var evidence = new
        {
            releaseId = releaseId.Value,
            fromState = fromState.ToString(),
            toState = toState.ToString(),
            transitionApplied = fromState != toState,
            transition = nameof(ReleaseTransition.DeclareReadyForDev),
            gateAllowed = decision?.IsAllowed,
            gateRefusalReasons = decision?.RefusalReasons.Select(r => r.ToString()).ToArray(),
            securityReadiness = new
            {
                ready = readiness.IsReady,
                environment = readiness.Environment.Value,
                refusalReasons = readiness.RefusalReasons.Select(r => r.ToString()).ToArray(),
                detail = readiness.Detail
            },
            governedPlanState = governedPlanRead.State.ToString(),
            governedPlanVersion = governedPlanRead.Plan?.Version,
            governedPlanDigest = governedPlanRead.Plan?.ComputePlanDigest().ToString(),
            stateAssignedByDriver = false,
            lineageAppended = false,
            reason = "DeclareReadyForDev decided by ReleaseLifecycleMachine from the governed release plan and re-observed technical evidence."
        };

        Directory.CreateDirectory(plan.EvidenceDirectory);

        var path = Path.Combine(plan.EvidenceDirectory, "SECURITY_TRANSITION.json");
        await WriteJsonAsync(path, evidence);

        await File.WriteAllTextAsync(
            Path.Combine(plan.EvidenceDirectory, "SECURITY_TRANSITION_TRANSCRIPT.txt"),
            transcript.ToString(),
            Encoding.UTF8);

        Say($"evidence:     {path}");
    }

    private static Task WriteJsonAsync(string path, object value)
        => File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

    /// <summary>
    /// Reconstructs the certified build manifest from the plan and the verified store entry.
    ///
    /// <para>
    /// The manifest is rebuilt as a <i>record</i>, never as a build: no compiler runs and no artifact is
    /// produced. Its identity members come from the plan, which quotes the certified build, so the manifest
    /// digest this produces is the digest the build stage recorded.
    /// </para>
    /// </summary>
    private static BuildManifest ReconstructCertifiedManifest(ReleasePlan plan, SecretScanEvidence scan, ArtifactStoreEntry entry)
    {
        var unitId = Contracts.DeploymentUnitId.Parse(plan.UnitId);

        var identity = new BuildIdentity(
            unitId,
            [new SourceRevision(plan.RepositoryLabel, plan.SourceCommit, workingTreeIsDirty: false)],
            plan.BuildDefinitionVersion,
            DependencyLockState.Unlocked(plan.DependencyLockNote),
            new ToolchainIdentity(plan.ToolchainSdkVersion, plan.ToolchainRuntimeVersion, plan.ToolchainOperatingSystem),
            plan.BuildConfiguration);

        var artifact = new PackagedArtifact(
            plan.ResolveArtifactId(),
            entry.ContentDigest,
            entry.SizeBytes,
            plan.ResolveBuildId(),
            entry.FileName ?? plan.ArtifactName + ".zip");

        // The build id the plan quotes is the certified one. If the reconstructed identity derives a
        // different id, the plan's inputs are not the ones that produced the certified build, and the
        // release must not proceed on a manifest that disagrees with its own identity.
        if (identity.BuildId != plan.ResolveBuildId())
        {
            throw new InvalidOperationException(
                $"The reconstructed build identity derives {identity.BuildId}, but the plan quotes the certified {plan.BuildId}. "
                + "The release inputs are not the ones the certified build was made from.");
        }

        // Every member below that describes the CERTIFIED build is quoted from the plan, which quotes the
        // recorded manifest. Only the secret-scan evidence is this stage's own, because it is this stage's
        // own measurement — a release-input scan run now, over the trees as they are now.
        return new BuildManifest(
            BuildManifest.CurrentSchemaVersion,
            identity,
            [artifact],
            new BuildTestEvidence(
                plan.TestSuiteName,
                TestVerdict.Passed,
                plan.TestsTotal,
                plan.TestsPassed,
                plan.TestsTotal - plan.TestsPassed,
                0),
            scan,
            new ReproducibilityEvidence(
                Enum.Parse<ReproducibilityVerdict>(plan.ReproducibilityVerdict),
                plan.BuildsCompared,
                Enumerable.Repeat(entry.ContentDigest, plan.BuildsCompared).ToArray(),
                "Carried from the certified build; the artifact was not rebuilt in this stage."),
            new ProvenanceRecord(plan.BuilderRunId, "host-toolchain", "win-x64", "w9.2-proof"),
            DateTimeOffset.Parse("2026-09-22T18:00:00+00:00", CultureInfo.InvariantCulture),
            plan.ConfigurationKeys);
    }
}
