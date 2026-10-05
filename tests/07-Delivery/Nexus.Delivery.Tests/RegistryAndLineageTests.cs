using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The registry's immutability and the ledger's append-only discipline — the two properties that make
/// the promotion proof a control rather than a convention.
/// </summary>
public sealed class RegistryAndLineageTests : IDisposable
{
    private readonly string _root;

    public RegistryAndLineageTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus-w91-reg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private FileArtifactRegistry Registry => new(System.IO.Path.Combine(_root, "registry"));

    private FileDeploymentLineageLog Lineage => new(System.IO.Path.Combine(_root, "lineage"));

    // =============================================================================================
    // Registry — write-once
    // =============================================================================================

    [Fact]
    public async Task Register_AcceptsAFreshBundle()
    {
        var registry = Registry;
        var bundle = TestData.Bundle();

        var outcome = await registry.RegisterAsync(bundle);

        Assert.True(outcome.IsAccepted);
        Assert.NotNull(outcome.Entry);
        Assert.Equal(bundle.BundleId, outcome.Entry!.BundleId);
        Assert.True(await registry.ExistsAsync(bundle.BundleId));
    }

    /// <summary>
    /// The immutability requirement recorded in W9.0 §4: the store must refuse an overwrite, not rely on
    /// people not attempting one. This is assertion A-9 of the first proof candidate.
    /// </summary>
    [Fact]
    public async Task Register_RefusesASecondWriteOfTheSameBundleId()
    {
        var registry = Registry;
        var bundle = TestData.Bundle();

        Assert.True((await registry.RegisterAsync(bundle)).IsAccepted);

        var second = await registry.RegisterAsync(bundle);

        Assert.False(second.IsAccepted);
        Assert.Equal(RegistryRefusalReason.BundleIdAlreadyExists, second.RefusalReason);
        Assert.Contains("NEW bundle", second.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_RefusesToReplaceABundleWhoseBytesChanged()
    {
        var registry = Registry;
        var original = TestData.Bundle();

        Assert.True((await registry.RegisterAsync(original)).IsAccepted);

        // Same id, different artifact bytes — the Owner's rule says this is a NEW bundle, never an
        // overwrite, and the registry must be the thing that says so.
        var mutated = new ReleaseBundle(
            original.SchemaVersion,
            original.BundleId,
            original.CreatedAt,
            original.Source,
            original.Builder,
            [TestData.Artifact(digestFill: 'e')],
            [.. original.SharedContracts]);

        var outcome = await registry.RegisterAsync(mutated);

        Assert.False(outcome.IsAccepted);
        Assert.Equal(RegistryRefusalReason.BundleIdAlreadyExists, outcome.RefusalReason);
    }

    [Fact]
    public async Task TryOpen_RoundTripsTheBundle()
    {
        var registry = Registry;
        var bundle = TestData.Bundle();
        await registry.RegisterAsync(bundle);

        var reopened = await registry.TryOpenAsync(bundle.BundleId);

        Assert.NotNull(reopened);
        Assert.Equal(bundle.BundleId, reopened!.BundleId);
        Assert.Equal(bundle.Source.CommitSha, reopened.Source.CommitSha);
        Assert.Equal(bundle.AllArtifacts.Count(), reopened.AllArtifacts.Count());
    }

    [Fact]
    public async Task TryOpen_OfAnAbsentBundle_ReturnsNull()
    {
        Assert.Null(await Registry.TryOpenAsync(BundleId.Parse("nexus-2026.09.22-absent")));
    }

    [Fact]
    public async Task Entry_ManifestDigest_MatchesTheCodec()
    {
        var registry = Registry;
        var bundle = TestData.Bundle();
        await registry.RegisterAsync(bundle);

        var entry = await registry.TryGetEntryAsync(bundle.BundleId);

        Assert.NotNull(entry);
        Assert.Equal(BundleManifestCodec.ComputeManifestDigest(bundle), entry!.ManifestDigest);
    }

    [Fact]
    public async Task List_IsOrderedByRegistrationTimeThenId_Deterministically()
    {
        var registry = Registry;
        await registry.RegisterAsync(TestData.Bundle("nexus-2026.09.20-bbbb", commit: "b".PadRight(40, 'b')));
        await registry.RegisterAsync(TestData.Bundle("nexus-2026.09.22-aaaa", commit: "a".PadRight(40, 'a')));

        var first = await registry.ListAsync();
        var second = await registry.ListAsync();

        Assert.Equal(
            first.Select(e => e.BundleId.Value),
            second.Select(e => e.BundleId.Value));

        Assert.Equal(
            first.OrderBy(e => e.RegisteredAt).ThenBy(e => e.BundleId.Value, StringComparer.Ordinal).Select(e => e.BundleId.Value),
            first.Select(e => e.BundleId.Value));
    }

    [Fact]
    public async Task Quarantine_MarksTheEntryAndRecordsTheReason()
    {
        var registry = Registry;
        var bundle = TestData.Bundle();
        await registry.RegisterAsync(bundle);

        await registry.QuarantineAsync(bundle.BundleId, "A live credential is reachable from this commit.");

        var entry = await registry.TryGetEntryAsync(bundle.BundleId);

        Assert.True(entry!.IsQuarantined);
        Assert.Contains("live credential", entry.QuarantineReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quarantine_WithoutAReason_IsRefused()
    {
        var registry = Registry;
        var bundle = TestData.Bundle();
        await registry.RegisterAsync(bundle);

        await Assert.ThrowsAsync<ArgumentException>(() => registry.QuarantineAsync(bundle.BundleId, "   "));
    }

    [Fact]
    public async Task Quarantine_OfAnUnregisteredBundle_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Registry.QuarantineAsync(BundleId.Parse("nexus-2026.09.22-absent"), "reason"));
    }

    [Fact]
    public async Task EveryRegisteredBundle_LandsInsideTheRegistryRoot()
    {
        var registry = Registry;
        await registry.RegisterAsync(TestData.Bundle());

        var bundlesRoot = System.IO.Path.Combine(_root, "registry", "bundles");
        var directories = Directory.GetDirectories(bundlesRoot);

        var directory = Assert.Single(directories);
        Assert.StartsWith(System.IO.Path.GetFullPath(bundlesRoot), System.IO.Path.GetFullPath(directory), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("C:\\windows")]
    [InlineData("1starts-with-a-digit")]
    public void BundleId_RefusesAnythingThatCouldEscapeOrConfuseAPath(string value)
    {
        Assert.False(BundleId.IsValid(value));
        Assert.Throws<ArgumentException>(() => BundleId.Parse(value));
    }

    // =============================================================================================
    // Lineage — append-only, and L-1
    // =============================================================================================

    private DeploymentLineageRecord Record(
        string lineageId = "L-W9-0001",
        DeploymentTransition transition = DeploymentTransition.PromoteToTest,
        BundleId? bundleId = null,
        BundleId? previous = null,
        string? reason = "Promotion to ENV-TEST after verification.",
        DeploymentAuthorization? authorization = null,
        IReadOnlyList<string>? configKeys = null)
        => new(
            lineageId,
            DateTimeOffset.Parse("2026-09-22T15:41:09+00:00", System.Globalization.CultureInfo.InvariantCulture),
            transition,
            bundleId ?? TestData.Bundle().BundleId,
            PromotionState.VerifiedDev,
            transition == DeploymentTransition.Rollback ? PromotionState.RolledBack : PromotionState.DeployedTest,
            DeploymentEnvironmentId.TestEnv,
            TestData.CleanCommit,
            [TestData.Artifact()],
            new MigrationMetadata("ef-sqlserver", ["20260827064146_InitialSqlSchema"]),
            authorization ?? DeploymentAuthorization.For(DeploymentAuthorityRole.DeliveryTeam, "delivery-bot", DateTimeOffset.UnixEpoch),
            reason,
            previous,
            configKeys);

    [Fact]
    public async Task Lineage_AppendsAndReadsBack()
    {
        var log = Lineage;

        Assert.True((await log.AppendAsync(Record())).IsAppended);

        var all = await log.ReadAllAsync();

        var record = Assert.Single(all);
        Assert.Equal("L-W9-0001", record.LineageId);
        Assert.Equal(DeploymentTransition.PromoteToTest, record.Transition);
        Assert.Equal(DeploymentEnvironmentId.TestEnv, record.Environment);
    }

    /// <summary>
    /// Invariant L-3 and the estate's duplicate-refusal discipline: a re-run of a recording step must fail
    /// loudly instead of writing a second, contradictory history.
    /// </summary>
    [Fact]
    public async Task Lineage_RefusesADuplicateRecordId()
    {
        var log = Lineage;
        Assert.True((await log.AppendAsync(Record("L-W9-0007"))).IsAppended);

        var second = await log.AppendAsync(Record("L-W9-0007", reason: "A different account of the same act."));

        Assert.False(second.IsAppended);
        Assert.Contains("append-only", second.RefusalDetail!, StringComparison.Ordinal);
        Assert.Single(await log.ReadAllAsync());
    }

    [Fact]
    public async Task Lineage_ReadByBundle_IncludesRollbackRecordsThatReferenceIt()
    {
        var log = Lineage;
        var older = TestData.Bundle("nexus-2026.09.15-2b91e0", commit: "b".PadRight(40, 'b'));
        var newer = TestData.Bundle("nexus-2026.09.22-7f3a1c");

        await log.AppendAsync(Record("L-W9-0001", bundleId: older.BundleId));
        await log.AppendAsync(Record(
            "L-W9-0002",
            transition: DeploymentTransition.Rollback,
            bundleId: newer.BundleId,
            previous: older.BundleId,
            reason: "Regression observed in ENV-TEST."));

        var forOlder = await log.ReadByBundleAsync(older.BundleId);

        Assert.Equal(2, forOlder.Count);
    }

    [Fact]
    public async Task Lineage_TryGet_ReturnsTheRecordOrNull()
    {
        var log = Lineage;
        await log.AppendAsync(Record("L-W9-0042"));

        Assert.NotNull(await log.TryGetAsync("L-W9-0042"));
        Assert.Null(await log.TryGetAsync("L-W9-0099"));
    }

    // ---- L-1: no value may ever reach a lineage record -------------------------------------------------

    [Fact]
    public void Lineage_RefusesAConfigurationKeyThatIsNotNameShaped()
    {
        // Invariant L-1. A record is the most likely place a value would leak — it is a convenient place
        // to "record what we deployed with" — so the guard lives in the constructor, not in review.
        var ex = Assert.Throws<ArgumentException>(() => Record(configKeys: ["ConnectionStrings__X=Xk9Qm2Vt7Lp4Rb8N"]));

        Assert.Contains("L-1", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Xk9Qm2Vt7Lp4Rb8N", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Lineage_RefusesACredentialShapedReason()
    {
        // Generated, not a literal: the repository's own scan gate refuses credential-shaped literals in
        // test files, and it is right to — see SecretScannerTests' header.
        Assert.Throws<ArgumentException>(() => Record(reason: "rotated " + TestData.CredentialValue()));
    }

    [Fact]
    public void Lineage_RequiresAReasonAndAnActorForARollback()
    {
        Assert.Throws<ArgumentException>(() => Record(
            transition: DeploymentTransition.Rollback,
            reason: null,
            previous: TestData.Bundle("nexus-2026.09.15-2b91e0").BundleId));
    }

    [Fact]
    public void Lineage_RequiresAnOwnerForAProdPromotion()
    {
        Assert.Throws<ArgumentException>(() => new DeploymentLineageRecord(
            "L-W9-0009",
            DateTimeOffset.UnixEpoch,
            DeploymentTransition.PromoteToProd,
            TestData.Bundle().BundleId,
            PromotionState.VerifiedTest,
            PromotionState.DeployedProd,
            DeploymentEnvironmentId.ProdEnv,
            TestData.CleanCommit,
            [TestData.Artifact()],
            authorization: DeploymentAuthorization.For(DeploymentAuthorityRole.DeliveryTeam, "delivery-bot", DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public void Lineage_RefusesAMalformedRecordId()
    {
        Assert.Throws<ArgumentException>(() => Record(lineageId: "record-1"));
        Assert.Throws<ArgumentException>(() => Record(lineageId: "L-W9-"));
    }

    [Fact]
    public async Task Lineage_RoundTripsIdentityThroughTheFile()
    {
        var log = Lineage;
        var original = Record("L-W9-0055", configKeys: ["ASPNETCORE_ENVIRONMENT", "Nexus__ChatCoreApi__BaseUrl"]);

        await log.AppendAsync(original);
        var read = await log.TryGetAsync("L-W9-0055");

        Assert.NotNull(read);
        Assert.Equal(original.BundleId, read!.BundleId);
        Assert.Equal(original.SourceCommitSha, read.SourceCommitSha);
        Assert.Equal(original.ConfigurationKeys, read.ConfigurationKeys);
        Assert.Equal(original.SecretReferences.Count, read.SecretReferences.Count);
        Assert.Equal(original.Migrations.SetDigest, read.Migrations.SetDigest);
    }

    // =============================================================================================
    // Promoter — invariant I-5: a transition is not complete until it is recorded
    // =============================================================================================

    [Fact]
    public async Task Promoter_RecordsAnAllowedTransition()
    {
        var log = Lineage;
        var promoter = new DeploymentPromoter(Registry, log);

        var outcome = await promoter.TransitionAsync(
            TestData.Request(TestData.Bundle(), DeploymentTransition.Register, PromotionState.Built),
            "L-W9-0001",
            TestData.DeploymentAttemptId());

        Assert.True(outcome.IsAllowed);
        Assert.True(outcome.IsComplete);
        Assert.NotNull(outcome.LineageRecord);
        Assert.Single(await log.ReadAllAsync());
    }

    [Fact]
    public async Task Promoter_WritesNoRecordForARefusedTransition()
    {
        var log = Lineage;
        var promoter = new DeploymentPromoter(Registry, log);

        var outcome = await promoter.TransitionAsync(
            TestData.Request(TestData.Bundle(), DeploymentTransition.Register, PromotionState.Live),
            "L-W9-0001",
            TestData.DeploymentAttemptId());

        Assert.True(outcome.Decision.IsRefused);
        Assert.False(outcome.IsComplete);
        Assert.Null(outcome.LineageRecord);
        Assert.Empty(await log.ReadAllAsync());
    }

    /// <summary>
    /// Invariant I-5 stated as a test: the gate passed, and the transition still did not complete, because
    /// the ledger refused the record. Reporting this as success would be the estate's recurring defect in
    /// another form — a pass that only looks like one because the contradicting step never ran.
    /// </summary>
    [Fact]
    public async Task Promoter_ReportsIncompleteWhenTheLedgerRefusesTheRecord()
    {
        var log = Lineage;
        await log.AppendAsync(Record("L-W9-0001"));

        var promoter = new DeploymentPromoter(Registry, log);
        var outcome = await promoter.TransitionAsync(
            TestData.Request(TestData.Bundle(), DeploymentTransition.Register, PromotionState.Built),
            "L-W9-0001",
            TestData.DeploymentAttemptId());

        Assert.True(outcome.IsAllowed);
        Assert.False(outcome.IsComplete);
        Assert.Contains("append-only", outcome.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Promoter_AppliesAQuarantineToTheRegistryAsWellAsTheLedger()
    {
        var registry = Registry;
        var bundle = TestData.Bundle();
        await registry.RegisterAsync(bundle);

        var promoter = new DeploymentPromoter(registry, Lineage);
        var outcome = await promoter.TransitionAsync(
            TestData.Request(
                bundle,
                DeploymentTransition.Quarantine,
                PromotionState.Live,
                TestData.HealthyEvidence(bundle) with { Reason = "A credential is reachable from this commit." }),
            "L-W9-0001",
            TestData.DeploymentAttemptId());

        Assert.True(outcome.IsComplete);

        var entry = await registry.TryGetEntryAsync(bundle.BundleId);
        Assert.True(entry!.IsQuarantined);
    }
}
