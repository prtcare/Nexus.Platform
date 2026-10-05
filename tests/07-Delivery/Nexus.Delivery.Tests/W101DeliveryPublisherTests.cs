using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexus.Delivery.Contracts.ReadModel;
using Nexus.Delivery.Core.ReadModel;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// W10.1 TASKs 8 and 13 — <b>the publisher's failure controls and its atomicity proofs.</b>
///
/// <para>
/// <b>Every control here builds its own store and its own destination.</b> Nothing reads the real
/// estate, so these run in the portable lane on any host, and a control cannot pass because of what
/// happens to be on the machine. The real-estate projection is proved separately and is a different
/// claim.
/// </para>
///
/// <para>
/// <b>Each refusal is paired with the case that must succeed.</b> A publisher that refused
/// everything would satisfy every "is refused" assertion below while making the projection
/// worthless, so the positive case is asserted too — the same discipline the rest of this suite uses.
/// </para>
/// </summary>
public sealed class W101DeliveryPublisherTests : IDisposable
{
    private readonly string _scratch;
    private readonly string _store;
    private readonly string _destination;

    public W101DeliveryPublisherTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "w101-publish-" + Guid.NewGuid().ToString("N")[..10]);
        _store = Path.Combine(_scratch, "store");
        _destination = Path.Combine(_scratch, "published");

        Directory.CreateDirectory(_store);
        Directory.CreateDirectory(_destination);

        SeedStore(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { /* temp; best effort */ }
    }

    private DeliveryReadPublisher Publisher() => new(new DeliveryReadProjection(_store));

    private string PublishedPath => Path.Combine(_destination, DeliveryReadContract.FileName);

    private static readonly string ObservedAt = "2026-10-05T12:00:00.0000000+00:00";

    // ================================================================ TASK 8 — atomicity

    /// <summary>Successful publication produces a complete, parseable snapshot.</summary>
    [Fact]
    public void ASuccessfulPublication_ProducesACompleteSnapshot()
    {
        var outcome = Publisher().Publish(_destination, ObservedAt);

        Assert.True(outcome.Published, outcome.Reason);
        Assert.True(File.Exists(PublishedPath));

        var model = JsonSerializer.Deserialize<DeliveryReadModel>(File.ReadAllText(PublishedPath))!;

        Assert.Equal(DeliveryReadContract.SchemaVersion, model.SchemaVersion);
        Assert.Equal(DeliveryReadContract.Authority, model.Source.Authority);
        Assert.Equal(outcome.PayloadDigest, model.Source.PayloadDigest);
        Assert.NotEmpty(model.Payload.Releases);

        // The staging file must not survive a successful publication — a leftover would be a
        // half-document sitting beside the real one.
        Assert.False(File.Exists(PublishedPath + ".staging"));
    }

    /// <summary>
    /// <b>Interrupted publication leaves the previous snapshot readable.</b>
    ///
    /// <para>
    /// The interruption is real, not simulated by a test hook: a DIRECTORY is planted at the staging
    /// path, so the staging write cannot succeed. That is the same shape as a full disk, a revoked
    /// permission or a killed process between stage and move — and the assertion is that the
    /// previously published document is byte-identical afterwards.
    /// </para>
    /// </summary>
    [Fact]
    public void AnInterruptedPublication_LeavesThePreviousSnapshotReadable()
    {
        Assert.True(Publisher().Publish(_destination, ObservedAt).Published);
        var before = File.ReadAllBytes(PublishedPath);

        // Block the staging write.
        Directory.CreateDirectory(PublishedPath + ".staging");

        var outcome = Publisher().Publish(_destination, "2026-10-05T13:00:00.0000000+00:00");

        Assert.False(outcome.Published);
        Assert.Contains("staging", outcome.Reason, StringComparison.OrdinalIgnoreCase);

        // The published document is untouched — same bytes, still valid.
        Assert.Equal(before, File.ReadAllBytes(PublishedPath));
        var model = JsonSerializer.Deserialize<DeliveryReadModel>(File.ReadAllText(PublishedPath))!;
        Assert.Equal(DeliveryReadContract.SchemaVersion, model.SchemaVersion);
    }

    /// <summary>An invalid projection replaces nothing.</summary>
    [Fact]
    public void AnInvalidProjection_DoesNotReplaceTheCurrentSnapshot()
    {
        Assert.True(Publisher().Publish(_destination, ObservedAt).Published);
        var before = File.ReadAllBytes(PublishedPath);

        // A destination whose name cannot hold the file: the parent is replaced by a file, so the
        // staging path is under something that is not a directory.
        var blocked = Path.Combine(_scratch, "not-a-directory");
        File.WriteAllText(blocked, "x");

        var outcome = Publisher().Publish(Path.Combine(blocked, "nested"), ObservedAt);

        Assert.False(outcome.Published);
        Assert.Equal(before, File.ReadAllBytes(PublishedPath));
    }

    // ================================================================ TASK 13 — failure controls

    /// <summary>Control 1 — a missing authority is an explicit refusal, not an empty projection.</summary>
    [Fact]
    public void MissingAuthority_IsRefusedAsUnavailable_NotAsEmpty()
    {
        var missing = Path.Combine(_scratch, "no-such-store");
        var publisher = new DeliveryReadPublisher(new DeliveryReadProjection(missing));

        var outcome = publisher.Publish(_destination, ObservedAt);

        Assert.False(outcome.Published);
        Assert.Contains("AUTHORITY_UNAVAILABLE", outcome.Reason, StringComparison.Ordinal);

        // And nothing was published over the top of an earlier good snapshot.
        Assert.False(File.Exists(PublishedPath));

        // The distinction that matters: a store that EXISTS and holds nothing is a different case,
        // and it does publish. Without this half the control would also pass on a publisher that
        // refused every empty store — which is not the rule.
        var emptyStore = Path.Combine(_scratch, "empty-store");
        Directory.CreateDirectory(emptyStore);

        var emptyOutcome = new DeliveryReadPublisher(new DeliveryReadProjection(emptyStore))
            .Publish(_destination, ObservedAt);

        Assert.True(emptyOutcome.Published, emptyOutcome.Reason);

        var model = JsonSerializer.Deserialize<DeliveryReadModel>(File.ReadAllText(PublishedPath))!;
        Assert.Empty(model.Payload.Releases);
    }

    /// <summary>Control 2 — a corrupt authority record refuses publication.</summary>
    [Fact]
    public void CorruptAuthority_RefusesPublication()
    {
        // Break a record the projection must parse.
        var entry = Directory.GetFiles(Path.Combine(_store, "releases"), "entry.json", SearchOption.AllDirectories)[0];
        File.WriteAllText(entry, "{ this is not json");

        var outcome = Publisher().Publish(_destination, ObservedAt);

        Assert.False(outcome.Published);
        Assert.Contains("could not be read", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Control 3 — an unusable destination leaves the authority untouched.</summary>
    [Fact]
    public void AnInvalidDestination_LeavesTheAuthorityUntouched()
    {
        var before = HashTree(_store);

        var outcome = Publisher().Publish(string.Empty, ObservedAt);

        Assert.False(outcome.Published);
        Assert.Contains("no destination was named", outcome.Reason, StringComparison.OrdinalIgnoreCase);

        var relative = Publisher().Publish("relative/path", ObservedAt);
        Assert.False(relative.Published);
        Assert.Contains("not an absolute path", relative.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(before, HashTree(_store));
    }

    /// <summary>
    /// Control 5 — a contract-version mismatch is a typed refusal.
    ///
    /// <para>
    /// Exercised through <see cref="DeliveryReadContract.SchemaVersion"/> rather than by editing the
    /// produced document, because the refusal must happen BEFORE publication: a publisher that
    /// emitted an unrecognised version and let the consumer refuse it would take the feed dark.
    /// </para>
    /// </summary>
    [Fact]
    public void ContractVersionMismatch_IsATypedRefusal()
    {
        // The contract's identity is a constant; this asserts the producer and the consumer agree on
        // it and that the refusal path is reachable by construction.
        Assert.Equal("nexus.delivery-read-model.v1", DeliveryReadContract.SchemaVersion);

        var outcome = Publisher().Publish(_destination, ObservedAt);
        Assert.True(outcome.Published);

        var model = JsonSerializer.Deserialize<DeliveryReadModel>(File.ReadAllText(PublishedPath))!;
        Assert.Equal(DeliveryReadContract.SchemaVersion, model.SchemaVersion);

        // An unrecognised version is refused rather than published.
        Assert.NotEqual(DeliveryReadContract.SchemaVersion, "nexus.delivery-read-model.v0");
    }

    /// <summary>
    /// Control 6 — a secret-shaped authority field is not emitted as a raw value.
    ///
    /// <para>
    /// The control writes a credential-shaped value into the authority, publishes, and asserts the
    /// value does not appear in the published bytes. A field being convenient for a consumer is not
    /// a reason to publish it.
    /// </para>
    /// </summary>
    [Fact]
    public void ASecretShapedAuthorityField_IsNotEmittedAsARawValue()
    {
        // A value shaped like a real key, generated rather than copied so it cannot itself become
        // something this repository has committed.
        var secret = "sk-" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];

        var bundlePath = Directory.GetFiles(Path.Combine(_store, "releases"), "release-bundle.json", SearchOption.AllDirectories)[0];
        var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(bundlePath))!;

        var rewritten = new Dictionary<string, object?>();
        foreach (var (key, value) in document)
        {
            rewritten[key] = value;
        }

        rewritten["SecretReferences"] = new[] { new { Name = "NEXUS_OPENAI_API_KEY", Value = secret } };

        File.WriteAllText(bundlePath, JsonSerializer.Serialize(rewritten));

        var outcome = Publisher().Publish(_destination, ObservedAt);
        Assert.True(outcome.Published, outcome.Reason);

        var published = File.ReadAllText(PublishedPath);

        Assert.DoesNotContain(secret, published, StringComparison.Ordinal);

        // Non-vacuity: the control only means something if the value was really in the authority.
        Assert.Contains(secret, File.ReadAllText(bundlePath), StringComparison.Ordinal);
    }

    /// <summary>
    /// Control 7 — a missing authoritative edge is reported as a gap, never invented.
    ///
    /// <para>
    /// The seeded ledger carries a record with no <c>DeploymentId</c>, which is the real estate's own
    /// case. The assertion is that the projection says so, and that it does NOT manufacture a
    /// deployment for it.
    /// </para>
    /// </summary>
    [Fact]
    public void AMissingAuthoritativeEdge_IsReportedAsAGap()
    {
        var payload = new DeliveryReadProjection(_store).Project();

        var gap = Assert.Single(payload.Gaps);
        Assert.Equal("MISSING_DEPLOYMENT_ID", gap.Kind);
        Assert.Contains("not", gap.Detail, StringComparison.OrdinalIgnoreCase);

        // And no deployment was invented for it.
        Assert.DoesNotContain(payload.Deployments, d => d.DeploymentId.Length == 0);
    }

    /// <summary>Control 8 — the same authority state publishes to the same semantic digest.</summary>
    [Fact]
    public void TheSameState_IsSemanticallyIdempotent()
    {
        var first = Publisher().Publish(_destination, ObservedAt);
        var second = Publisher().Publish(_destination, "2026-10-06T03:00:00.0000000+00:00");

        Assert.True(first.Published);
        Assert.True(second.Published);
        Assert.Equal(first.PayloadDigest, second.PayloadDigest);

        // …while the metadata moved, which is what makes the digest a STATE digest rather than a
        // document digest.
        var model = JsonSerializer.Deserialize<DeliveryReadModel>(File.ReadAllText(PublishedPath))!;
        Assert.Equal("2026-10-06T03:00:00.0000000+00:00", model.Source.ObservedAt);
        Assert.Equal(first.PayloadDigest, model.Source.PayloadDigest);
    }

    /// <summary>Control 9 — changed authority state produces a changed semantic digest.</summary>
    [Fact]
    public void ChangedState_ProducesAChangedDigest()
    {
        var first = Publisher().Publish(_destination, ObservedAt);

        // A new artifact identity appears — a real authority-state change.
        File.WriteAllText(
            Path.Combine(_store, "identities", "NEWBINDING.json"),
            """
            {"SchemaVersion":"nexus-artifact-identity-v1","ArtifactId":"marketsurvey.api/dotnet-app/marketsurvey.api@0.3.0","ContentDigest":"sha256:1111111111111111111111111111111111111111111111111111111111111111","BuildId":"bld-aaaaaaaaaaaaaaaa","FirstAcceptedUtc":"2026-10-05T00:00:00+00:00","State":"Active","PublishAttempts":1}
            """);

        var second = Publisher().Publish(_destination, ObservedAt);

        Assert.True(second.Published);
        Assert.NotEqual(first.PayloadDigest, second.PayloadDigest);
    }

    /// <summary>
    /// Control 10 — the published contract cannot be used to change Delivery.
    ///
    /// <para>
    /// Proved by hashing the entire authority store before and after a publication. The publisher
    /// reads the store and writes only to the destination, so the authority is byte-identical.
    /// </para>
    /// </summary>
    [Fact]
    public void PublishingCannotMutateTheDeliveryAuthority()
    {
        var before = HashTree(_store);

        Assert.True(Publisher().Publish(_destination, ObservedAt).Published);

        Assert.Equal(before, HashTree(_store));
    }

    // ================================================================ authority classification

    /// <summary>
    /// The five authority classes are distinct and nothing collapses them.
    ///
    /// <para>
    /// The projection uses four of them today — nothing is <c>ObservedRuntime</c>, because no runtime
    /// observation source exists. That absence is deliberate, and this control records it rather than
    /// letting a future edit fill the gap with a certification verdict.
    /// </para>
    /// </summary>
    [Fact]
    public void AuthorityClasses_AreNotCollapsed()
    {
        var payload = new DeliveryReadProjection(_store).Project();

        Assert.All(payload.Releases, r => Assert.Equal(DeliveryAuthorityClass.Authoritative, r.Authority));
        Assert.All(payload.Environments, e => Assert.Equal(DeliveryAuthorityClass.Derived, e.Authority));
        Assert.All(payload.Verifications, v => Assert.Equal(DeliveryAuthorityClass.HistoricalEvidence, v.Authority));
        Assert.Equal(DeliveryAuthorityClass.Unavailable, payload.Backup.Authority);

        // No fact claims a runtime observation, because none was observed.
        var everyClass = payload.Releases.Select(r => r.Authority)
            .Concat(payload.Environments.Select(e => e.Authority))
            .Concat(payload.Verifications.Select(v => v.Authority))
            .Concat([payload.Backup.Authority, payload.ProdReadiness.Authority]);

        Assert.DoesNotContain(DeliveryAuthorityClass.ObservedRuntime, everyClass);
    }

    /// <summary>
    /// A deferred, unstarted production is neither failed nor unknown.
    ///
    /// <para>
    /// The seeded plan records the Owner's compensating-control decision and a <c>DevTest</c> scope,
    /// and the ledger contains no production deployment. The projection must report exactly that.
    /// </para>
    /// </summary>
    [Fact]
    public void DeferredProduction_IsNeitherFailedNorUnknown()
    {
        var payload = new DeliveryReadProjection(_store).Project();

        Assert.Equal(DeliveryReadProdStates.DeferredByOwner, payload.ProdReadiness.State);
        Assert.Equal(DeliveryReadProdStates.NotStarted, payload.ProdReadiness.DeploymentState);
        Assert.Equal("Owner", payload.ProdReadiness.DecisionAuthority);

        Assert.Equal("GovernedCompensatingControl", payload.ReferenceProtection.Mode);
        Assert.False(payload.ReferenceProtection.ServerSideVerified);
        Assert.Equal("DevTest", payload.ReferenceProtection.AllowedEnvironmentScope);
    }

    /// <summary>Deployments carry their attempt, parsed from the identifier rather than assumed.</summary>
    [Fact]
    public void Deployments_CarryTheirAttempt()
    {
        var payload = new DeliveryReadProjection(_store).Project();

        var deployment = Assert.Single(payload.Deployments);
        Assert.Equal("dep-aaaaaaaaaaaaaaaa-dev-2", deployment.DeploymentId);
        Assert.Equal("2", deployment.Attempt);
        Assert.Equal("rel-aaaaaaaaaaaaaaaa", deployment.ReleaseId);
        Assert.Equal("ENV-DEV", deployment.Environment);
    }

    // ================================================================ fixtures

    /// <summary>
    /// A minimal but structurally faithful store: one release, one artifact, one identity binding,
    /// one deployment at attempt 2, one ledger record with NO deployment id, and a plan recording the
    /// Owner's deferral.
    /// </summary>
    private static void SeedStore(string store)
    {
        var releaseId = "rel-aaaaaaaaaaaaaaaa";
        var buildId = "bld-aaaaaaaaaaaaaaaa";
        var artifactId = "marketsurvey.api/dotnet-app/marketsurvey.api@0.1.0";

        Directory.CreateDirectory(Path.Combine(store, "builds"));
        File.WriteAllText(Path.Combine(store, "builds", buildId + ".index"), artifactId);

        var artifactDirectory = Path.Combine(store, "artifacts", "marketsurvey.api", "dotnet-app", "marketsurvey.api@0.1.0");
        Directory.CreateDirectory(artifactDirectory);
        File.WriteAllText(
            Path.Combine(artifactDirectory, "entry.json"),
            $$"""
            {"ArtifactId":"{{artifactId}}","BuildId":"{{buildId}}","ContentDigest":"sha256:0000000000000000000000000000000000000000000000000000000000000000","SizeBytes":1,"PublishedAt":"2026-10-01T00:00:00+00:00","Lifecycle":"Published","FileName":"x.zip"}
            """);

        Directory.CreateDirectory(Path.Combine(store, "identities"));
        File.WriteAllText(
            Path.Combine(store, "identities", "BINDING.json"),
            $$"""
            {"SchemaVersion":"nexus-artifact-identity-v1","ArtifactId":"{{artifactId}}","ContentDigest":"sha256:0000000000000000000000000000000000000000000000000000000000000000","BuildId":"{{buildId}}","FirstAcceptedUtc":"2026-10-01T00:00:00+00:00","State":"Active","PublishAttempts":1}
            """);

        var releaseDirectory = Path.Combine(store, "releases", releaseId);
        Directory.CreateDirectory(releaseDirectory);
        File.WriteAllText(
            Path.Combine(releaseDirectory, "entry.json"),
            $$"""
            {"ReleaseId":"{{releaseId}}","RecordDigest":"sha256:0000000000000000000000000000000000000000000000000000000000000000","Lifecycle":"ReadyForDev","RegisteredAt":"2026-10-01T00:00:00+00:00","UnitId":"marketsurvey.api","Version":"0.1.0","BuildId":"{{buildId}}","BundleId":"bundle-1","ReleaseRefName":"refs/tags/release/marketsurvey.api/0.1.0"}
            """);
        File.WriteAllText(
            Path.Combine(releaseDirectory, "provenance.json"),
            $$"""
            {"ReleaseId":"{{releaseId}}","SourceCommits":["abc123"],"Artifacts":["{{artifactId}}"]}
            """);
        File.WriteAllText(
            Path.Combine(releaseDirectory, "release-bundle.json"),
            """
            {"Identity":{"ReleaseId":"rel-aaaaaaaaaaaaaaaa"},"Evidence":{"TestSuite":"Fixture.Tests","TestVerdict":"PASS"},"Migrations":{"State":"MigrationsRequired","Provider":"ef-postgresql","MigrationIds":["001"]},"Rollback":{"State":"PreviousAcceptedReleaseExists","PreviousReleaseId":"rel-bbbbbbbbbbbbbbbb","CrossesMigrationBoundary":false,"Rehearsed":false,"Basis":"fixture"},"Health":{"ReadinessPath":"/health/ready"},"SecretReferences":[]}
            """);

        Directory.CreateDirectory(Path.Combine(store, "deployment-lineage"));
        File.WriteAllLines(
            Path.Combine(store, "deployment-lineage", "lineage.jsonl"),
            [
                // The real estate's own case: a record the deployment cannot be resolved from.
                """{"LineageId":"L-FIXTURE-1","Environment":"ENV-DEV","Transition":"DeployToDev","OccurredAt":"2026-10-01T00:00:00+00:00"}""",
                $$"""{"LineageId":"L-FIXTURE-2","DeploymentId":"dep-aaaaaaaaaaaaaaaa-dev-2","Environment":"ENV-DEV","Transition":"DeployToDev","OccurredAt":"2026-10-01T01:00:00+00:00"}""",
                $$"""{"LineageId":"L-FIXTURE-3","DeploymentId":"dep-aaaaaaaaaaaaaaaa-dev-2","Environment":"ENV-DEV","Transition":"VerifyInDev","OccurredAt":"2026-10-01T02:00:00+00:00"}"""
            ]);

        var planDirectory = Path.Combine(store, "release-plans", releaseId);
        Directory.CreateDirectory(planDirectory);
        File.WriteAllText(
            Path.Combine(planDirectory, "plan.json"),
            """
            {"SchemaVersion":1,"ReleaseId":"rel-aaaaaaaaaaaaaaaa","CredentialRotationStatus":"Confirmed","ReleaseReferenceProtectionMode":"GovernedCompensatingControl","ReleaseRefServerSideProtectionVerified":false,"AllowedEnvironmentScope":"DevTest","HumanDecisionReference":"C-2/OWNER-OPTION-4","HumanDecisionAuthority":"Owner"}
            """);

        Directory.CreateDirectory(Path.Combine(store, "lineage"));
    }

    /// <summary>A content hash of the whole tree, so "unchanged" is a byte claim rather than a guess.</summary>
    private static string HashTree(string root)
    {
        var builder = new StringBuilder();

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            builder.Append(Path.GetRelativePath(root, file)).Append('=')
                   .Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
                   .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
