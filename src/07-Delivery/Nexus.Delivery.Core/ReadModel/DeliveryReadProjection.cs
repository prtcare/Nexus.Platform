using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexus.Delivery.Contracts.ReadModel;

namespace Nexus.Delivery.Core.ReadModel;

/// <summary>
/// The authority cannot be asked. <b>Distinct from an authority that answered "none".</b>
///
/// <para>
/// A typed exception rather than an empty projection, because the two lead to different published
/// documents and a consumer must be able to tell them apart: an unavailable authority publishes
/// nothing, while an authority that holds no releases publishes a document saying exactly that.
/// </para>
/// </summary>
public sealed class DeliveryAuthorityUnavailableException : Exception
{
    public DeliveryAuthorityUnavailableException(string message) : base(message) { }
}

/// <summary>
/// W10.1 TASK 7 — <b>reads Delivery authority and projects it onto the published read contract.</b>
///
/// <para>
/// <b>Owner side, by construction.</b> This type lives in the Delivery implementation assembly and
/// reads the store directly. A consumer never does: it reads the published file. That separation is
/// the whole point of the milestone — the projection is produced by the authority, not assembled by
/// the reader from whatever it could reach.
/// </para>
///
/// <para>
/// <b>Why it reads the store LAYOUT.</b> Every Delivery store interface is keyed by identifier and
/// has no enumeration — measured, not assumed: <c>IArtifactStore</c>, <c>IArtifactRegistry</c>,
/// <c>IReleaseRegistry</c>, <c>IReleasePlanStore</c> and <c>IDeploymentLineageLog</c> expose only
/// <c>Resolve*(id)</c> / <c>TryOpen(id)</c> / <c>Exists(id)</c>. A projection must answer "what
/// exists?", so it reads the documented directory convention. Adding <c>ListAsync</c> to five
/// interfaces would be a public-contract change to the authority to serve a projection; reading the
/// same bytes the store writes cannot disagree with the store.
/// </para>
///
/// <para>
/// <b>Nothing is invented.</b> A fact the authority does not hold is emitted as a
/// <see cref="DeliveryReadGap"/> — never reconstructed from prose, never inferred from a filename,
/// never assumed from the shape of a neighbour. The ledger already contains records whose
/// deployment cannot be resolved; those are gaps, and they are published as gaps.
/// </para>
/// </summary>
public sealed class DeliveryReadProjection
{
    private readonly string _root;

