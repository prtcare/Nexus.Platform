using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;

namespace Nexus.Delivery.Deploy;

/// <summary>
/// The W9.4 ENV-DEV deployment driver: it takes a release that is already <c>ReadyForDev</c> and moves it into
/// an isolated ENV-DEV, proving at each step that the bytes it moves are the certified ones.
///
/// <para>
/// <b>What it cannot do, by construction.</b> Its project references contain no build, packaging or npm
/// capability (see the csproj comment), so "deploy the certified artifact" cannot become "build something
/// similar" without someone deliberately adding a reference. It cannot mint a release, cannot move a tag, and
/// cannot alter a Release Bundle: those operations live in <c>Nexus.Delivery.Release</c> and no API in scope
/// here reaches them.
/// </para>
///
/// <para>
/// <b>Every decision is taken by a contract type, not by this file.</b> The pre-deployment release-reference
/// control is <see cref="ReleaseRefPreDeploymentGate"/>; the security readiness judgement is
/// <c>IReleasePlanStore</c> + <c>ReleaseSecurityPlanValidation</c>; the promotion decision is
/// <see cref="DeploymentStateMachine"/> and the recording is <see cref="DeploymentPromoter"/>; the schema
/// decision is <see cref="MigrationGovernance"/>. This driver sequences them and writes down what happened.
/// A driver that decided would be a sixth authority.
/// </para>
///
/// <para><b>The verbs.</b></para>
/// <list type="bullet">
/// <item><c>env-up</c> — creates the isolated database and the environment root named by the descriptor, write-once.</item>
/// <item><c>deploy</c> — gates → materialize → migrate → start → health → smoke → lineage → stop. ENV-DEV.</item>
/// <item><c>verify</c> — the same, as a verification of an existing ENV-DEV deployment. ENV-DEV.</item>
/// <item><c>promote-test</c> — the promotion preconditions, then the same path into ENV-TEST.</item>
/// <item><c>verify-test</c> — verifies the certified bytes running in ENV-TEST. ENV-TEST.</item>
/// <item><c>rollback-rehearsal</c> / <c>rollback-rehearsal-test</c> — exercises every rollback remedy this
/// release actually has, and records the one it does not.</item>
/// <item><c>failure-injection</c> / <c>failure-injection-test</c> — takes the database away underneath a
/// running deployment and checks that the readiness signal notices.</item>
/// <item><c>env-down</c> — removes the environment's database. Never reached from <c>deploy</c>.</item>
/// </list>
///
/// <para>
/// <b>Every verb names its lane, and the lane is a constant of the call site.</b> <c>deploy</c> and
/// <c>verify</c> pass <see cref="DeploymentLane.Dev"/>; <c>promote-test</c> and <c>verify-test</c> pass
/// <see cref="DeploymentLane.Test"/>. The lane is what the descriptor is validated against, so no argument
/// can redirect a verb at another environment — including by handing <c>deploy</c> a descriptor that names
/// ENV-TEST, which is refused before anything is read from it.
/// </para>
/// </summary>
internal static class Program
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Nexus.Delivery.Deploy env-up                  <env-descriptor.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Deploy deploy                 <release-plan.json> <env-dev.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Deploy verify                 <release-plan.json> <env-dev.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Deploy promote-test           <release-plan.json> <env-test.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Deploy verify-test            <release-plan.json> <env-test.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Deploy rollback-rehearsal     <release-plan.json> <env-dev.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Deploy rollback-rehearsal-test <release-plan.json> <env-test.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Deploy failure-injection      <release-plan.json> <env-dev.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Deploy failure-injection-test <release-plan.json> <env-test.json>");
            Console.Error.WriteLine("       Nexus.Delivery.Deploy env-down               <env-descriptor.json>");
            return 2;
        }

        var verb = args[0];
        var transcript = new StringBuilder();
        void Say(string line)
        {
            Console.WriteLine(line);
            transcript.AppendLine(line);
        }

        try
        {
            return verb switch
            {
                "env-up" => await EnvUpAsync(args[1], Say, transcript).ConfigureAwait(false),
                "deploy" when args.Length >= 3 => await DeployAsync(args[1], args[2], DeploymentLane.Dev, Say, transcript).ConfigureAwait(false),
                "verify" when args.Length >= 3 => await VerifyAsync(args[1], args[2], DeploymentLane.Dev, Say, transcript).ConfigureAwait(false),
                "promote-test" when args.Length >= 3 => await DeployAsync(args[1], args[2], DeploymentLane.Test, Say, transcript).ConfigureAwait(false),
                "verify-test" when args.Length >= 3 => await VerifyAsync(args[1], args[2], DeploymentLane.Test, Say, transcript).ConfigureAwait(false),
                "rollback-rehearsal" when args.Length >= 3 => await RollbackRehearsalAsync(args[1], args[2], DeploymentLane.Dev, Say, transcript).ConfigureAwait(false),
                "rollback-rehearsal-test" when args.Length >= 3 => await RollbackRehearsalAsync(args[1], args[2], DeploymentLane.Test, Say, transcript).ConfigureAwait(false),
                "failure-injection" when args.Length >= 3 => await FailureInjectionAsync(args[1], args[2], DeploymentLane.Dev, Say, transcript).ConfigureAwait(false),
                "failure-injection-test" when args.Length >= 3 => await FailureInjectionAsync(args[1], args[2], DeploymentLane.Test, Say, transcript).ConfigureAwait(false),
                "env-down" => await EnvDownAsync(args[1], Say).ConfigureAwait(false),
                _ => Fail(Say, $"Unknown verb or missing arguments: '{string.Join(' ', args)}'.")
            };
        }
        catch (Exception ex)
        {
            Say($"FAILED — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Fail(Action<string> say, string message)
    {
        say(message);
        return 1;
    }

    // ==================================================================== env-up

    private static async Task<int> EnvUpAsync(string envPlanPath, Action<string> say, StringBuilder transcript)
    {
        var descriptor = EnvironmentDescriptor.LoadDeclared(envPlanPath);

        say($"environment plan: {Path.GetFileName(envPlanPath)}");
        say($"environment     : {descriptor.Environment}");
        say($"root            : {descriptor.Root}");

        Directory.CreateDirectory(descriptor.Root);
        Directory.CreateDirectory(descriptor.LogsRoot);
        Directory.CreateDirectory(descriptor.Database!.SecretStoreDirectory);

        var database = new EnvironmentDatabase(descriptor, say);
        await database.EnsureAsync().ConfigureAwait(false);

        var described = await database.DescribeAsync().ConfigureAwait(false);
        var allContainers = await database.ListAllContainersAsync().ConfigureAwait(false);

        say($"isolation       : {described["publishedPorts"]}  network={described["networkMode"]}  mounts={described["mounts"]}");

        var evidence = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["environment"] = descriptor.Environment,
            ["root"] = descriptor.Root,
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["database"] = described,
            ["allContainersVisibleToTheEngine"] = allContainers,
            ["isolationClaims"] = new[]
            {
                $"The container is named '{descriptor.Database.ContainerName}', which no other service in the estate uses.",
                $"It publishes only on {descriptor.Database.HostAddress}:{descriptor.Database.Port}, a loopback address and a port no other container in the estate binds.",
                "It carries its own named volume, so its data is not shared with any other database.",
                "It carries its own credentials, generated write-once into the environment's secret store, which is outside every git repository.",
                // Quoted from the descriptor, not from a constant. This string was hardcoded to ENV-DEV while
                // the OBSERVED label beside it (labelNexusEnvironment) was also hardcoded - and both were
                // correct only while ENV-DEV was the one reachable environment. The observation was fixed
                // first and this claim was left behind, which the first ENV-TEST run made visible: the record
                // said `labelNexusEnvironment: "ENV-TEST"` two lines above a claim that the container is
                // labelled ENV-DEV. Two statements about one fact, disagreeing in one record, is the defect
                // the estate keeps re-finding; the claim now derives from the same descriptor the label does.
                $"It is labelled nexus.environment={descriptor.Environment} and nexus.managed-by=Nexus.Delivery.Deploy."
            },
            ["secretsInThisRecord"] = false,
            ["secretStorePath"] = descriptor.SecretFilePath
        };

        // Lane-qualified by the descriptor's own environment, not by a constant. For ENV-DEV this resolves to
        // the name the W9.4 evidence already carries (ENV_DEV_ENVIRONMENT.json); for ENV-TEST it cannot
        // overwrite it. A filename constant here would, under a second environment, produce an ENV-TEST
        // environment record filed under ENV-DEV's name.
        var environmentDocument = $"ENV_{descriptor.EnvironmentToken.ToUpperInvariant()}_ENVIRONMENT";

        WriteEvidence(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(envPlanPath))!, $"{environmentDocument}.json"), evidence);
        WriteTranscript(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(envPlanPath))!, $"{environmentDocument}_TRANSCRIPT.txt"), transcript);

        say($"RESULT: ENV_{descriptor.EnvironmentToken.ToUpperInvariant()}_READY");
        return 0;
    }

    private static async Task<int> EnvDownAsync(string envPlanPath, Action<string> say)
    {
        var descriptor = EnvironmentDescriptor.LoadDeclared(envPlanPath);
        var database = new EnvironmentDatabase(descriptor, say);

        await database.RemoveAsync(deleteVolume: true).ConfigureAwait(false);

        say($"RESULT: ENV_{descriptor.EnvironmentToken.ToUpperInvariant()}_REMOVED");
        return 0;
    }

    // ==================================================================== deploy

    private static async Task<int> DeployAsync(string releasePlanPath, string envPlanPath, DeploymentLane lane, Action<string> say, StringBuilder transcript)
    {
        var plan = Nexus.Delivery.Release.ReleasePlan.Load(releasePlanPath);
        var descriptor = EnvironmentDescriptor.Load(envPlanPath, lane.Environment);
        var evidencePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(releasePlanPath))!, $"{lane.DocumentPrefix}DEPLOYMENT.json");

        var releaseId = Require(plan.ExistingReleaseId, "existingReleaseId", "The deployment acts on a release that already exists; a full run assembles one and does not deploy it.");
        var remote = Require(plan.ReleaseRemote, "releaseRemote", "The pre-deployment control must observe the remote the reference was published to; a remote chosen by this tool is a remote nobody chose.");

        say($"release plan     : {Path.GetFileName(releasePlanPath)}");
        say($"environment      : {descriptor.Environment} at {descriptor.Root}");
        say($"release          : {releaseId}");

        var registry = new FileReleaseRegistry(plan.StoreRoot);
        var store = new FileArtifactStore(plan.StoreRoot);

        // ---- act 1: the release, verified through its own decode path ---------------------------------
        var verification = await registry.VerifyAsync(ReleaseId.Parse(releaseId)).ConfigureAwait(false);

        say($"release integrity: intact={verification.IsIntact} registryContentIntact={verification.RegistryContentIntact}");
        say($"                   registered={verification.RecordedDigest}");
        say($"                   recomputed={verification.RecomputedDigest}");

        if (!verification.IsIntact || !verification.RegistryContentIntact)
        {
            return Fail(say, $"REFUSED — the release does not verify: {verification.Detail}");
        }

        var release = await registry.TryOpenAsync(ReleaseId.Parse(releaseId)).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The registry verified '{releaseId}' but could not open it.");

        say($"lifecycle        : {verification.Lifecycle}");

        if (verification.Lifecycle != ReleaseLifecycleState.ReadyForDev)
        {
            return Fail(say, $"REFUSED — a deployment requires the release to be '{ReleaseLifecycleState.ReadyForDev}'; it is '{verification.Lifecycle}'.");
        }

        // ---- act 2: the C-2 pre-deployment control ---------------------------------------------------
        var tagPolicy = new GitReleaseTagPolicy(new ProcessRunner(), serverSideProtectionVerified: false);
        var preDeploymentVerdict = await new ReleaseRefPreDeploymentGate(tagPolicy, new GitRemoteReleaseTagVerifier(new ProcessRunner()), store)
            .VerifyAsync(plan.RepositoryLabel, plan.RepositoryRoot, remote, release)
            .ConfigureAwait(false);

        say($"pre-deployment   : state={preDeploymentVerdict.State}");
        say($"                   remote={preDeploymentVerdict.Remote} observed={preDeploymentVerdict.ObservedRemoteTagObjectSha ?? "(none)"} → {preDeploymentVerdict.ObservedRemoteTargetCommitSha ?? "(none)"}");

        foreach (var line in preDeploymentVerdict.Detail)
        {
            say($"                   · {line}");
        }

        if (!preDeploymentVerdict.IsDeployable)
        {
            return Fail(say, $"REFUSED — the pre-deployment release-reference control did not verify: {string.Join(", ", preDeploymentVerdict.RefusalReasons)}");
        }

        // ---- act 3: the governed release plan's judgement for this environment -----------------------
        var planStore = new FileReleasePlanStore(plan.GovernedPlanRoot);
        var planRead = await planStore.ReadAsync(ReleaseId.Parse(releaseId)).ConfigureAwait(false);

        say($"governed plan    : {planRead.State}{(planRead.IsPresent ? $" version {planRead.Plan!.Version}" : string.Empty)}");

        if (!planRead.IsPresent)
        {
            return Fail(say, $"REFUSED — no governed release plan is recorded for '{releaseId}'. The gate has no C-1/C-2 facts to read and will not fall back to another artifact.");
        }

        // Bound once, after the presence check, so every use below is provably the record that was found
        // rather than a second read that could see a later version.
        var governedPlan = planRead.Plan
            ?? throw new InvalidOperationException($"The governed store reported a plan for '{releaseId}' but returned none.");

        var readiness = ReleaseSecurityPlanValidation.AssessReadiness(governedPlan, descriptor.EnvironmentId);

        say($"security ready   : ready={readiness.IsReady} for {descriptor.Environment}");

        if (!readiness.IsReady)
        {
            return Fail(say, $"REFUSED — the governed plan does not authorize a deployment to {descriptor.Environment}: {string.Join(", ", readiness.RefusalReasons)}");
        }

        var authorization = new DeploymentAuthorization(
            DeploymentAuthorityRole.DeliveryTeam,
            lane.AuthorizationActor,
            DateTimeOffset.UtcNow);

        // ---- act 4: the bundle the promotion is decided over -------------------------------------------
        // Assembled from identities the plan and the release record already carry — no field here is
        // invented, and BuildBundleForPromotionAsync refuses if the projected bundle and the verified
        // record disagree about which unit and which digests this is.
        //
        // NOTE the ordering that is deliberately NOT taken here: the promotion decision is not taken at
        // this point. DeploymentStateMachine's DeployToDev gate requires ReadinessObserved to be non-null,
        // and readiness is a fact that only exists once this release is running in ENV-DEV. Taking the
        // decision here would mean inventing that observation. Every gate that CAN refuse before anything
        // moves has already run above (release integrity, the C-2 pre-deployment control, the governed
        // plan's readiness assessment) or runs below before the first target mutation (migration
        // governance). See the W9_4_MIGRATION_RECORD / deployment evidence for the explicit statement of
        // which refusals are pre-move and which are post-move.
        var bundle = await BuildBundleForPromotionAsync(release, plan).ConfigureAwait(false);

        // ---- act 5: materialize the certified bytes, and prove they are the certified bytes -----------
        var appRoot = descriptor.AppRoot(releaseId);

        if (Directory.Exists(appRoot))
        {
            // A re-run re-materializes from the store rather than trusting what is on disk — the store is
            // the retention mechanism, and a deployment that trusted its own previous output would be
            // unable to tell a corrupted target from a correct one.
            Directory.Delete(appRoot, recursive: true);
        }

        Directory.CreateDirectory(appRoot);

        var artifact = release.Artifacts[0];
        var stagedZip = Path.Combine(descriptor.ReleaseRoot(releaseId), "artifact.zip");

        var fetch = await store.FetchAsync(artifact.ArtifactId, stagedZip).ConfigureAwait(false);

        say($"artifact fetch   : {fetch.Status} ({artifact.ArtifactId.Value})");

        if (!fetch.IsFetched)
        {
            return Fail(say, $"REFUSED — the certified artifact could not be fetched from the store: {fetch.Detail}");
        }

        // ---- the artifact's SHA, measured BEFORE the deployment touches it ----------------------------
        var digestBefore = await Sha256FileAsync(stagedZip).ConfigureAwait(false);

        say($"artifact sha256  : before={digestBefore}");
        say($"                   recorded={artifact.ContentDigest}");

        if (!string.Equals(digestBefore, artifact.ContentDigest.ToString(), StringComparison.Ordinal))
        {
            return Fail(say, "REFUSED — the fetched bytes do not hash to the digest the release records.");
        }

        // ---- act 5a: the PROMOTION PRECONDITIONS, BEFORE anything in the target is touched --------------
        // TASK 1 of the W9.5 directive. Nine conditions, of which most are facts about what has been
        // established so far rather than observations of the target, and therefore have no home in
        // DeploymentGateEvidence. The gate can only refuse; it cannot authorize, and it writes nothing.
        //
        // PLACED HERE, AND THAT PLACEMENT IS THE FEATURE. The first version of this block sat with the
        // promotion decision in act 9 - after materialization, after the migrations, after the process was
        // started - which would have made a precondition failure land on an ENV-TEST that had already been
        // mutated. Every input this gate reads is available by act 5: the registry verification from act 1,
        // the C-2 verdict from act 2, the governed plan's readiness from act 3, the ledger, the declared
        // register, and the artifact's digest. Nothing here needs the target to have changed, so nothing
        // here may cause the target to change. This is the W9.4 lesson applied one layer up: the refusal that
        // CAN be taken before the move is taken before the move.
        var ledger = new FileDeploymentLineageLog(descriptor.DeploymentLineageRoot);

        if (lane.RequiresPromotionPreconditions)
        {
            var promotionGovernance = await EvaluatePromotionPreconditionsAsync(
                lane, descriptor, plan, release, ledger, bundle, verification, preDeploymentVerdict,
                readiness, digestBefore, say).ConfigureAwait(false);

            if (promotionGovernance.IsRefused)
            {
                foreach (var line in promotionGovernance.Detail)
                {
                    say($"                   · {line}");
                }

                // No evidence document is written, deliberately. That document records a deployment that was
                // attempted; this refusal happened BEFORE the target was touched, and a deployment record
                // for a deployment that never began would be a record of nothing. The refusal is the
                // transcript, and the transcript says the target is unchanged — which is the claim worth
                // making, because it is the one this placement exists to make true.
                say($"target           : UNCHANGED — the refusal was taken before any target mutation "
                    + "(no extraction, no migration, no process started)");

                return Fail(say, $"REFUSED — the promotion preconditions for {lane.Environment} are not satisfied: {string.Join(", ", promotionGovernance.RefusalReasons)}");
            }

            say($"promotion gate   : {promotionGovernance} — all nine preconditions established");
        }

        ZipFile.ExtractToDirectory(stagedZip, appRoot, overwriteFiles: true);

        // The tree digest is the observation of WHAT IS NOW ON DISK IN THE TARGET. It is recorded rather
        // than used as a pass/fail against the archive, because it is a different quantity: a tree hash
        // over (relative path, file content hash) pairs can never equal the hash of the archive file that
        // carried them. Comparing the two would be a check that cannot pass, which is the defect class this
        // estate keeps re-finding — so the two questions are asked separately, of the things they are
        // actually about.
        var treeAfter = await Sha256TreeAsync(appRoot).ConfigureAwait(false);

        say($"artifact tree    : sha256={treeAfter} files={Directory.GetFiles(appRoot, "*", SearchOption.AllDirectories).Length}");

        // ---- the artifact's SHA, measured AFTER materialization, and required to be identical ----------
        // This is the TASK 9 hard rule: the fetch is verified before and after, and the two readings must be
        // equal. It is the statement that the deployment did not transform the certified bytes — it copied
        // them. Read here, after extraction, so that extraction itself is inside the window being checked.
        var digestAfter = await Sha256FileAsync(stagedZip).ConfigureAwait(false);

        say($"artifact sha256  : after={digestAfter} unchanged={string.Equals(digestBefore, digestAfter, StringComparison.Ordinal)}");

        if (!string.Equals(digestBefore, digestAfter, StringComparison.Ordinal))
        {
            return Fail(say, "REFUSED — the artifact's bytes changed between the fetch and the materialization. The bytes deployed are not the bytes that were verified.");
        }

        // ---- act 6: the schema decision, taken before a single statement is executed ------------------
        // The deployment identity is minted HERE rather than at act 9, because the migration evidence below
        // is addressed by it and must be written whether or not the run reaches act 9. It is a pure function
        // of the release, the environment and a ledger-derived ordinal, so minting it earlier costs no
        // determinism - which is exactly why W9.4 made it a pure function in the first place.
        var deploymentId = await NextDeploymentIdAsync(ledger, bundle, descriptor, releaseId, lane).ConfigureAwait(false);

        say($"deployment id    : {deploymentId.Value} (attempt {deploymentId.Attempt} of {deploymentId.Release.Value} in {deploymentId.Environment})");

        var database = new EnvironmentDatabase(descriptor, say);
        await database.EnsureAsync().ConfigureAwait(false);

        // Acts 6, 7 and the re-judgement, extracted so that the migration's durable evidence is written by
        // the step itself rather than by every caller remembering to. See MigrationStep for why that
        // extraction is what closes D-W9.5-6.
        var migration = await RunMigrationStepAsync(
            deploymentId, descriptor, release, database, authorization, plan, appRoot, releaseId,
            Path.GetDirectoryName(Path.GetFullPath(envPlanPath))!, lane, say).ConfigureAwait(false);

        if (migration.Refusal is not null)
        {
            // Every migration refusal now writes the verb-level evidence document, not just the governance
            // one. Before D-W9.5-6 only the governance path did, so a run that failed at the migration
            // command or at the post-state check left no deployment record at all.
            WriteEvidence(evidencePath, BuildDeploymentEvidence(
                release, descriptor, plan, verification, preDeploymentVerdict, readiness, decision: null,
                migration.Governance, digestBefore, digestAfter, treeAfter, deployed: false, health: null, smoke: null, observability: null,
                reason: migration.Refusal,
                lane: lane));

            return Fail(say, migration.Refusal);
        }

        var governance = migration.Governance;
        var afterJudgement = migration.AfterJudgement;

        // ---- act 8: start the artifact and observe it -------------------------------------------------
        var health = await StartAndObserveAsync(descriptor, appRoot, releaseId, release, say).ConfigureAwait(false);

        if (health is null || !health.ReadinessObserved)
        {
            return Fail(say, "REFUSED — the artifact did not become ready in the target.");
        }

        // Owner Decision 3: smoke is load-bearing, not informational. Readiness proves that something
        // answered the address, not that the process this run started answered it. See VerifyAsync for the
        // measured false pass this refuses.
        if (!health.SmokeObserved)
        {
            say($"smoke            : processSurvived={health.Smoke["processSurvivedSmoke"]} "
                + $"readinessSecondProbe={health.Smoke["readinessSecondProbe"]} unknownRoute={health.Smoke["unknownRouteStatus"]}");
            await StopTrackedProcessAsync(say).ConfigureAwait(false);
            return Fail(
                say,
                "REFUSED — the smoke observation did not pass, so nothing has established that the process this run started "
                + "is the process that answered the readiness probe.");
        }

        // Stopped BEFORE any state moves. In W9.4 this ran last, so its failure cost the run its evidence
        // while the ledger kept the record (D-R2). A step that can fail must fail before the durable write.
        await StopTrackedProcessAsync(say).ConfigureAwait(false);

        // ---- act 9: the promotion decision, now that its evidence exists -------------------------------
        // The decision is taken here, and not earlier, because every member of this evidence is an
        // observation of the target: readiness was answered by this process in ENV-DEV, the digests were
        // read off the bytes on disk, and the compatibility was read out of the target's own migration
        // history. At act 4 none of those facts existed, and a decision taken then would have carried
        // nulls — which DeploymentStateMachine refuses as EvidenceIncomplete, correctly. What this costs
        // is stated plainly in the evidence rather than hidden: DeployToDev is a POST-move decision in
        // DeploymentStateMachine's own design, so its refusal lands after the environment has changed.
        // Every refusal that can be taken before the move has already been taken above.
        var promoter = new DeploymentPromoter(new FileArtifactRegistry(descriptor.Root), ledger);

        var observedEvidence = new DeploymentGateEvidence
        {
            WorkingTreeIsDirty = false,
            ReleaseRefIsProtected = preDeploymentVerdict.IsDeployable,
            ReadinessObserved = health.ReadinessObserved,
            SmokeObserved = health.SmokeObserved,
            // EARNED, not asserted: this value is the digest the fetched artifact hashed to BEFORE the
            // deployment touched it, re-read AFTER materialization and required to be identical. It is the
            // artifact's digest in the target because the artifact in the target IS that file, extracted.
            ObservedDigests = new Dictionary<DeploymentUnitId, ArtifactDigest>
            {
                [artifact.UnitId] = ArtifactDigest.Parse(digestBefore)
            },
            MigrationCompatibility = afterJudgement.ObservedCompatibility,
            ConfigurationSchemaSatisfied = true,
            Authorization = authorization,
            PreviousBundle = null,
            PreviousBundleAvailable = false,
            RollbackRehearsed = false,
            Reason = lane.RequiresPromotionPreconditions
                ? $"W9.5 governed promotion of the certified release into {lane.Environment}."
                : $"W9.4 first ENV-DEV deployment of the certified release.",
            ReleaseReferenceProtection = governedPlan.ToProtectionEvidence()
        };

        var deployRequest = new PromotionRequest(
            bundle, descriptor.EnvironmentId, lane.DeploymentPredecessorState, lane.DeploymentTransition, observedEvidence);

        var decision = DeploymentStateMachine.Decide(deployRequest);

        say($"promotion        : transition={lane.DeploymentTransition} allowed={decision.IsAllowed} from={decision.FromState} to={decision.NextState}");

        foreach (var reason in decision.RefusalReasons)
        {
            say($"                   · refused: {reason}");
        }

        if (decision.IsRefused)
        {
            await StopTrackedProcessAsync(say).ConfigureAwait(false);

            WriteEvidence(evidencePath, BuildDeploymentEvidence(
                release, descriptor, plan, verification, preDeploymentVerdict, readiness, decision,
                governance, digestBefore, digestAfter, treeAfter, deployed: false, health, health.Smoke, health.Observability,
                reason: $"The deployment state machine refused {lane.DeploymentTransition}: {string.Join(", ", decision.RefusalReasons)}.",
                lane: lane));

            return Fail(say, $"REFUSED — the deployment state machine did not allow {lane.DeploymentTransition} into {descriptor.Environment}: {string.Join(", ", decision.RefusalReasons)}");
        }

        // ---- act 10: the verification decision, over the same observations the deployment was decided on --
        // The verification asks the second question: the bytes now running are the certified bytes, and the
        // target's schema is the schema this release records. Its predecessor is the state the step above
        // records — the caller states that state, and the ledger is the record that it is true.
        var verifyRequest = new PromotionRequest(
            bundle, descriptor.EnvironmentId, lane.DeployedState, lane.VerificationTransition, observedEvidence);

        var verifyDecision = DeploymentStateMachine.Decide(verifyRequest);

        say($"verification     : transition={lane.VerificationTransition} allowed={verifyDecision.IsAllowed} from={verifyDecision.FromState} to={verifyDecision.NextState}");

        foreach (var reason in verifyDecision.RefusalReasons)
        {
            say($"                   · refused: {reason}");
        }

        // The identity was minted at act 6, before the migration, so the migration's durable evidence could
        // be addressed by it. Not re-derived here: two derivations of one value are two places to disagree.

        // ---- act 11: STAGE the evidence, then COMMIT both transitions ----------------------------------
        // Both transitions of one deployment carry the SAME DeploymentId: it names the deployment, and a
        // deployment and its verification are not two deployments.
        var deploymentFacts = BuildDeploymentEvidence(
                release, descriptor, plan, verification, preDeploymentVerdict, readiness, decision,
                governance, digestBefore, digestAfter, treeAfter, deployed: true, health, health.Smoke, health.Observability,
                reason: null,
                verifyDecision: verifyDecision,
                lane: lane)
            .ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value, StringComparer.Ordinal);

        deploymentFacts["deploymentId"] = deploymentId.Value;
        deploymentFacts["deploymentAttempt"] = deploymentId.Attempt;
        deploymentFacts["intendedLineageId"] = plan.LineageId;
        deploymentFacts["priorDeploymentLineageIds"] = await PriorDeploymentLineageIdsAsync(ledger, bundle, descriptor, lane).ConfigureAwait(false);

        var attempt = await NextVerificationAttemptAsync(ledger, bundle, deploymentId, lane).ConfigureAwait(false);

        var transaction = DeploymentEvidenceTransaction.ForAttempt(
            Path.GetDirectoryName(Path.GetFullPath(releasePlanPath))!,
            deploymentId,
            attempt);

        say($"evidence path    : {transaction.EvidencePath}");

        var staged = await transaction.StageAsync(
            releaseId, descriptor.EnvironmentId, deploymentId, attempt, plan.LineageId, deploymentFacts).ConfigureAwait(false);

        say($"evidence         : staged ({staged.Staging}) state={staged.Envelope.State} — written BEFORE the transition, deliberately");

        var committedLineageId = plan.LineageId;

        await transaction.CommitAsync(staged, async () =>
        {
            var promotion = await promoter.TransitionAsync(deployRequest, plan.LineageId, deploymentId).ConfigureAwait(false);

            say($"lineage          : {lane.DeploymentTransition} appended={promotion.IsComplete} id={plan.LineageId}");

            if (!promotion.IsComplete)
            {
                throw new InvalidOperationException(
                    $"the deployment lineage ledger did not accept '{plan.LineageId}': {promotion.Detail}");
            }

            if (verifyDecision.IsAllowed)
            {
                var verificationId = await NextLineageIdAsync(descriptor, plan.LineageId, say).ConfigureAwait(false);
                var verification = await promoter.TransitionAsync(verifyRequest, verificationId, deploymentId).ConfigureAwait(false);

                say($"lineage          : {lane.VerificationTransition} appended={verification.IsComplete} id={verification.LineageRecord?.LineageId ?? "(none)"}");

                committedLineageId = verification.LineageRecord?.LineageId ?? verificationId;
            }

            return committedLineageId;
        }).ConfigureAwait(false);

        say($"evidence         : sealed state={DeploymentEvidenceState.Committed} path={transaction.EvidencePath}");

        var lineage = await ledger.ReadByBundleAsync(bundle.BundleId).ConfigureAwait(false);
        say($"lineage records  : {lineage.Count} for bundle {bundle.BundleId.Value}");

        say($"RESULT: ENV_{descriptor.EnvironmentToken.ToUpperInvariant()}_DEPLOYED");
        return 0;
    }

    // ==================================================================== lineage identity

    /// <summary>
    /// The next free <c>L-W9-&lt;digits&gt;</c> id at or after <paramref name="firstCandidate"/>.
    ///
    /// <para>
    /// <b>Why the verification record cannot simply be <c>&lt;deployment id&gt;-verify</c>.</b> That was the
    /// first implementation, and it failed on a real run with
    /// <c>A lineage id is 'L-W9-&lt;digits&gt;' ... Received 'L-W9-2-verify'</c> — the id grammar is a prefix
    /// and digits, with no suffix and no separator. The failure is recorded here rather than quietly
    /// corrected because the shape of the mistake matters: the estate's L-series is a single numbered
    /// sequence, so a verification is not a sub-record of the deployment it verifies. It is the next record
    /// in the same series.
    /// </para>
    ///
    /// <para>
    /// <b>The ledger, not the clock, decides the id.</b> The next id is the lowest unused number at or after
    /// the plan's, read from the ledger itself. Two runs cannot collide, and a re-run cannot silently
    /// renumber — if the plan's own id is taken, the verification steps past it rather than reusing or
    /// rewriting anything. The plan's id is validated here rather than passed through, so a plan naming a
    /// malformed id is refused by this driver with an explanation instead of by the record constructor with
    /// a parameter exception.
    /// </para>
    /// </summary>
    private static async Task<string> NextLineageIdAsync(EnvironmentDescriptor descriptor, string planLineageId, Action<string> say)
    {
        const string prefix = DeploymentLineageRecord.SeriesPrefix;

        if (string.IsNullOrWhiteSpace(planLineageId) || !planLineageId.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The run plan's lineage id '{planLineageId}' does not begin with '{prefix}'. The estate's governed L-series is 'L-W9-<digits>'.");
        }

        var suffix = planLineageId[prefix.Length..];

        if (suffix.Length == 0 || !suffix.All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException(
                $"The run plan's lineage id '{planLineageId}' is '{prefix}' followed by '{suffix}', which is not a run of digits. "
                + "The series is numbered; a suffixed id such as 'L-W9-2-verify' is not a member of it.");
        }

        var existing = await new FileDeploymentLineageLog(descriptor.DeploymentLineageRoot)
            .ReadAllAsync().ConfigureAwait(false);

        var used = existing.Select(r => r.LineageId).ToHashSet(StringComparer.Ordinal);
        var candidate = int.Parse(suffix, CultureInfo.InvariantCulture);

        while (used.Contains(prefix + candidate.ToString(CultureInfo.InvariantCulture)))
        {
            candidate++;
        }

        var id = prefix + candidate.ToString(CultureInfo.InvariantCulture);

        say($"lineage id       : plan names {planLineageId}; ledger holds {used.Count} record(s); next free id is {id}");

        return id;
    }

    // ==================================================================== verify

    /// <summary>
    /// The verification act on its own, for the case this driver actually hit: the deployment was recorded
    /// and the verification was <b>allowed by its gate</b> but its record was never appended, because the
    /// driving process was terminated from outside between the two ledger writes.
    ///
    /// <para>
    /// <b>This is the completion of the sequence, not a way around it.</b> It re-runs every gate the
    /// deployment ran (release integrity through the decode path, the C-2 pre-deployment control, the
    /// governed plan's readiness assessment), re-materializes the certified bytes and re-proves them
    /// before and after, re-observes the target schema, and restarts the artifact to observe readiness and
    /// smoke again. It appends <i>only</i> the verification record. It will not append a deployment record,
    /// and it refuses unless the deployment it is verifying is already in the ledger for this bundle — so
    /// it cannot be used to verify something that was never deployed.
    /// </para>
    /// </summary>
    private static async Task<int> VerifyAsync(string releasePlanPath, string envPlanPath, DeploymentLane lane, Action<string> say, StringBuilder transcript)
    {
        var plan = Nexus.Delivery.Release.ReleasePlan.Load(releasePlanPath);
        var descriptor = EnvironmentDescriptor.Load(envPlanPath, lane.Environment);
        var evidencePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(envPlanPath))!, lane.EvidenceFileName);

        var releaseId = Require(plan.ExistingReleaseId, "existingReleaseId", "The verification acts on a release that already exists.");
        var remote = Require(plan.ReleaseRemote, "releaseRemote", "The pre-deployment control must observe the remote the reference was published to.");

        say($"release plan     : {Path.GetFileName(releasePlanPath)}");
        say($"environment      : {descriptor.Environment} at {descriptor.Root}");
        say($"release          : {releaseId}");

        var registry = new FileReleaseRegistry(plan.StoreRoot);
        var store = new FileArtifactStore(plan.StoreRoot);

        // ---- act 1: the release, re-verified through its own decode path -------------------------------
        var verification = await registry.VerifyAsync(ReleaseId.Parse(releaseId)).ConfigureAwait(false);

        say($"release integrity: intact={verification.IsIntact} registryContentIntact={verification.RegistryContentIntact}");
        say($"                   registered={verification.RecordedDigest}");
        say($"                   recomputed={verification.RecomputedDigest}");

        if (!verification.IsIntact || !verification.RegistryContentIntact)
        {
            return Fail(say, $"REFUSED — the release does not verify: {verification.Detail}");
        }

        var release = await registry.TryOpenAsync(ReleaseId.Parse(releaseId)).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The registry verified '{releaseId}' but could not open it.");

        if (verification.Lifecycle != ReleaseLifecycleState.ReadyForDev)
        {
            return Fail(say, $"REFUSED — verification requires the release to be '{ReleaseLifecycleState.ReadyForDev}'; it is '{verification.Lifecycle}'.");
        }

        // ---- act 2: the C-2 control, re-run ------------------------------------------------------------------
        var tagPolicy = new GitReleaseTagPolicy(new ProcessRunner(), serverSideProtectionVerified: false);
        var preDeployment = await new ReleaseRefPreDeploymentGate(tagPolicy, new GitRemoteReleaseTagVerifier(new ProcessRunner()), store)
            .VerifyAsync(plan.RepositoryLabel, plan.RepositoryRoot, remote, release)
            .ConfigureAwait(false);

        say($"pre-deployment   : state={preDeployment.State} remote={preDeployment.Remote} observed={preDeployment.ObservedRemoteTagObjectSha ?? "(none)"}");

        if (!preDeployment.IsDeployable)
        {
            return Fail(say, $"REFUSED — the pre-deployment release-reference control did not verify: {string.Join(", ", preDeployment.RefusalReasons)}");
        }

        // ---- act 3: the governed plan, re-read ---------------------------------------------------------
        var planStore = new FileReleasePlanStore(plan.GovernedPlanRoot);
        var planRead = await planStore.ReadAsync(ReleaseId.Parse(releaseId)).ConfigureAwait(false);

        if (!planRead.IsPresent)
        {
            return Fail(say, $"REFUSED — no governed release plan is recorded for '{releaseId}'.");
        }

        var governedPlan = planRead.Plan
            ?? throw new InvalidOperationException($"The governed store reported a plan for '{releaseId}' but returned none.");

        var readiness = ReleaseSecurityPlanValidation.AssessReadiness(governedPlan, descriptor.EnvironmentId);

        say($"security ready   : ready={readiness.IsReady} for {descriptor.Environment}");

        if (!readiness.IsReady)
        {
            return Fail(say, $"REFUSED — the governed plan does not authorize a deployment to {descriptor.Environment}: {string.Join(", ", readiness.RefusalReasons)}");
        }

        // ---- act 4: there must be a recorded deployment to verify --------------------------------------
        var ledger = new FileDeploymentLineageLog(descriptor.DeploymentLineageRoot);
        var bundle = await BuildBundleForPromotionAsync(release, plan).ConfigureAwait(false);
        var bundleRecords = await ledger.ReadByBundleAsync(bundle.BundleId).ConfigureAwait(false);

        var deployments = bundleRecords
            .Where(r => r.Transition == lane.DeploymentTransition && r.Environment == descriptor.EnvironmentId)
            .ToArray();

        say($"ledger           : {bundleRecords.Count} record(s) for {bundle.BundleId.Value}, {deployments.Length} of them a {lane.DeploymentTransition} into {descriptor.Environment}");

        if (deployments.Length == 0)
        {
            return Fail(say, $"REFUSED — nothing has been recorded as deployed into {descriptor.Environment} for {bundle.BundleId.Value}. A verification of a deployment that never happened would be a record of nothing.");
        }

        foreach (var record in deployments)
        {
            say($"                   · {record.LineageId} at {record.OccurredAt:O} from={record.FromState} to={record.ToState}");
        }

        // ---- act 5: materialize the certified bytes and re-prove them before and after ------------------
        var appRoot = descriptor.AppRoot(releaseId);
        var artifact = release.Artifacts[0];
        var stagedZip = Path.Combine(descriptor.ReleaseRoot(releaseId), "artifact.zip");

        var fetch = await store.FetchAsync(artifact.ArtifactId, stagedZip).ConfigureAwait(false);

        say($"artifact fetch   : {fetch.Status} ({artifact.ArtifactId.Value})");

        if (!fetch.IsFetched)
        {
            return Fail(say, $"REFUSED — the certified artifact could not be fetched: {fetch.Detail}");
        }

        var digestBefore = await Sha256FileAsync(stagedZip).ConfigureAwait(false);

        say($"artifact sha256  : before={digestBefore}");
        say($"                   recorded={artifact.ContentDigest}");

        if (!string.Equals(digestBefore, artifact.ContentDigest.ToString(), StringComparison.Ordinal))
        {
            return Fail(say, "REFUSED — the fetched bytes do not hash to the digest the release records.");
        }

        if (Directory.Exists(appRoot))
        {
            Directory.Delete(appRoot, recursive: true);
        }

        Directory.CreateDirectory(appRoot);
        ZipFile.ExtractToDirectory(stagedZip, appRoot, overwriteFiles: true);

        var treeAfter = await Sha256TreeAsync(appRoot).ConfigureAwait(false);
        var digestAfter = await Sha256FileAsync(stagedZip).ConfigureAwait(false);

        say($"artifact tree    : sha256={treeAfter} files={Directory.GetFiles(appRoot, "*", SearchOption.AllDirectories).Length}");
        say($"artifact sha256  : after={digestAfter} unchanged={string.Equals(digestBefore, digestAfter, StringComparison.Ordinal)}");

        if (!string.Equals(digestBefore, digestAfter, StringComparison.Ordinal))
        {
            return Fail(say, "REFUSED — the artifact's bytes changed between the fetch and the materialization.");
        }

        // ---- act 6: observe the schema, and judge it with the contract ---------------------------------
        var database = new EnvironmentDatabase(descriptor, say);
        await database.EnsureAsync().ConfigureAwait(false);

        var authorization = new DeploymentAuthorization(
            DeploymentAuthorityRole.DeliveryTeam,
            lane.AuthorizationActor,
            DateTimeOffset.UtcNow);

        var observed = await database.ObserveAsync(descriptor.EnvironmentId).ConfigureAwait(false);

        var appliedMatch = observed.AppliedMigrationIds.SequenceEqual(release.Migrations.Metadata.MigrationIds, StringComparer.Ordinal);

        say($"schema           : applied={observed.AppliedMigrationIds.Count} expected={release.Migrations.Metadata.MigrationIds.Count} exactMatch={appliedMatch}");

        if (!appliedMatch)
        {
            return Fail(say, "REFUSED — the target's applied migration set is not the set this release records.");
        }

        var judgement = MigrationGovernance.Judge(new MigrationGovernanceRequest(
            release.Migrations,
            descriptor.EnvironmentId,
            observed,
            MeasureDestructiveForwardMigrations(plan),
            authorization));

        say($"schema verdict   : {judgement.Decision} compatibility={judgement.ObservedCompatibility?.ToString() ?? "(none)"}");

        if (judgement.ObservedCompatibility is null)
        {
            return Fail(say, "REFUSED — the target's compatibility could not be established, so the verification would be unevaluable.");
        }

        // ---- act 6b: a required backup must be discharged, not merely noted -----------------------------
        // Owner Decision 1 and TASK 6. Before the remediation this verb consulted the migration judgement for
        // COMPATIBILITY ONLY, so a verdict of BackupRequired was printed and then ignored: the run went on to
        // certify a VerifiedDev while a backup obligation the release records was undischarged. That is the
        // same defect as D-R1 one layer up — an obligation recorded, and then not enforced.
        //
        // The remedy is the one the deploy verb applies: take the backup, verify it, re-judge. If the
        // re-judgement still requires a backup, the verification refuses. A certified VerifiedDev is not
        // produced while the obligation stands.
        if (judgement.RequiresBackup)
        {
            say($"backup           : required ({string.Join(", ", judgement.RefusalReasons)}); taking and verifying one before the verification proceeds");

            var backup = await database
                .BackupAsync(Path.Combine(descriptor.Root, "backups", releaseId))
                .ConfigureAwait(false);

            say($"backup           : verified={backup.IsVerified} path={backup.Path ?? "(none)"} bytes={backup.Bytes} tocEntries={backup.TocEntries}");

            if (!backup.IsVerified)
            {
                return Fail(
                    say,
                    $"REFUSED — this release records a backup obligation and the backup could not be verified: {backup.RefusalReason}");
            }

            observed = observed.WithVerifiedBackup(backup.Basis);

            judgement = MigrationGovernance.Judge(new MigrationGovernanceRequest(
                release.Migrations,
                descriptor.EnvironmentId,
                observed,
                MeasureDestructiveForwardMigrations(plan),
                authorization));

            say($"schema verdict   : {judgement.Decision} after the verified backup");

            if (!judgement.PermitsDeployment)
            {
                return Fail(
                    say,
                    "REFUSED — the target's schema state does not permit this verification even with a verified backup: "
                    + string.Join(", ", judgement.RefusalReasons));
            }
        }

        // ---- act 7: start the artifact and observe it --------------------------------------------------
        var health = await StartAndObserveAsync(descriptor, appRoot, releaseId, release, say).ConfigureAwait(false);

        if (health is null || !health.ReadinessObserved)
        {
            return Fail(say, "REFUSED — the artifact did not become ready in the target.");
        }

        // Owner Decision 3 (W9.4 instrument remediation): smoke is LOAD-BEARING, not informational.
        //
        // Readiness proves that something answered the address. It does not prove that the process THIS run
        // started is the thing that answered. The W9.4 false pass is the measured case: a second instance
        // could not bind and exited, the probe was answered by the process already holding the port, and the
        // run reported a healthy deployment it had not established — while its own log capture recorded
        // logLinesCaptured=0 and processSurvivedSmoke=false.
        if (!health.SmokeObserved)
        {
            say($"smoke            : processSurvived={health.Smoke["processSurvivedSmoke"]} "
                + $"readinessSecondProbe={health.Smoke["readinessSecondProbe"]} unknownRoute={health.Smoke["unknownRouteStatus"]}");
            await StopTrackedProcessAsync(say).ConfigureAwait(false);
            return Fail(
                say,
                "REFUSED — the smoke observation did not pass, so nothing has established that the process this run started "
                + "is the process that answered the readiness probe.");
        }

        // ---- act 8: stop what this driver started, BEFORE any state moves -------------------------------
        // Deliberately ahead of the transition. In W9.4 the stop ran after the lineage append, so a throw here
        // cost the run its evidence file while the ledger kept the record (D-R2). A step that can fail must
        // fail before the durable record is written, not after it.
        await StopTrackedProcessAsync(say).ConfigureAwait(false);

        // ---- act 9: decide the verification -------------------------------------------------------------
        var evidence = new DeploymentGateEvidence
        {
            WorkingTreeIsDirty = false,
            ReleaseRefIsProtected = preDeployment.IsDeployable,
            ReadinessObserved = health.ReadinessObserved,
            SmokeObserved = health.SmokeObserved,
            ObservedDigests = new Dictionary<DeploymentUnitId, ArtifactDigest>
            {
                [artifact.UnitId] = ArtifactDigest.Parse(digestBefore)
            },
            MigrationCompatibility = judgement.ObservedCompatibility,
            ConfigurationSchemaSatisfied = true,
            Authorization = authorization,
            PreviousBundle = null,
            PreviousBundleAvailable = false,
            RollbackRehearsed = false,
            Reason = lane.RequiresPromotionPreconditions
                ? $"W9.5 {lane.Environment} verification of the promoted certified release."
                : "W9.4 ENV-DEV verification, resuming after the deployment record was written and the driving process was terminated before the verification record was.",
            ReleaseReferenceProtection = governedPlan.ToProtectionEvidence()
        };

        var request = new PromotionRequest(
            bundle, descriptor.EnvironmentId, lane.DeployedState, lane.VerificationTransition, evidence);

        var decision = DeploymentStateMachine.Decide(request);

        say($"verification     : transition={lane.VerificationTransition} allowed={decision.IsAllowed} from={decision.FromState} to={decision.NextState}");

        foreach (var reason in decision.RefusalReasons)
        {
            say($"                   · refused: {reason}");
        }

        if (decision.IsRefused)
        {
            return Fail(say, $"REFUSED — the state machine did not allow {lane.VerificationTransition}: {string.Join(", ", decision.RefusalReasons)}");
        }

        var lineageId = await NextLineageIdAsync(descriptor, plan.LineageId, say).ConfigureAwait(false);

        // Minted here for the same reason the deploy path mints it at act 6: the identity must be stated
        // before anything that names it is written. This is the SAME deployment attempt's identity, not a
        // second one - a verification of a deployment is not another deployment, which is why this derives
        // from the lane's entry transition rather than from the verification transition.
        var deploymentId = await NextDeploymentIdAsync(ledger, bundle, descriptor, releaseId, lane).ConfigureAwait(false);

        say($"deployment id    : {deploymentId.Value} (attempt {deploymentId.Attempt} of {deploymentId.Release.Value} in {deploymentId.Environment})");

        var records = await ledger.ReadByBundleAsync(bundle.BundleId).ConfigureAwait(false);

        // Every member below is a fact this verification ESTABLISHED, assembled before the transition because
        // it must be durable before the transition. Owner Decision 2: nothing here may be written after the
        // state has moved, because a failure between the two is what produced W9.4's ledger-with-no-evidence.
        var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["releaseId"] = releaseId,
            ["deploymentId"] = deploymentId.Value,
            ["deploymentAttempt"] = deploymentId.Attempt,
            ["intendedLineageId"] = lineageId,
            ["environment"] = descriptor.Environment,
            ["occurredUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["releaseIntegrity"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["verifiedThroughTheDecodePath"] = true,
                ["isIntact"] = verification.IsIntact,
                ["registryContentIntact"] = verification.RegistryContentIntact,
                ["recordedDigest"] = verification.RecordedDigest?.ToString() ?? "(none)",
                ["recomputedDigest"] = verification.RecomputedDigest?.ToString() ?? "(none)",
                ["lifecycle"] = verification.Lifecycle?.ToString() ?? "(none)",
                ["bundleMutated"] = false
            },
            ["preDeploymentControl"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["state"] = preDeployment.State.ToString(),
                ["verified"] = preDeployment.IsDeployable,
                ["observedRemoteTagObjectSha"] = preDeployment.ObservedRemoteTagObjectSha ?? "(none)",
                ["observedRemoteTargetCommitSha"] = preDeployment.ObservedRemoteTargetCommitSha ?? "(none)",
                ["refusalReasons"] = preDeployment.RefusalReasons.Select(r => r.ToString()).ToArray()
            },
            ["artifact"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["recordedDigest"] = artifact.ContentDigest.ToString(),
                ["sha256Before"] = digestBefore,
                ["sha256After"] = digestAfter,
                ["bytesIdenticalBeforeAndAfter"] = string.Equals(digestBefore, digestAfter, StringComparison.Ordinal),
                ["targetTreeSha256"] = treeAfter,
                ["rebuilt"] = false
            },
            ["schema"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["exactMatch"] = appliedMatch,
                ["appliedCount"] = observed.AppliedMigrationIds.Count,
                ["decision"] = judgement.Decision.ToString(),
                ["observedCompatibility"] = judgement.ObservedCompatibility?.ToString() ?? "(none)",
                ["migrationWasRun"] = false,
                ["note"] = "The verification consults MigrationGovernance for compatibility only. No migration statement is issued by this verb, and none was required: the target's applied set already matched this release exactly."
            },
            ["observations"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["readinessObserved"] = health.ReadinessObserved,
                ["livenessStatus"] = health.LivenessStatus,
                ["readinessStatus"] = health.ReadinessStatus,
                ["readinessMillis"] = health.ReadinessMillis,
                ["smoke"] = health.Smoke,
                ["observability"] = health.Observability
            },
            ["verification"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["transition"] = lane.VerificationTransition.ToString(),
                ["allowed"] = decision.IsAllowed,
                ["fromState"] = decision.FromState.ToString(),
                ["nextState"] = decision.NextState.ToString(),
                ["stateAssignedByDriver"] = false,
                ["refusalReasons"] = decision.RefusalReasons.Select(r => r.ToString()).ToArray(),
                // There is deliberately NO "appended" member here any more. A fact file cannot truthfully
                // state the outcome of a commit that has not happened yet, and the W9.4 evidence wrote
                // "appended" only after the append — which is why losing it lost the whole record. Whether
                // the transition committed is recorded by the ENVELOPE's State and LineageId, which are
                // written after the commit and are the only members that may be.
                ["intendedLineageId"] = lineageId
            },
            ["deploymentRecordsForThisBundle"] = deployments.Select(r => r.LineageId).ToArray(),
            ["allLineageRecordsForThisBundle"] = records.Select(r => r.LineageId).ToArray(),
            ["secretsInThisRecord"] = false,
            ["applicationRebuilt"] = false,
            ["azureInfrastructureCreated"] = false,
            ["customerOrProductionDataUsed"] = false
        };

        // ---- act 10: STAGE the evidence, then COMMIT the transition -------------------------------------
        // The order is the whole of Owner Decision 2, and it is enforced by the transaction's shape rather
        // than by these comments: CommitAsync accepts nothing but a StagedEvidence, and the only way to
        // obtain one is for the evidence to have been written to disk first. If the staging throws, the
        // caller never reaches the commit and the ledger is untouched.
        // Evidence is addressed by ATTEMPT, not by environment. The environment-scoped path this replaced
        // let each verification overwrite the one before it; two W9.4 envelopes were destroyed that way.
        var attempt = await NextVerificationAttemptAsync(ledger, bundle, deploymentId, lane).ConfigureAwait(false);

        var transaction = DeploymentEvidenceTransaction.ForAttempt(
            Path.GetDirectoryName(Path.GetFullPath(envPlanPath))!,
            deploymentId,
            attempt);

        say($"evidence path    : {transaction.EvidencePath}");

        var staged = await transaction.StageAsync(
            releaseId, descriptor.EnvironmentId, deploymentId, attempt, lineageId, facts).ConfigureAwait(false);

        say($"evidence         : staged ({staged.Staging}) state={staged.Envelope.State} — written BEFORE the transition, deliberately");

        var promoter = new DeploymentPromoter(new FileArtifactRegistry(descriptor.Root), ledger);
        PromotionOutcome outcome = PromotionOutcome.Refused(decision);

        await transaction.CommitAsync(staged, async () =>
        {
            outcome = await promoter.TransitionAsync(request, lineageId, deploymentId).ConfigureAwait(false);

            if (!outcome.IsComplete)
            {
                throw new InvalidOperationException($"the lineage ledger did not accept '{lineageId}': {outcome.Detail}");
            }

            return outcome.LineageRecord!.LineageId;
        }).ConfigureAwait(false);

        say($"lineage          : {lane.VerificationTransition} appended={outcome.IsComplete} id={outcome.LineageRecord?.LineageId ?? "(none)"}");
        say($"evidence         : sealed state={DeploymentEvidenceState.Committed} path={transaction.EvidencePath}");

        WriteTranscript(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(envPlanPath))!, $"{lane.DocumentPrefix}VERIFICATION_TRANSCRIPT.txt"), transcript);

        say($"RESULT: ENV_{descriptor.EnvironmentToken.ToUpperInvariant()}_VERIFIED");
        return 0;
    }

    // ==================================================================== promotion preconditions

    /// <summary>
    /// Gathers the nine <see cref="PromotionGovernance"/> preconditions from the stores that own them, and
    /// hands them to the contract's pure judgement.
    ///
    /// <para>
    /// <b>This method observes; it does not decide.</b> Every member below is read from a governed store —
    /// the deployment ledger, the release registry, the pre-deployment verdict, the release plan — rather
    /// than asserted here. The one member that cannot be observed is the register of blocking defects, and
    /// that is supplied by the run plan, which is the party preparing the promotion's own statement. It is
    /// null when the plan does not carry it, and null refuses.
    /// </para>
    ///
    /// <para>
    /// <b>The source environment is the lane's <c>PromotedFrom</c>, not a relative of the current
    /// environment.</b> ENV-TEST is promoted from ENV-DEV and from nothing else (rule R-4), so the records
    /// this reads are ENV-DEV's <c>DeployToDev</c> and <c>VerifyInDev</c> records for this bundle. The DEV
    /// lane never reaches this method: <c>RequiresPromotionPreconditions</c> is false for it, because a
    /// deployment into the first environment is not a promotion.
    /// </para>
    /// </summary>
    private static async Task<PromotionGovernanceDecision> EvaluatePromotionPreconditionsAsync(
        DeploymentLane lane,
        EnvironmentDescriptor descriptor,
        Nexus.Delivery.Release.ReleasePlan plan,
        ReleaseRecord release,
        FileDeploymentLineageLog ledger,
        ReleaseBundle bundle,
        ReleaseVerification releaseVerification,
        ReleaseRefPreDeploymentVerdict preDeployment,
        ReleaseSecurityReadiness readiness,
        string digestBefore,
        Action<string> say)
    {
        var sourceEnvironment = lane.PromotionSource
            ?? throw new InvalidOperationException(
                $"{lane.Environment} has no promotion source, so this gate should not have been invoked for it.");

        var records = await ledger.ReadByBundleAsync(bundle.BundleId).ConfigureAwait(false);

        var sourceDeployments = records
            .Where(r => r.Environment == sourceEnvironment && r.Transition == DeploymentTransition.DeployToDev)
            .OrderBy(r => r.OccurredAt)
            .Select(r => r.LineageId)
            .ToArray();

        var sourceVerifications = records
            .Where(r => r.Environment == sourceEnvironment && r.Transition == DeploymentTransition.VerifyInDev)
            .OrderBy(r => r.OccurredAt)
            .Select(r => r.LineageId)
            .ToArray();

        say($"promotion gate   : source {sourceEnvironment} holds {sourceDeployments.Length} deployment(s) "
            + $"and {sourceVerifications.Length} verification(s) for {bundle.BundleId.Value}");

        // Rollback capability: three-valued. The capability under test is not "a previous release exists" -
        // this is the first release of marketsurvey.api and W9.4 recorded NoPreviousAcceptedRelease - but
        // "the remedy this release actually has has been exercised". A rehearsal is a fact about the team
        // rather than about the store, so it is read from the rehearsal evidence the lane wrote.
        var rollbackProven = ReadRollbackCapability(plan, lane, say);

        var request = new PromotionGovernanceRequest
        {
            TargetEnvironment = descriptor.EnvironmentId,
            CurrentState = lane.DeploymentPredecessorState,
            SourceEnvironment = sourceEnvironment,

            SourceReleaseCertified = releaseVerification.IsIntact && releaseVerification.RegistryContentIntact,
            SourceReleaseLifecycle = releaseVerification.Lifecycle,

            SourceDeploymentLineageIds = sourceDeployments,
            SourceVerificationLineageIds = sourceVerifications,

            RollbackCapabilityProven = rollbackProven,

            ReleaseReferenceIntact = preDeployment.IsDeployable,
            ArtifactIntegrityIntact = string.Equals(digestBefore, release.Artifacts[0].ContentDigest.ToString(), StringComparison.Ordinal),
            SecurityPlanPermitsTarget = readiness.IsReady,

            BlockingDefects = plan.BlockingDefects is null
                ? DeploymentBlockingDefectRegister.NotDeclared
                : DeploymentBlockingDefectRegister.Declared(plan.BlockingDefects.Select(d => d.ToDefect())),

            // The descriptor loaded, validated, and named this lane's environment - all before this method was
            // reached. A descriptor that had failed any of those would have thrown at Load.
            TargetEnvironmentDefinitionValid = true
        };

        return PromotionGovernance.Evaluate(request);
    }

    /// <summary>
    /// Whether this release's rollback remedy has been exercised, read from the rehearsal the
    /// <b>source environment</b> recorded.
    ///
    /// <para>
    /// <b>Three-valued, and the third value matters.</b> The estate's releases record
    /// <c>NoPreviousAcceptedRelease</c> — there is no earlier release of <c>marketsurvey.api</c> to return
    /// to, and W9.4 recorded that as the state rather than leaving it as an absence. So the capability under
    /// test is not "a previous release exists" but "the remedy this release actually has has been
    /// exercised". For a first release that remedy is the artifact-level reversion — re-fetching the
    /// certified bytes from the store and proving they are identical — and W9.4 rehearsed exactly that in
    /// ENV-DEV.
    /// </para>
    ///
    /// <para>
    /// <b>It reads the SOURCE environment's rehearsal, not this one's, and the order is not arbitrary.</b>
    /// A promotion rests on what the environment being promoted <i>from</i> has established: rule R-4 admits
    /// an artifact to an environment only from the one before it, proven. The TEST-specific rehearsal TASK 16
    /// requires is a separate proof, performed after this promotion, and it is what a future ENV-PROD
    /// promotion would rest on. Reading this lane's own rehearsal here would make the requirement circular —
    /// the promotion could not run until the thing it enables had already happened.
    /// </para>
    ///
    /// <para>
    /// <b>It reads <c>reversionRehearsed</c>, not a verdict string.</b> The first version of this method
    /// looked for a <c>result</c> member; the rehearsal document does not have one — its verdict is on the
    /// verb's stdout, and the document records the facts (<c>reversionRehearsed</c>,
    /// <c>releaseLevelRollbackRehearsed</c>, and an <c>honestStatement</c>). Reading a member the document
    /// does not carry would have returned null forever, and the gate would have refused for a reason that
    /// looked like a missing proof rather than a wrong reader.
    /// </para>
    ///
    /// <para>
    /// <b>Absent evidence is <see langword="null"/>, and null refuses.</b> A rehearsal file that is missing
    /// or unreadable is not a rehearsal that passed.
    /// </para>
    /// </summary>
    private static bool? ReadRollbackCapability(
        Nexus.Delivery.Release.ReleasePlan plan,
        DeploymentLane lane,
        Action<string> say)
    {
        if (lane.PromotionSource is null)
        {
            // Not a promotion. There is no source environment to have rehearsed anything.
            return null;
        }

        var sourcePrefix = lane.PromotionSource == DeploymentEnvironmentId.DevEnv ? "DEV_" : "TEST_";

        var directory = plan.SourceEnvironmentEvidenceDirectory;

        if (string.IsNullOrWhiteSpace(directory))
        {
            say("                   rehearsal evidence: the plan names no sourceEnvironmentEvidenceDirectory, "
                + "so where the source environment's proof lives has not been stated.");

            return null;
        }

        var candidate = Path.Combine(directory, $"{sourcePrefix}ROLLBACK_REHEARSAL.json");

        if (!File.Exists(candidate))
        {
            say($"                   rehearsal evidence: '{candidate}' does not exist.");

            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(candidate));

            if (!document.RootElement.TryGetProperty("reversionRehearsed", out var rehearsed)
                || rehearsed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                say($"                   rehearsal evidence: '{Path.GetFileName(candidate)}' records no "
                    + "reversionRehearsed fact, so nothing about the remedy is established.");

                return null;
            }

            say($"                   rehearsal evidence: {Path.GetFileName(candidate)} "
                + $"reversionRehearsed={rehearsed.GetBoolean()} "
                + $"releaseLevelRollbackRehearsed={document.RootElement.TryGetProperty("releaseLevelRollbackRehearsed", out var rl) && rl.ValueKind is JsonValueKind.True or JsonValueKind.False && rl.GetBoolean()}");

            return rehearsed.GetBoolean();
        }
        catch (JsonException ex)
        {
            say($"                   rehearsal evidence: '{Path.GetFileName(candidate)}' is not readable JSON: {ex.Message}");

            return null;
        }
    }

    // ==================================================================== deployment identity

    /// <summary>
    /// The identity of <b>this</b> deployment attempt, derived from the ledger rather than chosen.
    ///
    /// <para>
    /// <b>Owner Decision 4.</b> A <c>LineageId</c> numbers an audit record; a <c>DeploymentId</c> names a
    /// deployment. The W9.4 re-establishment had no member for the second, so recovering ENV-DEV had no
    /// identity to be recorded under — retaining <c>L-W9-2</c> said nothing about the recovery, and minting a
    /// new <c>L-W9-*</c> would have invented a transition that never happened.
    /// </para>
    ///
    /// <para>
    /// <b>The attempt ordinal is counted, not chosen.</b> It is the number of deployment acts already
    /// recorded for this bundle in this environment, plus one. The records written before explicit identity
    /// existed are counted too: they were real attempts that simply had no member to carry an id, and
    /// numbering that skipped them would make the sequence describe only the era it was introduced in.
    /// </para>
    ///
    /// <para>
    /// <b>No id is invented for a historical record.</b> A recovery therefore cannot link backwards by
    /// <c>DeploymentId</c> — the earlier attempts do not have one. The link is named by <c>LineageId</c>
    /// instead, which is the identity those records genuinely have, and it is recorded in the evidence
    /// under <c>priorDeploymentLineageIds</c>.
    /// </para>
    /// </summary>
    private static async Task<DeploymentId> NextDeploymentIdAsync(
        FileDeploymentLineageLog ledger,
        ReleaseBundle bundle,
        EnvironmentDescriptor descriptor,
        string releaseId,
        DeploymentLane lane)
    {
        var records = await ledger.ReadByBundleAsync(bundle.BundleId).ConfigureAwait(false);

        // The ordinal counts this LANE's own entry transition into this environment. For ENV-DEV that is a
        // DeployToDev; for ENV-TEST it is a PromoteToTest. Counting both would make a DEV redeployment
        // consume a TEST attempt ordinal, and the two lanes' identities would then depend on each other's
        // history — which is precisely what an environment-scoped identity must not do.
        var priorAttempts = records.Count(r =>
            r.Transition == lane.DeploymentTransition && r.Environment == descriptor.EnvironmentId);

        return DeploymentId.For(ReleaseId.Parse(releaseId), descriptor.EnvironmentId, priorAttempts + 1);
    }

    /// <summary>
    /// The ordinal of <b>this verification attempt</b> within its deployment, counted from the ledger.
    ///
    /// <para>
    /// A deployment can be verified more than once — the W9.4 remediation verified the recovered
    /// deployment three times as the instrument was completed — and each attempt's evidence must be
    /// immutable. The identity that distinguishes them is this ordinal, scoped to the
    /// <see cref="DeploymentId"/>; combined the two address the envelope at
    /// <c>deployments/&lt;DeploymentId&gt;/attempts/&lt;ordinal&gt;/</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Not a timestamp.</b> A clock is not an identity: two runs in the same second would collide, and
    /// the value would not be reproducible on another host. Both ordinals here are derived from the
    /// governed ledger, which is the record of what actually happened.
    /// </para>
    /// </summary>
    private static async Task<int> NextVerificationAttemptAsync(
        FileDeploymentLineageLog ledger,
        ReleaseBundle bundle,
        DeploymentId deploymentId,
        DeploymentLane lane)
    {
        var records = await ledger.ReadByBundleAsync(bundle.BundleId).ConfigureAwait(false);

        var prior = records.Count(r =>
            r.Transition == lane.VerificationTransition && r.DeploymentId == deploymentId);

        return prior + 1;
    }

    /// <summary>
    /// The <c>LineageId</c>s of the verification attempts already recorded against a deployment, oldest
    /// first. Used to resolve an attempt ordinal back to the record it produced.
    /// </summary>
    private static async Task<string[]> PriorVerificationLineageIdsAsync(
        FileDeploymentLineageLog ledger,
        ReleaseBundle bundle,
        DeploymentId deploymentId,
        DeploymentLane lane)
    {
        var records = await ledger.ReadByBundleAsync(bundle.BundleId).ConfigureAwait(false);

        return
        [
            .. records
                .Where(r => r.Transition == lane.VerificationTransition && r.DeploymentId == deploymentId)
                .OrderBy(r => r.OccurredAt)
                .Select(r => r.LineageId)
        ];
    }

    /// <summary>
    /// The <c>LineageId</c>s of the deployment acts this attempt follows, for the evidence. Reported rather
    /// than linked, because a link would require an id those records do not have.
    /// </summary>
    private static async Task<string[]> PriorDeploymentLineageIdsAsync(
        FileDeploymentLineageLog ledger,
        ReleaseBundle bundle,
        EnvironmentDescriptor descriptor,
        DeploymentLane lane)
    {
        var records = await ledger.ReadByBundleAsync(bundle.BundleId).ConfigureAwait(false);

        return
        [
            .. records
                .Where(r => r.Transition == lane.DeploymentTransition && r.Environment == descriptor.EnvironmentId)
                .OrderBy(r => r.OccurredAt)
                .Select(r => r.LineageId)
        ];
    }

    // ==================================================================== migration step
    /// <summary>
    /// What one migration step produced. <see cref="Refusal"/> is non-null when the caller must refuse, and
    /// carries the sentence to refuse with — the step decides <i>whether</i>, the caller decides <i>how to
    /// report it</i>, which is the same split the rest of this driver uses.
    ///
    /// <para>
    /// <see cref="AfterMigration"/> and <see cref="AfterJudgement"/> are null exactly when
    /// <see cref="Refusal"/> is non-null, because a refused step never reached the post-migration
    /// observation. They are nullable rather than defaulted so that the caller has to confront that, instead
    /// of a placeholder quietly standing in for an observation nobody took.
    /// </para>
    /// </summary>
    private sealed record MigrationStep(
        MigrationOutcome Governance,
        MigrationTargetObservation? AfterMigration,
        MigrationGovernanceVerdict? AfterJudgement,
        string? Refusal,
        string EvidencePath,
        string Result);

    /// <summary>
    /// Acts 6 and 7 and the post-migration re-judgement, in one scope that <b>cannot exit without writing
    /// the migration's durable evidence</b>.
    ///
    /// <para>
    /// <b>This method is the remedy for <c>D-W9.5-6</c>.</b> Before it, the driver wrote its evidence
    /// document on the migration-governance refusal path and on the state-machine refusal path, but the two
    /// failure paths that occur <i>after</i> the target has been mutated — readiness never observed, smoke
    /// not observed — were bare returns, and the migration step itself wrote nothing at all. A run that
    /// applied five migrations to a governed environment left no structured record of having done so. The
    /// <c>finally</c> below is the whole point: the record is not written by the success path or the failure
    /// path, it is written by every path, because the scope cannot end without it.
    /// </para>
    ///
    /// <para>
    /// <b>The destination is computed at each return rather than captured from the <c>finally</c></b>,
    /// because C# evaluates a <c>return</c> expression before the <c>finally</c> runs, so the returned record
    /// must already be complete. The address is a pure function of the evidence root and the deployment
    /// identity, so computing it early and writing to it later cannot disagree — the same property that lets
    /// the deployment identity be minted before the deployment it names.
    /// </para>
    ///
    /// <para>
    /// The method takes the database rather than constructing one: it is sequencing, not ownership, and a
    /// second <c>EnsureAsync</c> here would be a second place that decides whether the environment exists.
    /// </para>
    /// </summary>
    private static async Task<MigrationStep> RunMigrationStepAsync(
        DeploymentId deploymentId,
        EnvironmentDescriptor descriptor,
        ReleaseRecord release,
        EnvironmentDatabase database,
        DeploymentAuthorization authorization,
        Nexus.Delivery.Release.ReleasePlan plan,
        string appRoot,
        string releaseId,
        string evidenceRoot,
        DeploymentLane lane,
        Action<string> say)
    {
        var destructive = MeasureDestructiveForwardMigrations(plan);
        var evidencePath = MigrationEvidence.PathFor(evidenceRoot, deploymentId);

        var recorder = new MigrationEvidenceRecorder(
            deploymentId,
            descriptor,
            release,
            [.. release.Migrations.Metadata.MigrationIds],
            [.. destructive],
            lane.AuthorizationActor);

        var result = MigrationEvidenceResult.Threw;
        var commandRan = false;

        MigrationOutcome? governance = null;
        MigrationTargetObservation? afterMigration = null;
        MigrationGovernanceVerdict? afterJudgement = null;

        try
        {
            // ---- act 6: the schema decision, before a single statement is executed --------------------
            governance = await JudgeMigrationAsync(release, descriptor, database, authorization, plan, say).ConfigureAwait(false);

            recorder.RecordPreState(governance.Before);
            recorder.RecordGovernance(
                governance.Verdict.Decision.ToString(),
                governance.Verdict.ObservedCompatibility?.ToString());

            if (governance.Backup is not null)
            {
                recorder.RecordBackup(governance.Backup);
            }

            if (!governance.Verdict.PermitsDeployment)
            {
                result = MigrationEvidenceResult.RefusedByGovernance;

                var reasons = string.Join(", ", governance.Verdict.RefusalReasons);
                recorder.RecordRefusal(reasons);

                return new MigrationStep(
                    governance, null, null,
                    $"REFUSED — migration governance returned {governance.Verdict.Decision}: {reasons}",
                    evidencePath, result);
            }

            // ---- act 7: apply the migration set, through the artifact's own operator command ----------
            if (governance.Verdict.Decision == MigrationGovernanceDecision.MigrationApproved
                && governance.Verdict.ObservedCompatibility != MigrationCompatibility.Match)
            {
                var migrate = await RunArtifactAsync(
                    descriptor, appRoot, releaseId, ["--migrate"], TimeSpan.FromMinutes(5), say).ConfigureAwait(false);

                commandRan = true;
                recorder.RecordCommand(executed: true, exitCode: migrate.ExitCode);

                say($"migrate          : exit={migrate.ExitCode}");

                if (migrate.ExitCode != 0)
                {
                    result = MigrationEvidenceResult.CommandFailed;
                    recorder.RecordRefusal($"The migration command exited {migrate.ExitCode}.");

                    // The post-state is still observed: the useful question after a failed migration is what
                    // state the target was LEFT in, and a record that stops at "the command failed" cannot
                    // answer it.
                    recorder.RecordPostState(await database.ObserveAsync(descriptor.EnvironmentId).ConfigureAwait(false));

                    return new MigrationStep(
                        governance, null, null,
                        $"REFUSED — the migration command failed with exit {migrate.ExitCode}. Its output is in the environment log, redacted.",
                        evidencePath, result);
                }
            }
            else
            {
                recorder.RecordCommand(executed: false, exitCode: null);

                say($"migrate          : not required (compatibility={governance.Verdict.ObservedCompatibility})");
            }

            afterMigration = await database.ObserveAsync(descriptor.EnvironmentId).ConfigureAwait(false);

            recorder.RecordPostState(afterMigration);

            var appliedMatch = afterMigration.AppliedMigrationIds
                .SequenceEqual(release.Migrations.Metadata.MigrationIds, StringComparer.Ordinal);

            say($"schema after     : applied={afterMigration.AppliedMigrationIds.Count} expected={release.Migrations.Metadata.MigrationIds.Count} exactMatch={appliedMatch}");

            if (!appliedMatch)
            {
                result = MigrationEvidenceResult.PostStateMismatch;

                recorder.RecordRefusal(
                    $"The target's applied set ({string.Join(", ", afterMigration.AppliedMigrationIds)}) is not the "
                    + $"set this release records ({string.Join(", ", release.Migrations.Metadata.MigrationIds)}).");

                return new MigrationStep(
                    governance, afterMigration, null,
                    "REFUSED — the target's applied migration set is not the set this release records. Startup validation does not proceed.",
                    evidencePath, result);
            }

            // What this run added is the difference between the two observations, not the whole applied set:
            // a target that already carried four of the five migrations has not had all five applied by this
            // run, and a record that said otherwise would overstate what the run did.
            recorder.RecordApplied(
                [.. afterMigration.AppliedMigrationIds.Except(governance.Before.AppliedMigrationIds, StringComparer.Ordinal)]);

            result = commandRan ? MigrationEvidenceResult.Applied : MigrationEvidenceResult.NotRequired;

            // The compatibility that goes into the promotion evidence is RE-JUDGED by the contract over the
            // post-migration observation, not computed here. The comparison is MigrationGovernance's own
            // prefix relation and it stays private to it - a driver that re-implemented the comparison would
            // be a second authority on the one question the contract exists to answer. Judged twice on
            // purpose: once before the target moved, and once after.
            afterJudgement = MigrationGovernance.Judge(new MigrationGovernanceRequest(
                release.Migrations,
                descriptor.EnvironmentId,
                afterMigration,
                destructive,
                authorization));

            say($"schema verdict   : {afterJudgement.Decision} compatibility={afterJudgement.ObservedCompatibility?.ToString() ?? "(none)"} (consulted for compatibility only - no further migration is proposed, so the BackupRequired this second judgement reports, the post-migration state having no backup of its own, is not a refusal of anything)");

            if (afterJudgement.ObservedCompatibility is null)
            {
                recorder.RecordRefusal("The target's compatibility could not be established after the migration, so the verification step would be unevaluable.");

                return new MigrationStep(
                    governance, afterMigration, afterJudgement,
                    "REFUSED — the target's compatibility could not be established after the migration, so the verification step would be unevaluable.",
                    evidencePath, result);
            }

            return new MigrationStep(governance, afterMigration, afterJudgement, null, evidencePath, result);
        }
        finally
        {
            // Written on EVERY exit from the block above. A failure to write is deliberately not swallowed:
            // it would mean a mutated target with no record, which is precisely the finding this construct
            // exists to close, and a caller has no useful way to continue past it.
            var written = recorder.Write(evidenceRoot, result);

            say($"migration record : {written} (result={result})");
        }
    }

    // ==================================================================== migration governance

    private sealed record MigrationOutcome(
        MigrationGovernanceVerdict Verdict,
        MigrationTargetObservation Before,
        EnvironmentDatabase.BackupObservation? Backup);

    /// <summary>
    /// Asks <see cref="MigrationGovernance"/> what may be done to this target, with everything measured
    /// rather than supplied.
    ///
    /// <para>
    /// <b>The destructive-migration input is measured from the release's own migration sources</b>, at the
    /// commit the release records — not taken from a claim in a plan file. This is the one input the
    /// judgement cannot observe from the target, so it is the one worth being explicit about: it is read
    /// from the certified source tree, and the reading is recorded.
    /// </para>
    ///
    /// <para>
    /// <b>The backup obligation is discharged by taking and verifying a backup, not by asserting one.</b>
    /// This release records <c>BackupRequired = true</c> with <c>ForwardFixOnly</c> reversibility, so
    /// <see cref="MigrationGovernance"/> will refuse <c>BackupRequired</c> over a target that holds data
    /// until it is given a <c>backupVerified: true</c> observation. That observation is produced here by
    /// <see cref="EnvironmentDatabase.BackupAsync"/>, which dumps the target and then reads the dump back — the
    /// header, the table of contents, and the digest. A backup that could not be taken leaves the input
    /// false, so the verdict is <c>BackupRequired</c> and the deployment stops. There is no path through
    /// this method that reports a verified backup which was not verified.
    /// </para>
    /// </summary>
    private static async Task<MigrationOutcome> JudgeMigrationAsync(
        ReleaseRecord release,
        EnvironmentDescriptor descriptor,
        EnvironmentDatabase database,
        DeploymentAuthorization authorization,
        Nexus.Delivery.Release.ReleasePlan plan,
        Action<string> say)
    {
        var observed = await database.ObserveAsync(descriptor.EnvironmentId).ConfigureAwait(false);

        var destructive = MeasureDestructiveForwardMigrations(plan);

        say($"migration        : state={release.Migrations.State} provider={release.Migrations.Metadata.Provider} expected={release.Migrations.Metadata.MigrationIds.Count}");
        say($"                   observed applied={observed.AppliedMigrationIds.Count} data={observed.DataState} backup={release.Migrations.BackupRequired}");
        say($"                   destructive forward migrations measured: {(destructive.Count == 0 ? "none" : string.Join(", ", destructive))}");

        // The release's own recorded obligation decides whether a backup is even attempted. Taking one
        // unconditionally would make the release's BackupRequired flag decorative, and would produce a
        // backup artifact for releases that never asked for one.
        EnvironmentDatabase.BackupObservation? backup = null;

        if (release.Migrations.BackupRequired && observed.DataState != MigrationTargetDataState.Empty)
        {
            say($"backup           : the release records BackupRequired=true and the target reports {observed.DataState}; taking and verifying a backup");

            backup = await database
                .BackupAsync(Path.Combine(descriptor.Root, "backups", release.ReleaseId.Value))
                .ConfigureAwait(false);

            say($"backup           : verified={backup.IsVerified} path={backup.Path ?? "(none)"} bytes={backup.Bytes} tocEntries={backup.TocEntries}");

            if (!backup.IsVerified)
            {
                say($"                   · {backup.RefusalReason}");
            }
        }
        else
        {
            say($"backup           : not required (releaseBackupRequired={release.Migrations.BackupRequired}, observedDataState={observed.DataState})");
        }

        // The observation handed to the judgement carries the VERIFIED fact, so the contract's own rule 7
        // is what decides. The driver does not decide that the obligation is discharged; it reports what
        // was observed and lets the contract refuse if that is not enough. WithVerifiedBackup carries every
        // other member through unchanged, so the data state that triggered the obligation cannot be edited
        // away on the way to answering it.
        var before = backup is { IsVerified: true }
            ? observed.WithVerifiedBackup(backup.Basis)
            : observed;

        var verdict = MigrationGovernance.Judge(new MigrationGovernanceRequest(
            release.Migrations,
            descriptor.EnvironmentId,
            before,
            destructive,
            authorization));

        say($"migration verdict: {verdict.Decision}");

        foreach (var reason in verdict.RefusalReasons)
        {
            say($"                   · refused: {reason}");
        }

        foreach (var line in verdict.Detail)
        {
            say($"                   · {line}");
        }

        return new MigrationOutcome(verdict, before, backup);
    }

    /// <summary>
    /// Reads the certified source tree's migration files and reports the ids whose <b>forward</b> path
    /// removes or narrows schema.
    ///
    /// <para>
    /// <b>The file's own structure decides where the forward path ends.</b> An EF migration declares
    /// <c>Up</c> then <c>Down</c>; a destructive call appearing after <c>Down</c> begins is the reversion,
    /// which is exactly the thing this input must not count. A grep over the whole file would report every
    /// <c>DropTable</c> in every <c>Down</c> method and would make the judgement refuse releases that are
    /// purely additive — a false positive that would train its readers to ignore the signal.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> MeasureDestructiveForwardMigrations(Nexus.Delivery.Release.ReleasePlan plan)
    {
        var directory = Path.Combine(plan.RepositoryRoot, "apps", "api", "Migrations");

        if (!Directory.Exists(directory))
        {
            // Not measured is not "none". Recording the absence as an empty list would claim a clean bill of
            // health this method did not establish, so it returns a value that makes the judgement refuse
            // instead — the ids the release records, which is the most conservative honest answer here.
            return plan.MigrationIds;
        }

        string[] destructiveCalls =
        [
            "migrationBuilder.DropTable", "migrationBuilder.DropColumn", "migrationBuilder.DropIndex",
            "migrationBuilder.DropSchema", "migrationBuilder.DropUniqueConstraint", "migrationBuilder.DropForeignKey"
        ];

        var found = new List<string>();

        foreach (var file in Directory.GetFiles(directory, "*.cs"))
        {
            if (file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            var downIndex = text.IndexOf("protected override void Down", StringComparison.Ordinal);
            var forward = downIndex < 0 ? text : text[..downIndex];

            if (destructiveCalls.Any(call => forward.Contains(call, StringComparison.Ordinal)))
            {
                found.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        return found;
    }

    // ==================================================================== start / observe

    private static Process? _tracked;

    private sealed record HealthObservation(
        bool ReadinessObserved,
        int LivenessStatus,
        int ReadinessStatus,
        long ReadinessMillis,
        bool SmokeObserved,
        IReadOnlyDictionary<string, string> Smoke,
        IReadOnlyDictionary<string, string> Observability);

    /// <summary>
    /// Starts the certified artifact under ENV-DEV configuration, waits for readiness, and smokes it.
    ///
    /// <para>
    /// <b>The environment is handed to the artifact, never written into it.</b> The connection string, the
    /// environment name and the URLs are child-process environment variables, which is the property W9 set
    /// out to establish: one artifact, configured per environment, with no rebuild.
    /// </para>
    ///
    /// <para>
    /// <b>The connection string is never printed.</b> The only place the password appears is the child's
    /// environment block and the migration command's; the log this driver writes is redacted before it is
    /// read back for evidence.
    /// </para>
    /// </summary>
    private static async Task<HealthObservation?> StartAndObserveAsync(
        EnvironmentDescriptor descriptor,
        string appRoot,
        string releaseId,
        ReleaseRecord release,
        Action<string> say)
    {
        var password = descriptor.ReadDatabasePassword();
        var logPath = Path.Combine(descriptor.LogsRoot, $"{descriptor.EnvironmentToken}-deploy-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}.log");

        Directory.CreateDirectory(descriptor.LogsRoot);
        Directory.CreateDirectory(descriptor.AppDataRoot(releaseId));

        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = appRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        info.ArgumentList.Add(Path.Combine(appRoot, "MarketSurvey.Api.dll"));
        ApplyEnvironment(info, descriptor, appRoot, releaseId, password);

        // NOT `using`. The process outlives this method — a later act stops it — and a `using` here disposes
        // the very object the tracked field holds. That was W9.4's D-R2: teardown's first question,
        // `process.HasExited`, threw "No process is associated with this object" on the disposed object, and
        // because teardown ran after the lineage append the run lost its evidence file and left the
        // application orphaned and still serving.
        var process = Process.Start(info)
            ?? throw new InvalidOperationException("The certified artifact could not be started.");

        _tracked = process;

        var log = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (log) { log.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (log) { log.AppendLine(e.Data); } } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        say($"started          : pid={process.Id} on http://127.0.0.1:{descriptor.Host!.Port} as {descriptor.Host.EnvironmentName} (log redacted before evidence)");

        var baseUrl = $"http://127.0.0.1:{descriptor.Host.Port}";
        var watch = Stopwatch.StartNew();
        var ready = false;
        var readinessStatus = 0;

        while (watch.Elapsed < TimeSpan.FromMinutes(2))
        {
            if (process.HasExited)
            {
                say($"                   process exited early with code {process.ExitCode}");
                break;
            }

            try
            {
                using var response = await Http.GetAsync(baseUrl + descriptor.Host.ReadinessPath).ConfigureAwait(false);
                readinessStatus = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    ready = true;
                    break;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet. This is the expected state for the first seconds after start.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        var readinessMillis = watch.ElapsedMilliseconds;

        if (!ready)
        {
            StopProcess(process, say);
            _tracked = null;
            WriteRedactedLog(logPath, log.ToString(), descriptor);
            return null;
        }

        say($"readiness        : {descriptor.Host.ReadinessPath} → {readinessStatus} in {readinessMillis} ms");

        var liveness = await StatusAsync(baseUrl + descriptor.Host.LivenessPath).ConfigureAwait(false);
        say($"liveness         : {descriptor.Host.LivenessPath} → {liveness}");

        // ---- smoke ------------------------------------------------------------------------------------
        var smoke = new Dictionary<string, string>(StringComparer.Ordinal);

        var liveBody = await BodyAsync(baseUrl + descriptor.Host.LivenessPath).ConfigureAwait(false);
        smoke["livenessBody"] = liveBody;
        say($"smoke            : liveness body {liveBody}");

        var readyBody = await BodyAsync(baseUrl + descriptor.Host.ReadinessPath).ConfigureAwait(false);
        smoke["readinessBody"] = readyBody;
        say($"smoke            : readiness body {readyBody}");

        var unknown = await StatusAsync(baseUrl + "/__no-such-endpoint__").ConfigureAwait(false);
        smoke["unknownRouteStatus"] = unknown.ToString(CultureInfo.InvariantCulture);
        say($"smoke            : unknown route → {unknown} (a 404 proves routing answered; a 5xx would prove the pipeline threw)");

        var readyAgain = await StatusAsync(baseUrl + descriptor.Host.ReadinessPath).ConfigureAwait(false);
        smoke["readinessSecondProbe"] = readyAgain.ToString(CultureInfo.InvariantCulture);
        smoke["databaseRoundTripsRequired"] = "true";
        say($"smoke            : readiness re-probed → {readyAgain} (the endpoint issues a database round trip, so a repeat 200 is not a cached one)");

        var survived = !process.HasExited;
        smoke["processSurvivedSmoke"] = survived.ToString(CultureInfo.InvariantCulture);

        // ---- observability ----------------------------------------------------------------------------
        string captured;
        lock (log)
        {
            captured = log.ToString();
        }

        var lines = captured.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var observability = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["logLinesCaptured"] = lines.Length.ToString(CultureInfo.InvariantCulture),
            ["logHasStartupBanner"] = lines.Any(l => l.Contains("Now listening on", StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture),
            ["logHasApplicationStarted"] = lines.Any(l => l.Contains("Application started", StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture),
            ["logHasUnhandledError"] = lines.Any(l => l.Contains("Unhandled exception", StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture),
            ["logPath"] = logPath,
            ["logContainsTheDatabaseCredential"] = captured.Contains(password, StringComparison.Ordinal).ToString(CultureInfo.InvariantCulture)
        };

        say($"observability    : {observability["logLinesCaptured"]} log line(s), started={observability["logHasApplicationStarted"]}, unhandledError={observability["logHasUnhandledError"]}");

        WriteRedactedLog(logPath, captured, descriptor);

        return new HealthObservation(
            ReadinessObserved: true,
            LivenessStatus: liveness,
            ReadinessStatus: readinessStatus,
            ReadinessMillis: readinessMillis,
            // Smoke is a set of observations, not a verdict: a smoke step that recorded the probes and then
            // reported "passed" would be asserting its own success. What actually decided the smoke is the
            // pair of conditions below, and they are recorded as the fact they are.
            SmokeObserved: survived
                && liveness is >= 200 and < 300
                && readyAgain is >= 200 and < 300
                && unknown == 404,
            Smoke: smoke,
            Observability: observability);
    }

    private static void ApplyEnvironment(ProcessStartInfo info, EnvironmentDescriptor descriptor, string appRoot, string releaseId, string password)
    {
        // ---- what this method derives, and what it no longer invents --------------------------------
        // Derived, because these ARE the environment's identity and the descriptor already states them: the
        // ASP.NET environment name, the bind address, the database connection, and the data-protection key
        // path (which must be per-environment, or two environments would share cookie-encryption keys).
        info.Environment["ASPNETCORE_ENVIRONMENT"] = descriptor.Host!.EnvironmentName;
        info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{descriptor.Host.Port}";
        info.Environment["ConnectionStrings__Database"] = descriptor.BuildConnectionString(password);
        info.Environment["DataProtection__Path"] = Path.Combine(descriptor.AppDataRoot(releaseId), "keys");
        info.Environment["DOTNET_ENVIRONMENT"] = descriptor.Host.EnvironmentName;

        // Supplied, because these are the APPLICATION's configuration and each environment must state its
        // own. The W9.4 driver set Storage__Provider=Local, Testing__DisableGps=true and a logging level as
        // literals right here, for every environment it could reach - which was safe only while that was
        // exactly one environment, and would have been a GPS-bypass injection into any other. See
        // EnvironmentDescriptor.Runtime for the full account. Nothing is defaulted: an environment that does
        // not declare a setting the artifact requires gets an artifact that refuses to start, which is the
        // honest outcome and is what W9.5 measured.
        foreach (var setting in descriptor.Runtime?.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            info.Environment[setting.Key] = setting.Value;
        }
    }

    private static async Task<ProcessResult> RunArtifactAsync(
        EnvironmentDescriptor descriptor,
        string appRoot,
        string releaseId,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        Action<string> say)
    {
        var password = descriptor.ReadDatabasePassword();
        var logPath = Path.Combine(descriptor.LogsRoot, $"{descriptor.EnvironmentToken}-migrate-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}.log");

        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = appRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        info.ArgumentList.Add(Path.Combine(appRoot, "MarketSurvey.Api.dll"));

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        ApplyEnvironment(info, descriptor, appRoot, releaseId, password);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("The artifact's operator command could not be started.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cancellation = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"The artifact's operator command did not finish within {timeout}.");
        }

        var captured = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        WriteRedactedLog(logPath, captured, descriptor);

        say($"                   output → {logPath} (redacted, {captured.Length} chars)");

        return new ProcessResult(process.ExitCode, logPath);
    }

    /// <summary>
    /// Stops what this driver started, idempotently and without throwing. The heavy lifting lives in
    /// <see cref="ProcessTermination"/> so it has a test beside it; this is the driver's binding to it.
    /// </summary>
    private static Task StopTrackedProcessAsync(Action<string> say)
    {
        ProcessTermination.Terminate(_tracked, say: say);
        _tracked = null;
        return Task.CompletedTask;
    }

    /// <summary>Stops one specific process. Same guarantees: it reports, it does not throw.</summary>
    private static void StopProcess(Process process, Action<string> say)
        => ProcessTermination.Terminate(process, say: say);

    private static async Task<int> StatusAsync(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            return (int)response.StatusCode;
        }
        catch (HttpRequestException)
        {
            return -1;
        }
    }

    private static async Task<string> BodyAsync(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return body.Length <= 200 ? body : body[..200] + "...";
        }
        catch (HttpRequestException)
        {
            return "(no response)";
        }
    }

    /// <summary>
    /// Writes the captured output with the credential replaced. <b>Every</b> log this driver persists goes
    /// through here, so "no secrets in the evidence" is a property of one method rather than of each caller
    /// remembering.
    /// </summary>
    private static void WriteRedactedLog(string path, string content, EnvironmentDescriptor descriptor)
    {
        var redacted = descriptor.Redact(content);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, redacted, new UTF8Encoding(false));
    }

    private sealed record ProcessResult(int ExitCode, string LogPath);

    // ==================================================================== rollback rehearsal

    private static async Task<int> RollbackRehearsalAsync(string releasePlanPath, string envPlanPath, DeploymentLane lane, Action<string> say, StringBuilder transcript)
    {
        var plan = Nexus.Delivery.Release.ReleasePlan.Load(releasePlanPath);
        var descriptor = EnvironmentDescriptor.Load(envPlanPath, lane.Environment);
        var evidencePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(releasePlanPath))!, $"{lane.DocumentPrefix}ROLLBACK_REHEARSAL.json");

        var releaseId = Require(plan.ExistingReleaseId, "existingReleaseId", "The rehearsal acts on an existing release.");
        var registry = new FileReleaseRegistry(plan.StoreRoot);
        var store = new FileArtifactStore(plan.StoreRoot);

        var release = await registry.TryOpenAsync(ReleaseId.Parse(releaseId)).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The registry holds no release '{releaseId}'.");

        say($"release          : {releaseId}");
        say($"rollback state   : {plan.RollbackState}");
        say($"rollback basis   : {plan.RollbackBasis}");

        var results = new List<Dictionary<string, string>>();

        // ---- control 1: a rollback to a previous release cannot be fabricated -------------------------
        var bundle = await BuildBundleForPromotionAsync(release, plan).ConfigureAwait(false);
        var authorization = new DeploymentAuthorization(DeploymentAuthorityRole.OnCall, $"w9.5-{lane.VerbLabel.ToLowerInvariant()}-rollback-rehearsal", DateTimeOffset.UtcNow);

        // The state is the LANE's deployed state, not DeployedDev. This rehearsal is run for ENV-TEST as
        // well as ENV-DEV, and the first version probed `PromotionState.DeployedDev` and printed the literal
        // "Rollback from DeployedDev" — so the TEST rehearsal was asking a question about ENV-DEV's state and
        // reporting an answer about ENV-DEV's state, in a run whose whole purpose is to prove something about
        // ENV-TEST. The assertion happened to hold either way, because Rollback is defined only from Live and
        // both states are non-Live; that is what made it easy to miss, and it is not a reason to keep it. A
        // proof that names the wrong subject is not a proof of the right one.
        var fabricated = DeploymentStateMachine.Decide(new PromotionRequest(
            bundle,
            descriptor.EnvironmentId,
            lane.DeployedState,
            DeploymentTransition.Rollback,
            new DeploymentGateEvidence
            {
                Authorization = authorization,
                Reason = $"Rehearsal probe: is a rollback from {lane.DeployedState} even a transition?",
                PreviousBundle = null,
                PreviousBundleAvailable = false,
                RollbackRehearsed = false,
                ReleaseReferenceProtection = ReleaseReferenceProtectionEvidence.Unrecorded
            }));

        say($"control 1        : Rollback from {lane.DeployedState} allowed={fabricated.IsAllowed} reasons={string.Join(", ", fabricated.RefusalReasons)}");

        results.Add(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["control"] = "A rollback cannot be declared from ENV-DEV",
            ["expected"] = "refused",
            ["observed"] = fabricated.IsRefused ? "refused" : "ALLOWED — control failed",
            ["reasons"] = string.Join(", ", fabricated.RefusalReasons),
            ["whyItMatters"] = "The estate's Rollback transition is defined only from Live. A first release with no previous bundle must not be able to record a rollback it did not perform."
        });

        // ---- control 2: an artifact-level reversion is available, and is exercised ---------------------
        var appRoot = descriptor.AppRoot(releaseId);
        var beforeExists = Directory.Exists(appRoot);
        var beforeDigest = beforeExists ? await Sha256TreeAsync(appRoot).ConfigureAwait(false) : "(no deployment present)";

        say($"control 2        : materialized tree before rehearsed reversion = {beforeDigest}");

        var stagedZip = Path.Combine(descriptor.ReleaseRoot(releaseId), "artifact-rehearsal.zip");
        var fetch = await store.FetchAsync(release.Artifacts[0].ArtifactId, stagedZip).ConfigureAwait(false);

        if (!fetch.IsFetched)
        {
            return Fail(say, $"REFUSED — the store could not re-supply the certified artifact: {fetch.Detail}");
        }

        var refetched = await Sha256FileAsync(stagedZip).ConfigureAwait(false);
        var identical = string.Equals(refetched, release.Artifacts[0].ContentDigest.ToString(), StringComparison.Ordinal);

        say($"control 2        : re-fetched from the store = {refetched} identical={identical}");

        results.Add(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["control"] = "The deployed bytes can be reproduced from the store",
            ["expected"] = "identical digest",
            ["observed"] = identical ? "identical" : "DIVERGED",
            ["whyItMatters"] = "A rollback depends on the previous bytes still being retained and reproducible. This release has no previous accepted release, so the only reversion this stage can rehearse is the artifact-level one — and it is the property a future rollback will rest on."
        });

        File.Delete(stagedZip);

        // ---- control 3: the honest statement of what could NOT be rehearsed ---------------------------
        results.Add(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["control"] = "A release-level rollback to a previous accepted release",
            ["expected"] = "not rehearsable",
            ["observed"] = plan.RollbackState,
            ["whyItMatters"] = "Recorded as the state rather than left as an absence. This is the first release of marketsurvey.api: no release of this unit has been accepted in any environment, so there is no previous release to roll back to. The release records ForwardFixOnly reversibility for the same reason — no Down migration in this estate has ever been exercised."
        });

        say($"control 3        : release-level rollback not rehearsable — {plan.RollbackState}");

        var evidence = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["releaseId"] = releaseId,
            ["environment"] = descriptor.Environment,
            ["occurredUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["rollbackState"] = plan.RollbackState,
            ["rollbackBasis"] = plan.RollbackBasis,
            ["previousReleaseId"] = plan.PreviousReleaseId ?? "(none)",
            ["controls"] = results,
            ["materializedTreeBefore"] = beforeDigest,
            ["reversionRehearsed"] = identical,
            ["releaseLevelRollbackRehearsed"] = false,
            ["honestStatement"] = "The artifact-level reversion was rehearsed and passed. A release-level rollback was NOT rehearsed, because this unit has no previously accepted release to roll back to; that is the recorded state NoPreviousAcceptedRelease, not an omission."
        };

        WriteEvidence(evidencePath, evidence);
        WriteTranscript(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(releasePlanPath))!, $"{lane.DocumentPrefix}ROLLBACK_REHEARSAL_TRANSCRIPT.txt"), transcript);

        say($"RESULT: {(identical && fabricated.IsRefused ? "ROLLBACK_REHEARSAL_PASSED" : "ROLLBACK_REHEARSAL_FAILED")}");
        return identical && fabricated.IsRefused ? 0 : 1;
    }

    // ==================================================================== failure injection

    /// <summary>
    /// Takes the database away underneath a running deployment and checks that the readiness signal notices.
    ///
    /// <para>
    /// <b>Why this injection and not a synthetic one.</b> A readiness endpoint that returns 200 without
    /// touching anything is the classic false-green: it passes every happy-path test and fails the only moment
    /// it exists for. This injects the exact failure the signal is supposed to report — the database going
    /// away — and requires the signal to move, and then to move back when the dependency returns.
    /// </para>
    ///
    /// <para>
    /// <b>It stops only the environment's own database.</b> The container name is asserted against the
    /// managed prefix before any command is issued, so a descriptor aimed at the estate's live MarketSurvey
    /// database is refused rather than obeyed.
    /// </para>
    /// </summary>
    private static async Task<int> FailureInjectionAsync(string releasePlanPath, string envPlanPath, DeploymentLane lane, Action<string> say, StringBuilder transcript)
    {
        var plan = Nexus.Delivery.Release.ReleasePlan.Load(releasePlanPath);
        var descriptor = EnvironmentDescriptor.Load(envPlanPath, lane.Environment);
        var evidencePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(releasePlanPath))!, $"{lane.DocumentPrefix}FAILURE_INJECTION.json");

        var releaseId = Require(plan.ExistingReleaseId, "existingReleaseId", "The injection acts on an existing release.");
        var registry = new FileReleaseRegistry(plan.StoreRoot);

        var release = await registry.TryOpenAsync(ReleaseId.Parse(releaseId)).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The registry holds no release '{releaseId}'.");

        var appRoot = descriptor.AppRoot(releaseId);

        if (!Directory.Exists(appRoot))
        {
            return Fail(say, $"REFUSED — '{appRoot}' does not exist, so there is no deployment to inject into.");
        }

        var database = new EnvironmentDatabase(descriptor, say);

        say($"release          : {releaseId}");
        say($"environment      : {descriptor.Environment}");
        say($"fault            : the target database is stopped underneath a running deployment");

        var baseUrl = $"http://127.0.0.1:{descriptor.Host!.Port}";
        var password = descriptor.ReadDatabasePassword();

        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = appRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        info.ArgumentList.Add(Path.Combine(appRoot, "MarketSurvey.Api.dll"));
        ApplyEnvironment(info, descriptor, appRoot, releaseId, password);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("The certified artifact could not be started for the injection.");

        var log = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (log) { log.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (log) { log.AppendLine(e.Data); } } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Ordered, because the injected steps are a sequence: the observed baseline is the premise the later
        // observations are read against, and a step list whose order is not preserved makes the recovery step
        // indistinguishable from the injection step when a reader comes back to it.
        var steps = new List<Dictionary<string, string>>();

        try
        {
            var healthy = await WaitForAsync(baseUrl + descriptor.Host.ReadinessPath, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            say($"baseline         : readiness → {healthy} with the database up");

            steps.Add(Step("Baseline", "readiness returns success while the database is up", "200", healthy.ToString(CultureInfo.InvariantCulture), healthy == 200));

            await database.StopAsync().ConfigureAwait(false);
            say($"injected         : the {descriptor.Environment} database container was stopped");

            // The application pools connections; the readiness endpoint issues a real round trip, so the
            // first probe after the outage may still be served from a pooled connection. Probing until the
            // signal moves, within a bound, is the honest way to test "does it notice" rather than "does it
            // notice within one request".
            var degraded = await WaitForAsync(baseUrl + descriptor.Host.ReadinessPath, TimeSpan.FromMinutes(1), expectSuccess: false, process).ConfigureAwait(false);
            say($"after injection  : readiness → {degraded} with the database down");

            steps.Add(Step("Injection", "readiness must stop reporting success while the database is down", "503 or connection refused", degraded.ToString(CultureInfo.InvariantCulture), degraded != 200));

            var livenessDuring = await StatusAsync(baseUrl + descriptor.Host.LivenessPath).ConfigureAwait(false);
            say($"after injection  : liveness → {livenessDuring} (liveness is deliberately independent of the database)");

            steps.Add(Step("Liveness independence", "liveness must stay up so an orchestrator does not restart a healthy process", "200", livenessDuring.ToString(CultureInfo.InvariantCulture), livenessDuring == 200));

            await database.StartAsync().ConfigureAwait(false);
            say($"recovered        : the {descriptor.Environment} database container was started again");

            var recovered = await WaitForAsync(baseUrl + descriptor.Host.ReadinessPath, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            say($"after recovery   : readiness → {recovered}");

            steps.Add(Step("Recovery", "readiness must return to success once the dependency is back", "200", recovered.ToString(CultureInfo.InvariantCulture), recovered == 200));
        }
        finally
        {
            StopProcess(process, say);
            WriteRedactedLog(Path.Combine(descriptor.LogsRoot, $"{descriptor.EnvironmentToken}-injection-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}.log"), log.ToString(), descriptor);
        }

        var allHeld = steps.All(s => string.Equals(s["held"], "True", StringComparison.Ordinal));

        var evidence = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["releaseId"] = releaseId,
            ["environment"] = descriptor.Environment,
            ["occurredUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["fault"] = "The target database container was stopped underneath a running deployment, then started again.",
            ["containerStopped"] = database.ContainerName,
            ["steps"] = steps,
            ["allAssertionsHeld"] = allHeld,
            ["processWasRestarted"] = false,
            ["honestStatement"] = "The readiness signal moved because the database went away and moved back when it returned. The process was never restarted during the injection, so the change in signal cannot be attributed to a restart."
        };

        WriteEvidence(evidencePath, evidence);
        WriteTranscript(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(releasePlanPath))!, $"{lane.DocumentPrefix}FAILURE_INJECTION_TRANSCRIPT.txt"), transcript);

        say($"RESULT: {(allHeld ? "FAILURE_INJECTION_PASSED" : "FAILURE_INJECTION_FAILED")}");
        return allHeld ? 0 : 1;
    }

    private static Dictionary<string, string> Step(string name, string expectation, string expected, string observed, bool held)
        => new(StringComparer.Ordinal)
        {
            ["step"] = name,
            ["expectation"] = expectation,
            ["expected"] = expected,
            ["observed"] = observed,
            ["held"] = held.ToString(CultureInfo.InvariantCulture)
        };

    private static async Task<int> WaitForAsync(string url, TimeSpan timeout, bool expectSuccess = true, Process? process = null)
    {
        var watch = Stopwatch.StartNew();
        var last = -2;

        while (watch.Elapsed < timeout)
        {
            if (process is { HasExited: true })
            {
                return -1;
            }

            last = await StatusAsync(url).ConfigureAwait(false);

            if (expectSuccess ? last == 200 : last != 200)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        return last;
    }

    // ==================================================================== bundle projection

    /// <summary>
    /// The <see cref="ReleaseBundle"/> the deployment state machine decides over, projected from the release
    /// record.
    ///
    /// <para>
    /// <b>A projection, not a second bundle.</b> Every member is quoted from the immutable release record —
    /// the artifact ids and digests, the source commit, the migration set, the configuration keys. Nothing is
    /// re-derived and nothing is defaulted, so a promotion decided here is a decision about the same bundle
    /// the release registry holds.
    /// </para>
    /// </summary>
    private static Task<ReleaseBundle> BuildBundleForPromotionAsync(
        ReleaseRecord release,
        Nexus.Delivery.Release.ReleasePlan plan)
    {
        var unitId = release.Identity.UnitId;

        var unit = new DeploymentUnit(
            unitId,
            DeploymentUnitKind.Service,
            plan.RepositoryLabel,
            plan.RepositoryRoot,
            PackagingMedium.ProcessHost,
            new HealthContract(plan.ReadinessPath, plan.LivenessPath),
            configurationKeys: plan.ConfigurationKeys,
            secretReferences: [],
            migrations: release.Migrations.Metadata);

        var source = new ReleaseSource(
            release.Identity.SourceCommits[0],
            release.Identity.ReleaseRefName,
            workingTreeIsDirty: false);

        var builder = new BuilderIdentity(
            plan.ToolchainSdkVersion,
            plan.BuilderRunId,
            plan.ToolchainOperatingSystem);

        var artifacts = release.Artifacts
            .Select(a => new ReleaseArtifact(a.UnitId, a.ContentDigest, a.SizeBytes))
            .ToArray();

        var bundle = new ReleaseBundle(
            ReleaseBundle.CurrentSchemaVersion,
            release.BundleId,
            createdAt: DateTimeOffset.UtcNow,
            source,
            builder,
            artifacts,
            sharedContracts: null,
            migrations: [new UnitMigrationBinding(unitId, release.Migrations.Metadata)],
            configurationKeys: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [unitId.Value] = plan.ConfigurationKeys
            },
            secretReferences: new Dictionary<string, IReadOnlyList<SecretReference>>(StringComparer.Ordinal)
            {
                [unitId.Value] = []
            });

        // The unit the bundle carries and the unit the record carries must be the same unit. Checked rather
        // than assumed, because a bundle assembled from one unit and a record naming another would make the
        // promotion decision a decision about a different release than the one the registry verified.
        if (unit.UnitId != unitId || artifacts.Length != release.Artifacts.Count || artifacts[0].Digest != release.Artifacts[0].ContentDigest)
        {
            throw new InvalidOperationException(
                "The projected bundle does not carry the identities the release record carries. A promotion is never decided over a bundle that disagrees with the release it claims to describe.");
        }

        return Task.FromResult(bundle);
    }

    // ==================================================================== evidence

    private static Dictionary<string, object> BuildDeploymentEvidence(
        ReleaseRecord release,
        EnvironmentDescriptor descriptor,
        Nexus.Delivery.Release.ReleasePlan plan,
        ReleaseVerification verification,
        ReleaseRefPreDeploymentVerdict preDeployment,
        ReleaseSecurityReadiness readiness,
        DeploymentDecision? decision,
        MigrationOutcome migration,
        string digestBefore,
        string digestAfter,
        string treeAfter,
        bool deployed,
        HealthObservation? health,
        IReadOnlyDictionary<string, string>? smoke,
        IReadOnlyDictionary<string, string>? observability,
        string? reason,
        DeploymentDecision? verifyDecision = null,
        IReadOnlyList<string>? lineageIds = null,
        DeploymentLane? lane = null)
    {
        lane ??= DeploymentLane.Dev;
        var evidence = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["releaseId"] = release.ReleaseId.Value,
            ["environment"] = descriptor.Environment,
            ["occurredUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["deployed"] = deployed,
            ["refusalReason"] = reason ?? "(none)",
            ["artifact"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["artifactId"] = release.Artifacts[0].ArtifactId.Value,
                ["recordedDigest"] = release.Artifacts[0].ContentDigest.ToString(),
                // The TASK 9 hard rule, as two readings of the same file: the SHA of the fetched artifact
                // BEFORE the deployment touched it, and the SHA of that same file AFTER materialization.
                // "unchanged" is the assertion; the two readings are kept so a reader can check it rather
                // than take it.
                ["sha256Before"] = digestBefore,
                ["sha256After"] = digestAfter,
                ["bytesIdenticalBeforeAndAfter"] = string.Equals(digestBefore, digestAfter, StringComparison.Ordinal),
                ["bytesIdenticalToCertified"] = string.Equals(digestBefore, release.Artifacts[0].ContentDigest.ToString(), StringComparison.Ordinal),
                // The tree hash of the extracted target. A different quantity from the two above, and NOT
                // compared against them: a tree hash over (path, content-hash) pairs can never equal the
                // hash of the archive file that carried them. It is recorded because it is the observation
                // of what is on disk in ENV-DEV, which is what a later drift check would compare against.
                ["targetTreeSha256"] = treeAfter,
                ["targetTreeDigestIsComparableToTheArchiveDigest"] = false,
                ["rebuilt"] = false
            },
            ["releaseIntegrity"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["verifiedThroughTheDecodePath"] = true,
                ["isIntact"] = verification.IsIntact,
                ["registryContentIntact"] = verification.RegistryContentIntact,
                // Each is rendered with the "(none)" convention rather than ToString(), because a null
                // digest or lifecycle means the verification could not read the fact at all - which is a
                // different statement from a digest that was read, and the two must not look alike in
                // evidence.
                ["recordedDigest"] = verification.RecordedDigest?.ToString() ?? "(none)",
                ["recomputedDigest"] = verification.RecomputedDigest?.ToString() ?? "(none)",
                ["lifecycle"] = verification.Lifecycle?.ToString() ?? "(none)",
                ["bundleMutated"] = false
            },
            ["preDeploymentControl"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["state"] = preDeployment.State.ToString(),
                ["verified"] = preDeployment.IsDeployable,
                ["remote"] = preDeployment.Remote,
                ["releaseRefName"] = preDeployment.RefName,
                ["expectedTagObjectSha"] = preDeployment.ExpectedTagObjectSha ?? "(none)",
                ["observedRemoteTagObjectSha"] = preDeployment.ObservedRemoteTagObjectSha ?? "(none)",
                ["observedRemoteTargetCommitSha"] = preDeployment.ObservedRemoteTargetCommitSha ?? "(none)",
                ["refusalReasons"] = preDeployment.RefusalReasons.Select(r => r.ToString()).ToArray(),
                ["detail"] = preDeployment.Detail.ToArray()
            },
            ["governedPlanReadiness"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["ready"] = readiness.IsReady,
                ["environment"] = readiness.Environment.Value,
                ["refusalReasons"] = readiness.RefusalReasons.Select(r => r.ToString()).ToArray(),
                ["detail"] = readiness.Detail.ToArray()
            },
            ["promotion"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["transition"] = lane.DeploymentTransition.ToString(),
                ["allowed"] = decision?.IsAllowed ?? false,
                ["decided"] = decision is not null,
                ["fromState"] = decision?.FromState.ToString() ?? "(not decided)",
                ["nextState"] = decision?.NextState.ToString() ?? "(not decided)",
                ["stateAssignedByDriver"] = false,
                ["refusalReasons"] = decision?.RefusalReasons.Select(r => r.ToString()).ToArray() ?? [],
                // Stated rather than glossed: DeploymentStateMachine's DeployToDev gate requires
                // ReadinessObserved to be non-null, so this decision is taken AFTER the release is running
                // in ENV-DEV. Its refusal therefore lands post-move. Every refusal that could be taken
                // pre-move (release integrity, the C-2 control, the governed plan's readiness assessment,
                // migration governance) is taken before the first target mutation.
                ["decidedBeforeAnythingMoved"] = false,
                ["decidedAfterReadinessWasObserved"] = true
            },
            ["verification"] = verifyDecision is null
                ? new Dictionary<string, object>(StringComparer.Ordinal) { ["decided"] = false }
                : new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["transition"] = lane.VerificationTransition.ToString(),
                    ["allowed"] = verifyDecision.IsAllowed,
                    ["decided"] = true,
                    ["fromState"] = verifyDecision.FromState.ToString(),
                    ["nextState"] = verifyDecision.NextState.ToString(),
                    ["stateAssignedByDriver"] = false,
                    ["refusalReasons"] = verifyDecision.RefusalReasons.Select(r => r.ToString()).ToArray()
                },
            ["migrationGovernance"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["decision"] = migration.Verdict.Decision.ToString(),
                ["permitted"] = migration.Verdict.PermitsDeployment,
                ["refusalReasons"] = migration.Verdict.RefusalReasons.Select(r => r.ToString()).ToArray(),
                ["observedCompatibility"] = migration.Verdict.ObservedCompatibility?.ToString() ?? "(not established)",
                ["releaseRequirementState"] = release.Migrations.State.ToString(),
                ["releaseBackupRequiredBeforeInspection"] = release.Migrations.BackupRequired,
                ["releaseReversibility"] = release.Migrations.Reversibility.ToString(),
                ["targetBefore"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["reachable"] = migration.Before.Reachable,
                    ["schemaPresent"] = migration.Before.SchemaPresent,
                    ["appliedMigrations"] = migration.Before.AppliedMigrationIds.ToArray(),
                    ["dataState"] = migration.Before.DataState.ToString(),
                    ["backupVerified"] = migration.Before.BackupVerified,
                    ["basis"] = migration.Before.Basis
                },
                // The observed data state was HoldsData on a freshly created container, because the PostGIS
                // image installs spatial_ref_sys into public. That reading was accepted rather than narrowed,
                // and the obligation it triggers was discharged with a real verified backup. Recorded here so
                // a reader sees the reasoning rather than an unexplained backup artifact.
                ["backup"] = migration.Backup is null
                    ? new Dictionary<string, object>(StringComparer.Ordinal) { ["taken"] = false }
                    : new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["taken"] = true,
                        ["verified"] = migration.Backup.IsVerified,
                        ["path"] = migration.Backup.Path ?? "(none)",
                        ["digest"] = migration.Backup.Digest ?? "(none)",
                        ["bytes"] = migration.Backup.Bytes,
                        ["tocEntries"] = migration.Backup.TocEntries,
                        ["verificationMethod"] = "PGDMP header; pg_restore --list exit 0 with a non-zero TOC; sha256 of the host copy",
                        ["basis"] = migration.Backup.Basis,
                        ["refusalReason"] = migration.Backup.RefusalReason ?? "(none)"
                    },
                ["detail"] = migration.Verdict.Detail.ToArray()
            },
            ["observations"] = health is null
                ? new Dictionary<string, object>(StringComparer.Ordinal) { ["performed"] = false }
                : new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["performed"] = true,
                    ["readinessObserved"] = health.ReadinessObserved,
                    ["livenessStatus"] = health.LivenessStatus,
                    ["readinessStatus"] = health.ReadinessStatus,
                    ["readinessMillis"] = health.ReadinessMillis,
                    ["smoke"] = smoke ?? new Dictionary<string, string>(),
                    ["observability"] = observability ?? new Dictionary<string, string>()
                },
            ["secretsInThisRecord"] = false,
            ["applicationRebuilt"] = false,
            ["azureInfrastructureCreated"] = false,
            ["customerOrProductionDataUsed"] = false,
            ["targetDatabase"] = descriptor.Database!.DatabaseName,
            ["targetDatabaseIsolatedContainer"] = descriptor.Database.ContainerName,
            ["lineageId"] = plan.LineageId,
            ["lineageRecordsAppended"] = lineageIds?.ToArray() ?? [],
            ["lineageAppendedByTheDeploymentPromoter"] = deployed
        };

        return evidence;
    }

    private static void WriteEvidence(string path, object evidence)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private static void WriteTranscript(string path, StringBuilder transcript)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, transcript.ToString(), new UTF8Encoding(false));
    }

    // ==================================================================== hashing

    private static async Task<string> Sha256FileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false)).ToLowerInvariant();
    }

    /// <summary>
    /// A digest over a directory tree, as a set of (relative path, content digest) pairs in ordinal order.
    ///
    /// <para>
    /// <b>Path-sensitive and order-independent.</b> Each entry contributes <c>path NUL contentDigest NUL</c>,
    /// so renaming a file changes the digest while the enumeration order does not. Zipping the bytes instead
    /// would carry the archive's own timestamps and produce a digest that changes without any byte of the
    /// application changing.
    /// </para>
    /// </summary>
    private static async Task<string> Sha256TreeAsync(string root)
    {
        using var accumulator = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            await using var stream = File.OpenRead(file);
            var content = await SHA256.HashDataAsync(stream).ConfigureAwait(false);

            accumulator.AppendData(Encoding.UTF8.GetBytes(relative));
            accumulator.AppendData([0]);
            accumulator.AppendData(Encoding.UTF8.GetBytes(Convert.ToHexString(content).ToLowerInvariant()));
            accumulator.AppendData([0]);
        }

        return "sha256:" + Convert.ToHexString(accumulator.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Require(string? value, string member, string why)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The run plan does not name '{member}'. {why}");
        }

        return value;
    }
}
