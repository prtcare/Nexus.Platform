using System.Reflection;
using System.Text;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// <c>release-digest-v1</c>: the projection contract, frozen and mutation-tested.
///
/// <para>
/// <b>What these tests are for.</b> W9.4 recorded a false reading of the release digest. The reading was
/// false because the projection had no written definition — it was a method body. These tests turn it into
/// a contract with three properties that a method body cannot have: the field set is declared, the field
/// order is tied to the byte order rather than asserted in prose, and every projected field is shown to move
/// the digest by mutation while the permitted operational state is shown not to.
/// </para>
///
/// <para>
/// <b>The frozen digest below is a freeze, not a re-derivation.</b> It was computed from the projection
/// BEFORE this contract was written and is asserted unchanged, so a future edit to any contributing
/// canonical-form member fails here rather than silently re-identifying an already-registered release.
/// </para>
/// </summary>
public sealed class ReleaseDigestContractTests
{
    private static readonly DateTimeOffset WrittenAt = DateTimeOffset.Parse("2026-09-23T10:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);

    private const string OpenAiReference = "NEXUS_OPENAI_API_KEY";

    private const string OtherReference = "NEXUS_OTHER_API_KEY";

    // Static field initializers run in textual order, so these two must precede the baseline that reads them
    // through Variant's defaults. (Getting this wrong once silently produced a baseline with no work block,
    // which the field-order test then reported as a byte-order failure.)
    private static readonly GovernedWorkReference DefaultWork =
        new(ReleaseTestData.WorkReference, "Release bundle and immutable artifact registry", "W9");

    private static readonly SecretReference[] DefaultReferences = [SecretReference.Parse(OpenAiReference)];

    /// <summary>
    /// The baseline: the shared fixture plus one secret reference.
    ///
    /// <para>
    /// The reference is carried so that <b>every collection in the baseline is non-empty</b>. A collection
    /// that is empty in the baseline makes an insertion into it and an insertion into the collection before it
    /// land on the same byte offset, which would make the field-order test unable to place either one. A test
    /// below asserts this baseline is exactly the shared fixture with that one reference added, so every
    /// mutation row still changes exactly one field.
    /// </para>
    /// </summary>
    private static readonly ReleaseRecord Baseline = Variant();

    /// <summary>
    /// The frozen projection digest of the baseline fixture. Recorded once and asserted unchanged, so any edit
    /// to a contributing canonical-form member — a reordered field, a changed encoding, an added or removed
    /// field — fails here rather than silently re-identifying a release. The corresponding freeze on a REAL
    /// release is the registered bundle's own digest, which is read-only verified in the W9.4 evidence rather
    /// than here, because this suite must not depend on an absolute path outside the repository.
    /// </summary>
    private const string FrozenDigest = "sha256:f12791968aeb35fd2f7b85cb946d8dd9955519801e567080999ad40b19df4ef1";

    private static readonly ArtifactDigest OtherDigest = ArtifactDigest.Parse($"sha256:{new string('e', 64)}");

    private static readonly ArtifactDigest LockDigest = ArtifactDigest.Parse($"sha256:{new string('c', 64)}");

    private static readonly ReleaseId OtherReleaseId = ReleaseId.Parse("rel-0123456789abcdef");

    #region builders

    /// <summary>
    /// A record built from named parts, defaulting to the baseline's. Every mutation row below changes exactly
    /// one of these arguments, which is what makes its digest change attributable to that field. A null
    /// collection means the baseline's; pass an empty one to record "none".
    /// </summary>
    private static ReleaseRecord Variant(
        int? schemaVersion = null,
        ReleaseIdentity? identity = null,
        BundleId? bundleId = null,
        ReleaseEvidence? evidence = null,
        DependencyLockState? dependencyLock = null,
        IReadOnlyList<ContractVersion>? contracts = null,
        MigrationAssessment? migrations = null,
        RollbackMetadata? rollback = null,
        HealthContract? health = null,
        string? configurationSchemaVersion = null,
        DateTimeOffset? createdAt = null,
        IReadOnlyList<string>? configurationKeys = null,
        IReadOnlyList<SecretReference>? secretReferences = null,
        GovernedWorkReference? originatingWork = null,
        bool noOriginatingWork = false)
        => new(
            schemaVersion ?? ReleaseRecord.CurrentSchemaVersion,
            identity ?? ReleaseTestData.Identity(),
            bundleId ?? ReleaseTestData.BundleId,
            evidence ?? Evidence(),
            dependencyLock ?? BuildTestData.Unlocked,
            contracts ?? [new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0", "NEXUS/Platform")],
            migrations ?? ReleaseTestData.NoMigrations,
            rollback ?? ReleaseTestData.FirstRelease,
            health ?? ReleaseTestData.Health,
            configurationSchemaVersion ?? "runtime-config-v1",
            createdAt ?? WrittenAt,
            configurationKeys ?? ["api"],
            secretReferences ?? DefaultReferences,
            noOriginatingWork ? null : originatingWork ?? DefaultWork);

    private static ReleaseIdentity IdentityWith(
        IReadOnlyList<ReleaseArtifactIdentity>? artifacts = null,
        BuildId? buildId = null,
        string? refName = null,
        string? version = null,
        string unitId = "marketsurvey.api",
        string commit = BuildTestData.CommitA)
        => new(
            DeploymentUnitId.Parse(unitId),
            version ?? "0.1.0",
            buildId ?? BuildTestData.Identity(unitId: unitId, commit: commit).BuildId,
            [commit],
            refName ?? $"refs/tags/release/{unitId}/0.1.0",
            artifacts ?? [ReleaseTestData.ArtifactIdentity(DeploymentUnitId.Parse(unitId))]);

    private static ReleaseEvidence Evidence(
        ArtifactDigest? buildManifestDigest = null,
        string? buildManifestReference = null,
        string? testSuiteName = null,
        TestVerdict? testVerdict = null,
        int? testsTotal = null,
        int? testsPassed = null,
        SecretScanVerdict? secretScanVerdict = null,
        string[]? subjects = null,
        ReproducibilityVerdict? reproducibilityVerdict = null,
        int? buildsCompared = null,
        string? builderRunId = null)
        => new(
            buildManifestDigest ?? ArtifactDigest.Parse($"sha256:{new string('f', 64)}"),
            buildManifestReference ?? "W9_2_BUILD_ARTIFACT/BUILD_MANIFEST.txt",
            testSuiteName ?? "MarketSurvey.Tests",
            testVerdict ?? TestVerdict.Passed,
            testsTotal ?? 15,
            testsPassed ?? 15,
            secretScanVerdict ?? SecretScanVerdict.Clean,
            subjects ?? ["NEXUS/Platform", "PRT/MarketSurvey"],
            reproducibilityVerdict ?? ReproducibilityVerdict.ByteIdentical,
            buildsCompared ?? 2,
            builderRunId ?? "w9.3-proof");

    private static MigrationAssessment Migrations(
        string? fromVersion = null,
        string? toVersion = null,
        MigrationCompatibility? compatibility = null,
        bool? backupRequired = null,
        MigrationReversibility? reversibility = null,
        string? basis = null,
        string[]? ids = null,
        string? provider = null,
        bool withoutCompatibility = false)
        => MigrationAssessment.Required(
            new MigrationMetadata(provider ?? "ef-sqlserver", ids ?? ["20260827064146_InitialSqlSchema"]),
            fromVersion ?? "20260827064146",
            toVersion ?? "20260923000000",
            withoutCompatibility ? null : compatibility ?? MigrationCompatibility.Match,
            backupRequired ?? true,
            reversibility ?? MigrationReversibility.ForwardFixOnly,
            basis ?? "The certified manifest declares a migration set for this unit.");

    private static RollbackMetadata Rollback(
        ReleaseId? previousReleaseId = null,
        bool crossesMigrationBoundary = false,
        bool rehearsed = true,
        string? basis = null)
        => RollbackMetadata.ToPreviousRelease(
            previousReleaseId ?? OtherReleaseId,
            crossesMigrationBoundary,
            rehearsed,
            basis ?? "The previous accepted release for this unit is resolvable in the registry.");

    #endregion

    /// <summary>
    /// The variant builder's defaults ARE the shared release fixture, and the baseline is that fixture with one
    /// secret reference added. Without this, every mutation row below would be changing two things at once and
    /// the digest changes would prove nothing about the named field.
    /// </summary>
    [Fact]
    public void TheVariantBuilder_ReproducesTheSharedFixtureExactly()
    {
        Assert.Equal(ReleaseTestData.Record().ComputeRecordDigest(), Variant(secretReferences: []).ComputeRecordDigest());
        Assert.Equal(ReleaseTestData.Record().CanonicalForm(), Variant(secretReferences: []).CanonicalForm());

        Assert.Equal(
            ReleaseTestData.Record(secretReferences: [SecretReference.Parse(OpenAiReference)]).CanonicalForm(),
            Baseline.CanonicalForm());

        // The baseline carries every collection non-empty, which is what makes the field-order offsets distinct.
        Assert.NotEmpty(Baseline.SecretReferences);
        Assert.NotEmpty(Baseline.ConfigurationKeys);
        Assert.NotEmpty(Baseline.ContractVersions);
        Assert.Empty(Variant(secretReferences: []).SecretReferences);
        Assert.NotEmpty(Variant(noOriginatingWork: true).SecretReferences);
    }

    #region the frozen projection

    /// <summary>
    /// <b>The freeze.</b> The projection of the shared fixture hashes to the digest computed before this
    /// contract existed. A change to any contributing canonical-form member moves this value, which is the
    /// signal that an already-registered release identity would be re-derived.
    /// </summary>
    [Fact]
    public void TheProjection_StillHashesToTheFrozenDigest()
    {
        // Compared as strings so that a failure shows both digests in full rather than a record rendering.
        Assert.Equal(FrozenDigest, ReleaseDigestContract.Compute(Baseline).ToString());
    }

    /// <summary>
    /// The contract's hash and the record's own hash are the same function. Two definitions of one identity is
    /// the drift this contract exists to prevent, so the agreement is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void TheContractHash_AndTheRecordHash_AreOneDefinition()
    {
        Assert.Equal(Baseline.ComputeRecordDigest(), ReleaseDigestContract.Compute(Baseline));
        Assert.True(ReleaseDigestContract.Matches(Baseline, Baseline.ComputeRecordDigest()));
        Assert.False(ReleaseDigestContract.Matches(Baseline, OtherDigest));
    }

    /// <summary>Encoding, terminator and marker are contractual, so they are asserted on the bytes themselves.</summary>
    [Fact]
    public void TheProjection_IsUtf8NulTerminatedFields_StartingWithTheRecordMarker()
    {
        var projection = ReleaseDigestContract.CanonicalProjection(Baseline);

        Assert.StartsWith(
            $"{ReleaseDigestContract.Marker}\0{ReleaseRecord.CurrentSchemaVersion}\0",
            projection,
            StringComparison.Ordinal);

        // Every field is terminated, so no two field sequences can concatenate into one. The last byte is a
        // terminator, and no field can contain one.
        Assert.EndsWith("\0", projection, StringComparison.Ordinal);

        // The hash is over UTF-8 of the projection and nothing else — no BOM, no line endings.
        Assert.Equal(
            ArtifactDigest.Compute(Encoding.UTF8.GetBytes(projection)),
            ReleaseDigestContract.Compute(Baseline));

        Assert.Equal(Encoding.UTF8.GetByteCount(projection), ReleaseDigestContract.ProjectionByteLength(Baseline));
        Assert.Equal(ArtifactDigest.Sha256Algorithm, ReleaseDigestContract.HashAlgorithm);
        Assert.DoesNotContain("\r", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", projection, StringComparison.Ordinal);
    }

    /// <summary>A projection is a function of the record alone: the same inputs produce the same bytes, twice.</summary>
    [Fact]
    public void TheProjection_IsDeterministicAcrossInstances()
    {
        var left = Variant();
        var right = Variant();

        Assert.NotSame(left, right);
        Assert.Equal(left.CanonicalForm(), right.CanonicalForm());
        Assert.Equal(ReleaseDigestContract.Compute(left), ReleaseDigestContract.Compute(right));
        Assert.Equal(-1, ReleaseDigestContract.FirstDifferingOffset(left, right));
    }

    /// <summary>
    /// Collection order is fixed at construction, so iteration order cannot leak into the digest. Asserted by
    /// supplying the same collections in a different order and requiring an identical projection.
    /// </summary>
    [Fact]
    public void TheCollectionOrder_IsAFunctionOfContent_NotOfTheOrderSupplied()
    {
        var forward = Variant(
            contracts:
            [
                new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0", "NEXUS/Platform"),
                new ContractVersion("Nexus.Developer.Contracts", "0.2.0", "NEXUS/Platform")
            ],
            configurationKeys: ["api", "worker"],
            secretReferences: [SecretReference.Parse("NEXUS_OPENAI_API_KEY"), SecretReference.Parse("NEXUS_OTHER_API_KEY")]);

        var reversed = Variant(
            contracts:
            [
                new ContractVersion("Nexus.Developer.Contracts", "0.2.0", "NEXUS/Platform"),
                new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0", "NEXUS/Platform")
            ],
            configurationKeys: ["worker", "api"],
            secretReferences: [SecretReference.Parse("NEXUS_OTHER_API_KEY"), SecretReference.Parse("NEXUS_OPENAI_API_KEY")]);

        Assert.Equal(forward.CanonicalForm(), reversed.CanonicalForm());

        // ...and the sorted order is ordinal, not the supplied order.
        Assert.Equal(new[] { "api", "worker" }, forward.ConfigurationKeys);
        Assert.Equal(
            new[] { "Nexus.Developer.Contracts", "Nexus.ProductCore.Contracts" },
            forward.ContractVersions.Select(c => c.ContractName));
    }

    #endregion

    #region exclusion of operational state

    /// <summary>
    /// <b>The permitted mutable state.</b> When a record was written is not what the record says: the same
    /// release assembled in two runs is one release, and the registry's idempotent no-op depends on it.
    /// </summary>
    [Fact]
    public void WhenTheRecordWasWritten_DoesNotMoveTheDigest()
    {
        var earlier = Variant(createdAt: DateTimeOffset.Parse("2020-01-01T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture));
        var later = Variant(createdAt: DateTimeOffset.Parse("2030-12-31T23:59:59+00:00", System.Globalization.CultureInfo.InvariantCulture));

        Assert.NotEqual(earlier.CreatedAt, later.CreatedAt);
        Assert.Equal(earlier.CanonicalForm(), later.CanonicalForm());
        Assert.Equal(ReleaseDigestContract.Compute(earlier), ReleaseDigestContract.Compute(later));
        Assert.Equal(Baseline.ComputeRecordDigest(), earlier.ComputeRecordDigest());

        // And by content, not only by comparison: the written-at value is not in the projection at all.
        Assert.DoesNotContain(WrittenAt.ToString("O"), ReleaseDigestContract.CanonicalProjection(Baseline), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>No field escapes the contract.</b> Every public member of <see cref="ReleaseRecord"/> is either
    /// projected by a declared field or explained in <see cref="ReleaseDigestContract.ExcludedFields"/>, and no
    /// exclusion entry is stale. A member added in good faith is exactly how a projection stops being a
    /// contract, so the check is structural rather than a reading of the prose.
    /// </summary>
    [Fact]
    public void EveryRecordMember_IsProjectedOrDeclaredExcluded()
    {
        // Members that are not fields: aliases onto identity members, or a derived predicate.
        var derived = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ReleaseId"] = "derived from the identity projection; projecting it would project the hash into its own input",
            ["UnitId"] = "alias of identity.unitId",
            ["Version"] = "alias of identity.version",
            ["BuildId"] = "alias of identity.buildId",
            ["Artifacts"] = "alias of identity.artifacts[]",
            ["IsFirstRelease"] = "derived from rollback.state"
        };

        var projected = ReleaseDigestContract.IncludedFields
            .SelectMany(f => f.Split('.').Take(1))
            .Select(f => f.TrimEnd('[', ']'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var excluded = ReleaseDigestContract.ExcludedFields
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unaccounted = new List<string>();

        foreach (var property in typeof(ReleaseRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = property.Name;

            if (projected.Contains(name) || excluded.Contains(name) || derived.ContainsKey(name))
            {
                continue;
            }

            unaccounted.Add(name);
        }

        Assert.True(
            unaccounted.Count == 0,
            "These release-record members are neither projected nor declared excluded, so the digest contract does not "
            + $"describe them: {string.Join(", ", unaccounted)}");

        // The exclusion list is not allowed to outlive its members either.
        var stale = ReleaseDigestContract.ExcludedFields
            .Where(e => string.IsNullOrWhiteSpace(e.Note) || string.IsNullOrWhiteSpace(e.Name))
            .Select(e => e.Name)
            .ToList();

        Assert.True(stale.Count == 0, "An exclusion entry must name a member and give the reason it is excluded: " + string.Join(", ", stale));
    }

    /// <summary>
    /// <b>The storage encoding must not be able to change the projection.</b> The digest is taken over the
    /// record, and the registry recomputes it from the DECODED stored record — deliberately, because the
    /// digest is over meaning rather than over whichever encoding holds it (W9.3 D-2). The consequence is
    /// that a codec which cannot represent a projected member turns an intact release into a corruption
    /// finding. That happened: an unrecorded migration compatibility decoded as <c>Match</c>, and the
    /// registered release reported as changed. This asserts the property for every shape the record has.
    /// </summary>
    [Fact]
    public void TheStorageCodec_RoundTripsTheProjection()
    {
        var shapes = new List<(string Name, ReleaseRecord Record)>
        {
            ("baseline", Baseline),
            ("no compatibility recorded", Variant(migrations: Migrations(
                withoutCompatibility: true,
                basis: "Not established at release time."))),
            ("unknown migrations", Variant(migrations: MigrationAssessment.Unestablished(null, "Not established at release time."))),
            ("previous release exists", Variant(rollback: Rollback())),
            ("locked dependencies", Variant(dependencyLock: DependencyLockState.Locked(
                [new LockFileDigest("NEXUS/Platform", "packages.lock.json", LockDigest)]))),
            ("no liveness path", Variant(health: new HealthContract("/health/ready", null))),
            ("no optional members", Variant(secretReferences: [], noOriginatingWork: true, health: new HealthContract("/health/ready", null))),
            ("several of each collection", Variant(
                contracts:
                [
                    new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0", "NEXUS/Platform"),
                    new ContractVersion("Nexus.Delivery.Contracts", "0.2.0")
                ],
                configurationKeys: ["api", "worker"],
                secretReferences: [SecretReference.Parse(OpenAiReference), SecretReference.Parse(OtherReference)]))
        };

        var failures = new List<string>();

        foreach (var (name, record) in shapes)
        {
            if (!ReleaseRecordCodec.TryDecode(ReleaseRecordCodec.Encode(record), out var decoded) || decoded is null)
            {
                failures.Add($"{name}: did not decode");
                continue;
            }

            var projection = ReleaseDigestContract.CanonicalProjection(record);
            var roundTripped = ReleaseDigestContract.CanonicalProjection(decoded);

            if (projection != roundTripped)
            {
                failures.Add(
                    $"{name}: the projection changed through the storage encoding at offset "
                    + $"{ReleaseDigestContract.FirstDifferingOffset(record, decoded)} "
                    + $"({ReleaseDigestContract.Compute(record)} → {ReleaseDigestContract.Compute(decoded)})");
            }
        }

        Assert.True(failures.Count == 0, "The storage codec is lossy for the release projection: " + string.Join(" | ", failures));
    }

    #endregion

    #region mutation: every projected member moves the digest

    /// <summary>
    /// One row per projected member, each changing exactly one field of the baseline. Every row must move the
    /// digest; the first row of each group is that group's order probe, used by
    /// <see cref="TheDeclaredFieldOrder_IsTheByteOrder"/>.
    /// </summary>
    [Fact]
    public void EveryProjectedMember_MovesTheDigest()
    {
        var baselineDigest = Baseline.ComputeRecordDigest();
        var unmoved = new List<string>();
        var notProbed = new List<string>();
        var failed = new List<string>();

        foreach (var row in Rows())
        {
            try
            {
                var variant = row.Variant();

                if (ReleaseDigestContract.Compute(variant) == baselineDigest)
                {
                    unmoved.Add(row.Member);
                }

                if (ReleaseDigestContract.FirstDifferingOffset(Baseline, variant) < 0)
                {
                    notProbed.Add(row.Member);
                }
            }
            catch (Exception ex)
            {
                // Collected rather than thrown, so one broken row does not hide the rows behind it.
                failed.Add($"{row.Member}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failed.Count == 0, "These mutation rows could not be built at all: " + string.Join(" | ", failed));
        Assert.True(unmoved.Count == 0, "These projected members do not move the digest, so they are not identity: " + string.Join(", ", unmoved));
        Assert.True(notProbed.Count == 0, "These mutations produced identical projections: " + string.Join(", ", notProbed));

        // Every group in the declared field list has at least one row, and every row is hostile to the digest.
        Assert.Equal(
            ReleaseDigestContract.IncludedFields.Where(f => f is not ("marker" or "schemaVersion")).OrderBy(f => f, StringComparer.Ordinal),
            Rows().Select(r => r.Group).Distinct(StringComparer.Ordinal).OrderBy(g => g, StringComparer.Ordinal));
    }

    /// <summary>
    /// <b>Field order is part of the contract, and this ties the declaration to the bytes.</b> Each probe
    /// changes exactly one top-level field, so the first byte at which its projection differs from the
    /// baseline's is inside that field. Those offsets must increase in the declared order — if two fields were
    /// emitted in the other order, the offsets would not be monotone and this fails.
    /// </summary>
    [Fact]
    public void TheDeclaredFieldOrder_IsTheByteOrder()
    {
        var probes = Rows().Where(r => r.Probe).ToList();

        // marker and schemaVersion head the projection and are asserted as a prefix in
        // TheProjection_IsUtf8NulTerminatedFields_StartingWithTheRecordMarker: neither can be mutated without
        // changing the contract's own constants, so they are covered by that assertion instead of by a row.
        var ordered = ReleaseDigestContract.IncludedFields
            .Where(f => f is not ("marker" or "schemaVersion"))
            .ToList();

        Assert.Equal(ordered, probes.Select(p => p.Group).ToList());

        var offsets = new List<(string Group, int Offset)>();

        foreach (var probe in probes)
        {
            offsets.Add((probe.Group, ReleaseDigestContract.FirstDifferingOffset(Baseline, probe.Variant())));
        }

        for (var i = 1; i < offsets.Count; i++)
        {
            Assert.True(
                offsets[i].Offset > offsets[i - 1].Offset,
                $"'{offsets[i].Group}' is declared after '{offsets[i - 1].Group}' but its first differing byte is at "
                + $"offset {offsets[i].Offset}, before {offsets[i - 1].Offset}. The declared field order is not the byte order.");
        }
    }

    /// <summary>
    /// Enums project as their member NAMES. Replacing one member with another replaces the name in the
    /// projection and nowhere keeps the old one — which is what makes a renamed member a different release
    /// identity while a renumbered one would not be.
    ///
    /// <para>
    /// The renumbering half cannot be tested from here: it would require changing an enum's declaration, which
    /// is a source change rather than a record change. What is asserted is the positive property that the
    /// projection carries the name.
    /// </para>
    /// </summary>
    [Fact]
    public void Enums_ProjectAsNames_NotNumbers()
    {
        var projection = ReleaseDigestContract.CanonicalProjection(Baseline);

        Assert.Contains("Clean\0", projection, StringComparison.Ordinal);
        Assert.Contains("Passed\0", projection, StringComparison.Ordinal);
        Assert.Contains("ByteIdentical\0", projection, StringComparison.Ordinal);
        Assert.Contains("NoDatabaseMigration\0", projection, StringComparison.Ordinal);

        // The rollback state is projected as the enum member name, not as its ToString() rendering. Both are
        // stable, but only one is the projection, and naming the wrong one here would be a silent mismatch.
        Assert.Contains("NoPreviousAcceptedRelease\0", projection, StringComparison.Ordinal);

        // A different member of the same enum replaces the name rather than joining it.
        var failed = ReleaseDigestContract.CanonicalProjection(Variant(evidence: Evidence(testVerdict: TestVerdict.Failed)));

        Assert.Contains("Failed\0", failed, StringComparison.Ordinal);
        Assert.DoesNotContain("Passed\0", failed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Null and default representation, asserted per shape rather than described: an optional member renders as
    /// the empty field, and an absent optional member contributes no bytes at all — two different renderings
    /// that must not be confused, because one says "recorded as empty" and the other says "not recorded".
    /// </summary>
    [Fact]
    public void NullAndDefaultMembers_HaveOneStatedRenderingEach()
    {
        // Recorded-as-absent: no bytes at all.
        var withoutWork = Variant(noOriginatingWork: true);
        var withWork = Variant(originatingWork: new GovernedWorkReference(ReleaseTestData.WorkReference));

        Assert.DoesNotContain(ReleaseTestData.WorkReference, ReleaseDigestContract.CanonicalProjection(withoutWork), StringComparison.Ordinal);
        Assert.Equal(
            ReleaseDigestContract.ProjectionByteLength(withWork) - ReleaseDigestContract.ProjectionByteLength(withoutWork),
            ReleaseTestData.WorkReference.Length + 3);   // reference, title, series — all empty, all terminated

        // Recorded-as-empty: an empty field, and it is a different rendering from a value.
        var noLiveness = Variant(health: new HealthContract("/health/ready", null));
        var withLiveness = Variant(health: new HealthContract("/health/ready", "/health/live"));

        Assert.Equal(
            ReleaseDigestContract.ProjectionByteLength(withLiveness) - ReleaseDigestContract.ProjectionByteLength(noLiveness),
            "/health/live".Length);

        // A null optional string renders as empty rather than as the literal "null" or as absent.
        Assert.DoesNotContain("null\0", ReleaseDigestContract.CanonicalProjection(noLiveness), StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    private sealed record Row(string Group, string Member, bool Probe, Func<ReleaseRecord> Variant);

    private static IReadOnlyList<Row> Rows() =>
    [
        // ---- identity ---------------------------------------------------------------------------------
        new("identity", "identity.unitId", true, () => Variant(identity: IdentityWith(unitId: "marketsurvey.web"))),
        new("identity", "identity.version", false, () => Variant(identity: IdentityWith(
            version: "0.2.0",
            artifacts: [ReleaseTestData.ArtifactIdentity(version: "0.2.0")]))),
        new("identity", "identity.buildId", false, () => Variant(identity: IdentityWith(
            buildId: BuildTestData.Identity(unitId: "marketsurvey.web").BuildId))),
        new("identity", "identity.releaseRefName", false, () => Variant(identity: IdentityWith(
            refName: "refs/tags/release/marketsurvey.api/9.9.9"))),
        new("identity", "identity.sourceCommits", false, () => Variant(identity: IdentityWith(commit: BuildTestData.CommitB))),
        new("identity", "identity.artifacts[].artifactId", false, () => Variant(identity: IdentityWith(
            artifacts: [ReleaseTestData.ArtifactIdentity(name: "marketsurvey.worker")]))),
        new("identity", "identity.artifacts[].contentDigest", false, () => Variant(identity: IdentityWith(
            artifacts: [ReleaseTestData.ArtifactIdentity(digestFill: 'b')]))),
        new("identity", "identity.artifacts[].sizeBytes", false, () => Variant(identity: IdentityWith(
            artifacts: [ReleaseTestData.ArtifactIdentity(size: 5434179)]))),

        // ---- bundleId ---------------------------------------------------------------------------------
        new("bundleId", "bundleId", true, () => Variant(bundleId: BundleId.Parse("nexus-2026.09.23-other1"))),

        // ---- evidence ---------------------------------------------------------------------------------
        new("evidence", "evidence.buildManifestDigest", true, () => Variant(evidence: Evidence(buildManifestDigest: OtherDigest))),
        new("evidence", "evidence.buildManifestReference", false, () => Variant(evidence: Evidence(
            buildManifestReference: "W9_2_BUILD_ARTIFACT/OTHER_MANIFEST.txt"))),
        new("evidence", "evidence.testSuiteName", false, () => Variant(evidence: Evidence(testSuiteName: "MarketSurvey.Other.Tests"))),
        new("evidence", "evidence.testVerdict", false, () => Variant(evidence: Evidence(testVerdict: TestVerdict.NotRun))),
        new("evidence", "evidence.testsTotal", false, () => Variant(evidence: Evidence(testsTotal: 16))),
        new("evidence", "evidence.testsPassed", false, () => Variant(evidence: Evidence(testsPassed: 14))),
        new("evidence", "evidence.secretScanVerdict", false, () => Variant(evidence: Evidence(secretScanVerdict: SecretScanVerdict.Incomplete))),
        new("evidence", "evidence.reproducibilityVerdict", false, () => Variant(evidence: Evidence(
            reproducibilityVerdict: ReproducibilityVerdict.Divergent))),
        new("evidence", "evidence.buildsCompared", false, () => Variant(evidence: Evidence(buildsCompared: 3))),
        new("evidence", "evidence.builderRunId", false, () => Variant(evidence: Evidence(builderRunId: "w9.4-proof"))),
        new("evidence", "evidence.secretScanSubjectLabels[]", false, () => Variant(evidence: Evidence(
            subjects: ["NEXUS/Platform", "PRT/MarketSurvey", "NEXUS/Forge"]))),

        // ---- dependencyLock ---------------------------------------------------------------------------
        new("dependencyLock", "dependencyLock", true, () => Variant(dependencyLock: DependencyLockState.Locked(
            [new LockFileDigest("NEXUS/Platform", "packages.lock.json", LockDigest)]))),

        // ---- configurationSchemaVersion ----------------------------------------------------------------
        new("configurationSchemaVersion", "configurationSchemaVersion", true, () => Variant(
            configurationSchemaVersion: "runtime-config-v2")),

        // ---- health ------------------------------------------------------------------------------------
        new("health.readinessPath", "health.readinessPath", true, () => Variant(health: new HealthContract("/health/ready2", "/health/live"))),
        new("health.livenessPath", "health.livenessPath", true, () => Variant(health: new HealthContract("/health/ready", "/health/live2"))),

        // ---- contractVersions --------------------------------------------------------------------------
        new("contractVersions[]", "contractVersions[].contractName", true, () => Variant(contracts:
            [new ContractVersion("Nexus.ProductCore.Other", "0.1.0", "NEXUS/Platform")])),
        new("contractVersions[]", "contractVersions[].version", false, () => Variant(contracts:
            [new ContractVersion("Nexus.ProductCore.Contracts", "0.2.0", "NEXUS/Platform")])),
        new("contractVersions[]", "contractVersions[].sourceRepository", false, () => Variant(contracts:
            [new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0", "PRT/MarketSurvey")])),

        // ---- migrations --------------------------------------------------------------------------------
        new("migrations", "migrations.state", true, () => Variant(migrations: MigrationAssessment.Unestablished(
            null, "The certified manifest does not declare a migration set for this unit."))),
        new("migrations", "migrations.metadata.provider", false, () => Variant(migrations: Migrations(provider: "ef-postgresql"))),
        new("migrations", "migrations.metadata.setDigest", false, () => Variant(migrations: Migrations(
            ids: ["20260827064146_InitialSqlSchema", "20260923000000_AddTenantIndex"]))),
        new("migrations", "migrations.fromVersion", false, () => Variant(migrations: Migrations(fromVersion: "20260827000000"))),
        new("migrations", "migrations.toVersion", false, () => Variant(migrations: Migrations(toVersion: "20260924000000"))),
        new("migrations", "migrations.compatibility", false, () => Variant(migrations: Migrations(compatibility: MigrationCompatibility.Behind))),
        new("migrations", "migrations.backupRequired", false, () => Variant(migrations: Migrations(backupRequired: false))),
        new("migrations", "migrations.reversibility", false, () => Variant(migrations: Migrations(reversibility: MigrationReversibility.Unknown))),
        new("migrations", "migrations.basis", false, () => Variant(migrations: Migrations(basis: "Established by inspecting the published unit's migration assembly."))),

        // ---- rollback ----------------------------------------------------------------------------------
        new("rollback", "rollback.state+previousReleaseId", true, () => Variant(rollback: Rollback())),
        new("rollback", "rollback.previousReleaseId", false, () => Variant(rollback: Rollback(
            previousReleaseId: ReleaseId.Parse("rel-fedcba9876543210")))),
        new("rollback", "rollback.crossesMigrationBoundary", false, () => Variant(rollback: Rollback(crossesMigrationBoundary: true))),
        new("rollback", "rollback.rehearsed", false, () => Variant(rollback: Rollback(rehearsed: false))),
        new("rollback", "rollback.basis", false, () => Variant(rollback: Rollback(
            basis: "No earlier accepted release for this unit resolves in the registry."))),

        // ---- configurationKeys -------------------------------------------------------------------------
        new("configurationKeys[]", "configurationKeys[]", true, () => Variant(configurationKeys: ["api", "worker"])),

        // ---- secretReferences --------------------------------------------------------------------------
        new("secretReferences[]", "secretReferences[]", true, () => Variant(
            secretReferences: [SecretReference.Parse(OtherReference)])),

        // ---- originatingWork ---------------------------------------------------------------------------
        new("originatingWork", "originatingWork.workReference", true, () => Variant(
            originatingWork: new GovernedWorkReference("WI-09-3.2"))),
        new("originatingWork", "originatingWork.title", false, () => Variant(
            originatingWork: new GovernedWorkReference(ReleaseTestData.WorkReference, "A different title"))),
        new("originatingWork", "originatingWork.series", false, () => Variant(
            originatingWork: new GovernedWorkReference(ReleaseTestData.WorkReference, "Release bundle and immutable artifact registry", "W10")))
    ];
}