    /// <summary>
    /// The lexical ordering of authority classes in the payload, so a reader can compare two
    /// projections for a semantic change without depending on List order.
    /// </summary>
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public DeliveryReadProjection(string storeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);
        _root = storeRoot;
    }

    /// <summary>
    /// Builds the payload from authority state. Pure with respect to the store: reads only, writes
    /// nothing, and takes no clock — the caller supplies the observation instant so that a projection
    /// is reproducible for a fixed input.
    /// </summary>
    public DeliveryReadPayload Project()
    {
        // A store root that is not there is NOT an empty authority.
        //
        // Without this check the projection returns a payload with zero of everything, the publisher
        // accepts it, and a consumer is handed a valid, current-looking document asserting that
        // Delivery holds no releases — which is a fabricated answer to a question nobody could
        // answer. Measured: the control for it failed on the first run of this type, which is how
        // this check came to exist rather than being reasoned about.
        //
        // An EXISTING but empty root is different, and does publish: that is a reported zero, and the
        // distinction between "absent" and "none" is precisely what the read contract exists to keep.
        if (!Directory.Exists(_root))
        {
            throw new DeliveryAuthorityUnavailableException(
                $"no Delivery store exists at '{_root}'. A missing authority is not an empty authority: "
                + "nothing is published, because a valid document reporting zero releases would be an "
                + "answer to a question the authority cannot be asked.");
        }

        var builds = ReadBuilds();
        var artifacts = ReadArtifacts();
        var identities = ReadIdentities();
        var releases = ReadReleases();
        var deployments = ReadDeployments();
        var gaps = new List<DeliveryReadGap>();

        var environments = DeriveEnvironments(deployments);
        var verifications = ReadVerifications(releases, deployments);
        var promotions = ReadPromotions();
        var migrations = ReadMigrations();
        var rollbacks = ReadRollbacks();
        var plan = ReadReleaseSecurityPlan();
        var lineageEdges = ReadLineageEdges();
        var backup = ReadBackup();

        // A release whose deployment lineage cannot be resolved is a gap, not a silent absence.
        foreach (var release in releases)
        {
            var hasDeployment = deployments.Any(d => string.Equals(d.ReleaseId, release.ReleaseId, StringComparison.Ordinal));
            if (!hasDeployment)
            {
                gaps.Add(new DeliveryReadGap(
                    release.ReleaseId,
                    "NO_DEPLOYMENT_RECORD",
                    "The release is registered but no deployment in the ledger names it. Whether it was "
                    + "ever deployed cannot be answered from the authority, and is not inferred."));
            }
        }

        // The ledger's older records carry no DeploymentId. That is an authoritative absence.
        foreach (var gap in ReadDeploymentGaps())
        {
            gaps.Add(gap);
        }

        var prodDeployment = deployments
            .FirstOrDefault(d => string.Equals(d.Environment, "ENV-PROD", StringComparison.Ordinal));

        var prodReadiness = new DeliveryReadProdReadiness(
            // The Owner's recorded deferral. Sourced from the plan, never inferred from silence.
            plan is { AllowedEnvironmentScope: "DevTest" }
                ? DeliveryReadProdStates.DeferredByOwner
                : DeliveryReadProdStates.Unknown,
            // No production deployment exists. That is NOT_STARTED, which is not a failure.
            prodDeployment is null ? DeliveryReadProdStates.NotStarted : prodDeployment.State,
            plan?.DecisionReference ?? "(none recorded)",
            plan?.DecisionAuthority ?? "(none recorded)",
            plan is null ? DeliveryAuthorityClass.Unavailable : DeliveryAuthorityClass.Authoritative);

        return new DeliveryReadPayload(
            builds,
            artifacts,
            identities,
            releases,
            deployments,
            environments,
            verifications,
            promotions,
            migrations,
            backup,
            rollbacks,
            new DeliveryReadReferenceProtection(
                plan?.Mode ?? "(no plan)",
                plan?.ServerSideVerified ?? false,
                plan?.AllowedEnvironmentScope ?? "(no plan)",
                plan?.CredentialRotationStatus ?? "(no plan)",
                plan?.DecisionReference ?? "(no plan)",
                plan?.DecisionAuthority ?? "(no plan)",
                plan is null ? DeliveryAuthorityClass.Unavailable : DeliveryAuthorityClass.Authoritative),
            prodReadiness,
            lineageEdges,
            gaps);
    }

    /// <summary>
    /// The semantic digest: SHA-256 over the canonical payload <b>only</b>.
    ///
    /// <para>
    /// Timestamps are excluded deliberately. TASK 9 requires that republishing unchanged authority
    /// state yields the same digest, and TASK 4 requires that a timestamp never corrupts deterministic
    /// content identity. Both are satisfied by hashing the payload and nothing else, which lets a
    /// consumer tell a metadata refresh from a real state change.
    /// </para>
    /// </summary>
    public static string SemanticDigest(DeliveryReadPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // The payload is serialised through its declared type, so the bytes are a function of the
        // contract and the values — not of property order at the call site.
        var json = JsonSerializer.Serialize(payload, CanonicalOptions);

        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    // ================================================================ readers

    private List<DeliveryReadBuild> ReadBuilds()
    {
        var builds = new List<DeliveryReadBuild>();
        var directory = Path.Combine(_root, "builds");

        foreach (var file in FilesIn(directory, "*.index"))
        {
            var artifactId = File.ReadAllText(file).Trim().TrimStart('﻿');
            if (artifactId.Length == 0)
            {
                continue;
            }

            builds.Add(new DeliveryReadBuild(
                Path.GetFileNameWithoutExtension(file),
                artifactId,
                DeliveryAuthorityClass.Authoritative));
        }

        return Ordered(builds, b => b.BuildId);
    }

    private List<DeliveryReadArtifact> ReadArtifacts()
    {
        var artifacts = new List<DeliveryReadArtifact>();

        foreach (var entry in FilesInTree(Path.Combine(_root, "artifacts"), "entry.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(entry).TrimStart('﻿'));
            var e = document.RootElement;

            artifacts.Add(new DeliveryReadArtifact(
                Text(e, "ArtifactId"),
                Text(e, "BuildId"),
                Text(e, "ContentDigest"),
                Number(e, "SizeBytes"),
                Text(e, "Lifecycle"),
                Text(e, "PublishedAt"),
                DeliveryAuthorityClass.Authoritative));
        }

        return Ordered(artifacts, a => a.ArtifactId);
    }

    private List<DeliveryReadArtifactIdentity> ReadIdentities()
    {
        var identities = new List<DeliveryReadArtifactIdentity>();

        foreach (var file in FilesIn(Path.Combine(_root, "identities"), "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file).TrimStart('﻿'));
            var e = document.RootElement;

            identities.Add(new DeliveryReadArtifactIdentity(
                Text(e, "ArtifactId"),
                Text(e, "ContentDigest"),
                Text(e, "State"),
                (int)Number(e, "PublishAttempts"),
                Text(e, "FirstAcceptedUtc"),
                DeliveryAuthorityClass.Authoritative));
        }

        return Ordered(identities, i => i.ArtifactId);
    }

    private List<DeliveryReadRelease> ReadReleases()
    {
        var releases = new List<DeliveryReadRelease>();
        var directory = Path.Combine(_root, "releases");

        if (!Directory.Exists(directory))
        {
            return releases;
        }

        foreach (var releaseDirectory in Directory.GetDirectories(directory).OrderBy(d => d, StringComparer.Ordinal))
        {
            var entryPath = Path.Combine(releaseDirectory, "entry.json");
            if (!File.Exists(entryPath))
            {
                continue;
            }

            using var entryDocument = JsonDocument.Parse(File.ReadAllText(entryPath).TrimStart('﻿'));
            var e = entryDocument.RootElement;

            var sourceCommits = new List<string>();
            var artifactIds = new List<string>();

            var provenancePath = Path.Combine(releaseDirectory, "provenance.json");
            if (File.Exists(provenancePath))
            {
                using var provenanceDocument = JsonDocument.Parse(File.ReadAllText(provenancePath).TrimStart('﻿'));
                var p = provenanceDocument.RootElement;

                if (p.TryGetProperty("SourceCommits", out var commits) && commits.ValueKind == JsonValueKind.Array)
                {
                    sourceCommits.AddRange(commits.EnumerateArray().Select(c => c.GetString() ?? string.Empty));
                }

                if (p.TryGetProperty("Artifacts", out var arts) && arts.ValueKind == JsonValueKind.Array)
                {
                    artifactIds.AddRange(arts.EnumerateArray().Select(a => a.GetString() ?? string.Empty));
                }
            }

            releases.Add(new DeliveryReadRelease(
                Text(e, "ReleaseId"),
                Text(e, "UnitId"),
                Text(e, "Version"),
                Text(e, "Lifecycle"),
                Text(e, "BuildId"),
                Text(e, "BundleId"),
                Text(e, "ReleaseRefName"),
                sourceCommits,
                artifactIds,
                Text(e, "RegisteredAt"),
                DeliveryAuthorityClass.Authoritative));
        }

        return Ordered(releases, r => r.ReleaseId);
    }

    private List<DeliveryReadDeployment> ReadDeployments()
    {
        var deployments = new List<DeliveryReadDeployment>();
        var ledger = Path.Combine(_root, "deployment-lineage", "lineage.jsonl");

        if (!File.Exists(ledger))
        {
            return deployments;
        }

        // Fold the append-only ledger into one row per deployment attempt: the LAST record for an
        // attempt is its current state, and the first is when it appeared. A ledger read any other
        // way would report a deployment's history as its state.
        var folded = new Dictionary<string, (string Environment, string Attempt, string State, string Transition, string OccurredAt)>(
            StringComparer.Ordinal);

        foreach (var line in File.ReadLines(ledger))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line.TrimStart('﻿'));
            var e = document.RootElement;

            var deploymentId = Text(e, "DeploymentId");
            if (deploymentId.Length == 0)
            {
                continue;   // older-format record; reported as a gap, never guessed at
            }

            var environment = Text(e, "Environment");
            var transition = Text(e, "Transition");
            var occurredAt = Text(e, "OccurredAt");

            folded[deploymentId] = (environment, AttemptFrom(deploymentId), transition, transition, occurredAt);
        }

        foreach (var (deploymentId, row) in folded.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            deployments.Add(new DeliveryReadDeployment(
                deploymentId,
                ReleaseIdFrom(deploymentId),
                row.Environment,
                row.Attempt,
                row.State,
                row.Transition,
                row.OccurredAt,
                DeliveryAuthorityClass.Authoritative));
        }

        return deployments;
    }

    /// <summary>
    /// The gaps the ledger itself declares: records with no <c>DeploymentId</c>. Their deployment
    /// cannot be resolved, and the honest report is that it cannot be — not a reconstruction.
    /// </summary>
    private List<DeliveryReadGap> ReadDeploymentGaps()
    {
        var gaps = new List<DeliveryReadGap>();
        var ledger = Path.Combine(_root, "deployment-lineage", "lineage.jsonl");

        if (!File.Exists(ledger))
        {
            return gaps;
        }

        foreach (var line in File.ReadLines(ledger))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line.TrimStart('﻿'));
            var e = document.RootElement;

            if (Text(e, "DeploymentId").Length != 0)
            {
                continue;
            }

            var lineageId = Text(e, "LineageId");
            gaps.Add(new DeliveryReadGap(
                lineageId,
                "MISSING_DEPLOYMENT_ID",
                $"Ledger record '{lineageId}' carries no DeploymentId, so the deployment it describes "
                + "cannot be resolved from the authority. It is reported as a gap rather than "
                + "reconstructed from the surrounding records."));
        }

        return gaps;
    }

    private static List<DeliveryReadEnvironment> DeriveEnvironments(IReadOnlyList<DeliveryReadDeployment> deployments)
    {
        var environments = new List<DeliveryReadEnvironment>();

        foreach (var group in deployments.GroupBy(d => d.Environment, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            environments.Add(new DeliveryReadEnvironment(
                group.Key,
                RoleOf(group.Key),
                group.Count(),
                group.Max(d => d.LastOccurredAt),
                // DERIVED: this row is computed by the projection from authoritative attempts. It is
                // not itself something the authority records.
                DeliveryAuthorityClass.Derived));
        }

        return environments;
    }

    private List<DeliveryReadVerification> ReadVerifications(
        IReadOnlyList<DeliveryReadRelease> releases,
        IReadOnlyList<DeliveryReadDeployment> deployments)
    {
        var verifications = new List<DeliveryReadVerification>();

        foreach (var release in releases)
        {
            var bundlePath = Path.Combine(_root, "releases", release.ReleaseId, "release-bundle.json");
            if (!File.Exists(bundlePath))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(bundlePath).TrimStart('﻿'));
            if (!document.RootElement.TryGetProperty("Evidence", out var evidence))
            {
                continue;
            }

            foreach (var property in evidence.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                verifications.Add(new DeliveryReadVerification(
                    release.ReleaseId,
                    property.Name,
                    property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString(),
                    release.RegisteredAt,
                    // Recorded once, at a time. NOT a statement about what is running now.
                    DeliveryAuthorityClass.HistoricalEvidence));
            }
        }

        foreach (var deployment in deployments)
        {
            if (!deployment.LastTransition.StartsWith("Verify", StringComparison.Ordinal))
            {
                continue;
            }

            verifications.Add(new DeliveryReadVerification(
                deployment.DeploymentId,
                deployment.LastTransition,
                "OBSERVED",
                deployment.LastOccurredAt,
                DeliveryAuthorityClass.HistoricalEvidence));
        }

        return verifications
            .OrderBy(v => v.Subject, StringComparer.Ordinal)
            .ThenBy(v => v.Kind, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every promotion the ledger records, read from the ledger's own transitions.
    ///
    /// <para>
    /// <b>Read from the raw records, not from the folded deployments.</b> Folding keeps the LAST
    /// transition per attempt, so a promotion followed by a verification disappears — which is what
    /// happened on the first run of this projection: the accepted release's <c>PromoteToTest</c> was
    /// overwritten by <c>VerifyInTest</c> and the projection reported zero promotions against a
    /// release that had been promoted. A promotion is an event, and events do not survive being
    /// folded into state.
    /// </para>
    /// </summary>
    private List<DeliveryReadPromotion> ReadPromotions()
    {
        var promotions = new List<DeliveryReadPromotion>();
        var ledger = Path.Combine(_root, "deployment-lineage", "lineage.jsonl");

        if (!File.Exists(ledger))
        {
            return promotions;
        }

        foreach (var line in File.ReadLines(ledger))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line.TrimStart('﻿'));
            var e = document.RootElement;

            var transition = Text(e, "Transition");
            if (!transition.StartsWith("Promote", StringComparison.Ordinal))
            {
                continue;
            }

            var deploymentId = Text(e, "DeploymentId");
            var to = Text(e, "Environment");

            promotions.Add(new DeliveryReadPromotion(
                DeploymentReleaseId(deploymentId, e),
                FromEnvironmentOf(to),
                to,
                "PROMOTED",
                Text(e, "OccurredAt"),
                DeliveryAuthorityClass.HistoricalEvidence));
        }

        return promotions.OrderBy(p => p.ObservedAt, StringComparer.Ordinal).ThenBy(p => p.ReleaseId, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The release a promotion belongs to — from the deployment id when the record has one, and from
    /// the record's own <c>ReleaseId</c> when it does not.
    /// </summary>
    private static string DeploymentReleaseId(string deploymentId, JsonElement record) =>
        deploymentId.Length != 0 ? ReleaseIdFrom(deploymentId) : Text(record, "ReleaseId");

    /// <summary>The environment a promotion came from, from the one it reached.</summary>
    private static string FromEnvironmentOf(string toEnvironment) => toEnvironment switch
    {
        "ENV-TEST" => "ENV-DEV",
        "ENV-PROD" => "ENV-TEST",
        _ => "(unknown)"
    };

    private List<DeliveryReadMigration> ReadMigrations()
    {
        var migrations = new List<DeliveryReadMigration>();

        foreach (var releaseDirectory in DirectoriesIn(Path.Combine(_root, "releases")))
        {
            var bundlePath = Path.Combine(releaseDirectory, "release-bundle.json");
            if (!File.Exists(bundlePath))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(bundlePath).TrimStart('﻿'));
            if (!document.RootElement.TryGetProperty("Migrations", out var migrationsElement))
            {
                continue;
            }

            var ids = new List<string>();
            if (migrationsElement.TryGetProperty("MigrationIds", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array)
            {
                ids.AddRange(idsElement.EnumerateArray().Select(i => i.GetString() ?? string.Empty));
            }

            migrations.Add(new DeliveryReadMigration(
                Path.GetFileName(releaseDirectory),
                Text(migrationsElement, "State"),
                Text(migrationsElement, "Provider"),
                ids,
                DeliveryAuthorityClass.Authoritative));
        }

        return Ordered(migrations, m => m.ReleaseId);
    }

    private List<DeliveryReadRollback> ReadRollbacks()
    {
        var rollbacks = new List<DeliveryReadRollback>();

        foreach (var releaseDirectory in DirectoriesIn(Path.Combine(_root, "releases")))
        {
            var bundlePath = Path.Combine(releaseDirectory, "release-bundle.json");
            if (!File.Exists(bundlePath))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(bundlePath).TrimStart('﻿'));
            if (!document.RootElement.TryGetProperty("Rollback", out var rollback))
            {
                continue;
            }

            rollbacks.Add(new DeliveryReadRollback(
                Path.GetFileName(releaseDirectory),
                Text(rollback, "State"),
                Text(rollback, "PreviousReleaseId"),
                Bool(rollback, "CrossesMigrationBoundary"),
                Bool(rollback, "Rehearsed"),
                Text(rollback, "Basis"),
                DeliveryAuthorityClass.Authoritative));
        }

        return Ordered(rollbacks, r => r.ReleaseId);
    }

    /// <summary>
    /// The deterministic backup requirement. <b>No backup state is claimed.</b>
    ///
    /// <para>
    /// Every release bundle declares a rollback basis that presupposes a restore point, but no source
    /// reports whether a backup ran. The projection therefore publishes the obligation as
    /// authoritative and the state as unavailable — which is different from publishing "no backup",
    /// and is the distinction the five-class model exists to keep.
    /// </para>
    /// </summary>
    private static DeliveryReadBackup ReadBackup() => new(
        DeliveryReadProdStates.Unknown,
        "The authority records the deterministic backup requirement as a gate precondition. No source "
        + "reports whether a backup was taken, so no backup state is claimed. This is an absent source, "
        + "not a failure, and not a reported zero.",
        DeliveryAuthorityClass.Unavailable);

    private List<DeliveryReadLineageEdge> ReadLineageEdges()
    {
        var edges = new List<DeliveryReadLineageEdge>();
        var directory = Path.Combine(_root, "lineage");

        foreach (var file in FilesIn(directory, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file).TrimStart('﻿'));
            var e = document.RootElement;

            var lineageId = Text(e, "LineageId");
            var releaseId = Text(e, "ReleaseId");
            var buildId = Text(e, "BuildId");

            if (releaseId.Length != 0 && buildId.Length != 0)
            {
                edges.Add(new DeliveryReadLineageEdge(releaseId, buildId, "RELEASE_BUILT_FROM", DeliveryAuthorityClass.Authoritative));
            }

            if (lineageId.Length != 0 && releaseId.Length != 0)
            {
                edges.Add(new DeliveryReadLineageEdge(lineageId, releaseId, "LINEAGE_RECORDS_RELEASE", DeliveryAuthorityClass.Authoritative));
            }
        }

        return edges
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ThenBy(e => e.Kind, StringComparer.Ordinal)
            .ToList();
    }

    private PlanFacts? ReadReleaseSecurityPlan()
    {
        foreach (var releaseDirectory in DirectoriesIn(Path.Combine(_root, "release-plans")))
        {
            var planPath = Path.Combine(releaseDirectory, "plan.json");
            if (!File.Exists(planPath))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(planPath).TrimStart('﻿'));
            var e = document.RootElement;

            return new PlanFacts(
                Text(e, "ReleaseReferenceProtectionMode"),
                Bool(e, "ReleaseRefServerSideProtectionVerified"),
                Text(e, "AllowedEnvironmentScope"),
                Text(e, "CredentialRotationStatus"),
                Text(e, "HumanDecisionReference"),
                Text(e, "HumanDecisionAuthority"));
        }

        return null;
    }

    private sealed record PlanFacts(
        string Mode,
        bool ServerSideVerified,
        string AllowedEnvironmentScope,
        string CredentialRotationStatus,
        string DecisionReference,
        string DecisionAuthority);

    // ================================================================ helpers

    private static IEnumerable<string> FilesIn(string directory, string pattern) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, pattern).OrderBy(f => f, StringComparer.Ordinal)
            : [];

    /// <summary>
    /// Every file matching <paramref name="pattern"/> at any depth.
    ///
    /// <para>
    /// The store nests by identity — <c>artifacts/&lt;unit&gt;/&lt;type&gt;/&lt;name&gt;@&lt;version&gt;/entry.json</c>
    /// — so a top-level glob finds nothing and reports a populated store as empty. That failure is
    /// silent and looks like an absent authority rather than a wrong query, which is why the depth is
    /// stated here rather than left to the pattern.
    /// </para>
    /// </summary>
    private static IEnumerable<string> FilesInTree(string directory, string pattern) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, pattern, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
            : [];

    private static IEnumerable<string> DirectoriesIn(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetDirectories(directory).OrderBy(d => d, StringComparer.Ordinal)
            : [];

    private static List<T> Ordered<T>(List<T> items, Func<T, string> key) =>
        items.OrderBy(key, StringComparer.Ordinal).ToList();

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// `dep-&lt;release hex&gt;-&lt;env&gt;-&lt;attempt&gt;` — the attempt, from the identifier.
    ///
    /// <para>
    /// Four segments, because the release hex carries no separator of its own. An off-by-one here
    /// yields an empty attempt on every row rather than an error — which is why the arity is asserted
    /// by a test rather than left to this comment.
    /// </para>
    /// </summary>
    private static string AttemptFrom(string deploymentId)
    {
        var parts = deploymentId.Split('-');
        return parts.Length >= 4 ? parts[^1] : string.Empty;
    }

    /// <summary>The release, from the deployment identifier's own grammar.</summary>
    private static string ReleaseIdFrom(string deploymentId)
    {
        var parts = deploymentId.Split('-');
        return parts.Length >= 3 ? "rel-" + parts[1] : string.Empty;
    }

    private static string RoleOf(string environment) => environment switch
    {
        "ENV-DEV" => "Device",
        "ENV-TEST" => "Test",
        "ENV-PROD" => "Production",
        _ => "Unknown"
    };
}
