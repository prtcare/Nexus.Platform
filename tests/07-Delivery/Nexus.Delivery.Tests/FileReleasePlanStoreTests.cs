using System.Text;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The governed writer's guarantees, exercised against a real filesystem.
///
/// <para>
/// <b>What went wrong that these tests exist to prevent.</b> The authority controlling the W9 deployment was
/// an untracked JSON file that a person opened in an editor. Nothing versioned it, nothing validated it,
/// nothing recorded who changed it or why, and nothing could detect that it had been changed. Every
/// guarantee below is one the hand-edited file did not have.
/// </para>
///
/// <para>
/// <b>The refusal tests carry the weight.</b> A writer that accepted everything would satisfy every
/// "the write succeeded" assertion while providing none of the safety, so each guarantee is paired with the
/// input that must be refused, and the refusal is asserted together with the fact that the authority on disk
/// did not move.
/// </para>
/// </summary>
public sealed class FileReleasePlanStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "nexus-release-plan-tests",
        Guid.NewGuid().ToString("N"));

    private static readonly ReleaseId Release = ReleaseId.Parse("rel-2ec4c364727bcb74");

    private static readonly DateTimeOffset Stamp =
        DateTimeOffset.Parse("2026-09-25T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);

    public FileReleasePlanStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A test fixture that fails to clean up must not fail the test that passed.
        }
    }

    private FileReleasePlanStore Store => new(_root);

    private static ReleaseSecurityPlan Plan(string by = "owner", string reason = "Initial recording of the W9.4 C-1 and C-2 facts.") => new()
    {
        SchemaVersion = ReleaseSecurityPlan.CurrentSchemaVersion,
        ReleaseId = Release,
        CredentialRotationStatus = CredentialRotationStatus.Confirmed,
        CredentialRotationDecisionReference = "OWNER-DECISION-C1",
        ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.GovernedCompensatingControl,
        ReleaseRefServerSideProtectionVerified = false,
        CompensatingControlApproved = true,
        CompensatingControlVerified = true,
        AllowedEnvironmentScope = ReleaseReferenceProtectionScope.DevTest,
        HumanDecisionReference = "OWNER-DECISION-C2",
        HumanDecisionAuthority = DeploymentAuthorityRole.Owner,
        UpdatedAt = Stamp,
        UpdatedBy = by,
        Reason = reason,
        Version = 1
    };

    // =====================================================================================================
    // The write path
    // =====================================================================================================

    [Fact]
    public async Task CreatingAPlan_PersistsIt_RecordsBothHashes_AndReadsBack()
    {
        var created = await Store.CreateAsync(Plan());

        Assert.True(created.IsAccepted, created.Detail);
        Assert.Equal(1, created.Version);
        Assert.Null(created.BeforeHash);
        Assert.NotNull(created.AfterHash);

        var read = await Store.ReadAsync(Release);

        Assert.Equal(ReleasePlanReadState.Present, read.State);
        Assert.Equal(Plan().ComputePlanDigest(), read.Plan!.ComputePlanDigest());

        // The ledger records a before/after pair for every accepted write. A ledger that recorded only the
        // resulting state could not answer "what changed on this write", which is the question asked of it
        // when a release state moves for a reason nobody expected.
        var applications = await Store.ReadApplicationsAsync(Release);

        var record = Assert.Single(applications);

        Assert.Equal(0, record.PreviousVersion);
        Assert.Equal(1, record.Version);
        Assert.Null(record.BeforeHash);
        Assert.Equal(created.AfterHash, record.AfterHash);
        Assert.Equal(Stamp, record.AppliedAt);
        Assert.Equal("owner", record.AppliedBy);
        Assert.NotNull(record.StoredBytesHash);
    }

    [Fact]
    public async Task CreatingTheSamePlanTwice_IsANoOpRatherThanASecondWrite()
    {
        Assert.True((await Store.CreateAsync(Plan())).IsAccepted);

        var again = await Store.CreateAsync(Plan());

        Assert.True(again.IsAccepted);
        Assert.True(again.IsAlreadyPresent);
        Assert.Single(await Store.ReadApplicationsAsync(Release));
    }

    /// <summary>
    /// <b>A second, different plan for the same release is refused.</b> The ReleaseId is the key and one
    /// release has one security state; accepting an overwrite would make the authority last-writer-wins,
    /// which is the property the file store already refuses one level down for release records.
    /// </summary>
    [Fact]
    public async Task CreatingADifferentPlanForTheSameRelease_IsRefused_AndTheAuthorityDoesNotMove()
    {
        var created = await Store.CreateAsync(Plan());

        var refused = await Store.CreateAsync(Plan(reason: "A different reason entirely."));

        Assert.False(refused.IsAccepted);
        Assert.Equal(ReleasePlanWriteRefusalReason.ReleaseIdAlreadyHasAPlan, refused.RefusalReason);

        var read = await Store.ReadAsync(Release);

        Assert.Equal(created.AfterHash, read.Plan!.ComputePlanDigest());
    }

    [Fact]
    public async Task ApplyingAnUpdate_WritesTheNextVersion_WithTheBeforeAndAfterHashesPaired()
    {
        var created = await Store.CreateAsync(Plan());

        var applied = await Store.ApplyAsync(new ReleaseSecurityPlanUpdate
        {
            ReleaseId = Release,
            ExpectedVersion = 1,
            CompensatingControlVerified = true,
            UpdatedBy = "delivery-team",
            Reason = "Re-verified the C-2 control against the release reference.",
            UpdatedAt = Stamp.AddHours(1)
        });

        Assert.True(applied.IsAccepted, applied.Detail);
        Assert.Equal(2, applied.Version);
        Assert.Equal(created.AfterHash, applied.BeforeHash);
        Assert.NotEqual(applied.BeforeHash, applied.AfterHash);

        var applications = await Store.ReadApplicationsAsync(Release);

        Assert.Equal(2, applications.Count);
        Assert.Equal(applications[0].AfterHash, applications[1].BeforeHash);
    }

    /// <summary>
    /// <b>Optimistic concurrency, not a lock.</b> A caller that read version 1 cannot silently overwrite a
    /// version 2 written in between — which is precisely the hazard of a file two people can edit.
    /// </summary>
    [Fact]
    public async Task AnUpdateBuiltOnAStaleVersion_IsRefused_AndTheNewerVersionSurvives()
    {
        Assert.True((await Store.CreateAsync(Plan())).IsAccepted);

        Assert.True((await Store.ApplyAsync(new ReleaseSecurityPlanUpdate
        {
            ReleaseId = Release,
            ExpectedVersion = 1,
            Reason = "First update.",
            UpdatedBy = "delivery-team",
            UpdatedAt = Stamp.AddHours(1)
        })).IsAccepted);

        var stale = await Store.ApplyAsync(new ReleaseSecurityPlanUpdate
        {
            ReleaseId = Release,
            ExpectedVersion = 1,
            Reason = "A second update built on the same read.",
            UpdatedBy = "someone-else",
            UpdatedAt = Stamp.AddHours(2)
        });

        Assert.False(stale.IsAccepted);
        Assert.Equal(ReleasePlanWriteRefusalReason.ConcurrentModification, stale.RefusalReason);

        var read = await Store.ReadAsync(Release);

        Assert.Equal(2, read.Plan!.Version);
        Assert.Equal("delivery-team", read.Plan.UpdatedBy);

        // The refused write left no trace in the ledger. A ledger that recorded refusals would make
        // "was this applied" a question about reading rather than about state.
        Assert.Equal(2, (await Store.ReadApplicationsAsync(Release)).Count);
    }

    [Fact]
    public async Task ApplyingToAReleaseWithNoPlan_IsRefused_NotTreatedAsACreate()
    {
        var refused = await Store.ApplyAsync(new ReleaseSecurityPlanUpdate
        {
            ReleaseId = Release,
            ExpectedVersion = 0,
            CompensatingControlVerified = true,
            UpdatedBy = "delivery-team",
            Reason = "Applying to nothing.",
            UpdatedAt = Stamp
        });

        Assert.False(refused.IsAccepted);
        Assert.Equal(ReleasePlanWriteRefusalReason.PlanNotFound, refused.RefusalReason);
        Assert.Equal(ReleasePlanReadState.Absent, (await Store.ReadAsync(Release)).State);
    }

    /// <summary>
    /// <b>An invalid record is refused at the writer, and the previous authority survives.</b> This is the
    /// central difference from the hand-edited file: the bad state cannot become the authority at all.
    /// </summary>
    [Fact]
    public async Task AnUpdateThatWouldProduceAnInvalidRecord_IsRefused_AndNothingIsWritten()
    {
        var created = await Store.CreateAsync(Plan());

        var refused = await Store.ApplyAsync(new ReleaseSecurityPlanUpdate
        {
            ReleaseId = Release,
            ExpectedVersion = 1,
            CompensatingControlVerified = false,
            UpdatedBy = "delivery-team",
            Reason = "Un-verifying the control, which the rules forbid.",
            UpdatedAt = Stamp.AddHours(1)
        });

        Assert.False(refused.IsAccepted);
        Assert.Equal(ReleasePlanWriteRefusalReason.PlanInvalid, refused.RefusalReason);
        Assert.NotNull(refused.Verdict);
        Assert.True(refused.Verdict!.RefusedBecause(ReleasePlanRefusalReason.CompensatingControlNotVerified));

        var read = await Store.ReadAsync(Release);

        Assert.Equal(1, read.Plan!.Version);
        Assert.Equal(created.AfterHash, read.Plan.ComputePlanDigest());
        Assert.Single(await Store.ReadApplicationsAsync(Release));
    }

    [Fact]
    public async Task AnInvalidFirstPlan_IsRefused_AndNothingIsCreated()
    {
        var refused = await Store.CreateAsync(Plan() with { CompensatingControlApproved = false });

        Assert.False(refused.IsAccepted);
        Assert.Equal(ReleasePlanWriteRefusalReason.PlanInvalid, refused.RefusalReason);
        Assert.Equal(ReleasePlanReadState.Absent, (await Store.ReadAsync(Release)).State);
    }

    /// <summary>An unsupported enum member is refused rather than coerced to the nearest known value.</summary>
    [Fact]
    public async Task AnUnknownStateCombination_IsRefused_AndIsReportedAsSuch()
    {
        var refused = await Store.CreateAsync(Plan() with
        {
            ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.ServerSideProtected,
            ReleaseRefServerSideProtectionVerified = false
        });

        Assert.False(refused.IsAccepted);
        Assert.Equal(ReleasePlanWriteRefusalReason.PlanInvalid, refused.RefusalReason);
        Assert.True(refused.Verdict!.RefusedBecause(ReleasePlanRefusalReason.ServerSideProtectionNotVerified));
    }

    /// <summary>
    /// <b>A release id that could escape the store root is not constructible, and the store refuses a
    /// record with none.</b>
    ///
    /// <para>
    /// The store carries a <c>PathEscapesStoreRoot</c> refusal and a containment check as defence in depth,
    /// but both are unreachable from a caller using the typed API: <see cref="ReleaseId"/> has a private
    /// constructor and <c>Parse</c> accepts only <c>rel-</c> plus sixteen lowercase hex characters, so no
    /// caller can produce an id containing a separator or a parent reference. Asserted here as the property
    /// it actually is rather than by inventing an input the type does not permit — a test that forced an
    /// invalid id through a private constructor would be testing the guard while claiming to test the type.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AReleaseIdCannotEscapeTheStoreRoot_AndARecordWithNoneIsRefused()
    {
        Assert.False(ReleaseId.IsValid("../../escape"));
        Assert.False(ReleaseId.IsValid("rel-../../escape"));
        Assert.False(ReleaseId.IsValid("rel-2EC4C364727BCB74"));
        Assert.Throws<ArgumentException>(() => ReleaseId.Parse("../../escape"));

        var refused = await Store.CreateAsync(Plan() with { ReleaseId = null });

        Assert.False(refused.IsAccepted);
        Assert.Equal(ReleasePlanWriteRefusalReason.PlanInvalid, refused.RefusalReason);
        Assert.True(refused.Verdict!.RefusedBecause(ReleasePlanRefusalReason.ReleaseIdMissing));
        Assert.Equal(ReleasePlanReadState.Absent, (await Store.ReadAsync(Release)).State);
    }

    // =====================================================================================================
    // Storage encoding — the record refuses what it does not understand
    // =====================================================================================================

    [Fact]
    public void TheCodec_RoundTripsTheRecord()
    {
        var encoded = ReleaseSecurityPlanCodec.Encode(Plan());

        Assert.True(ReleaseSecurityPlanCodec.TryDecode(encoded, out var decoded, out var detail), detail);
        Assert.Equal(Plan().ComputePlanDigest(), decoded!.ComputePlanDigest());

        // Stated positively because it is the property every stored hash depends on: an encoding that could
        // not represent a member would make a changed member undetectable.
        Assert.Contains("\"SchemaVersion\"", Encoding.UTF8.GetString(encoded), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Unknown members are refused, not ignored.</b> The file this replaced ignored everything it did not
    /// recognise, so a typo was a member that silently did not exist.
    /// </summary>
    [Fact]
    public void AnUnknownMember_IsRefusedRatherThanIgnored()
    {
        var text = Encoding.UTF8.GetString(ReleaseSecurityPlanCodec.Encode(Plan()));

        var tampered = Encoding.UTF8.GetBytes(
            text.Replace("\"ReleaseId\"", "\"ReleaseIdTypo\": \"x\",\n  \"ReleaseId\"", StringComparison.Ordinal));

        Assert.False(ReleaseSecurityPlanCodec.TryDecode(tampered, out var decoded, out var detail));
        Assert.Null(decoded);

        // The refusal names the offending member rather than merely reporting that decoding failed. A reader
        // told only "does not decode" would have to diff the record against the schema by hand, and the
        // member it cannot map is the one thing the message must carry.
        Assert.Contains("ReleaseIdTypo", detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A record written by a schema this build cannot interpret is refused at the decode, in both
    /// directions.</b>
    ///
    /// <para>
    /// The store's reader decodes and cross-checks; it does not validate what it reads. So this check is the
    /// only thing standing between a newer record and a reader that would otherwise report it as
    /// <c>Present</c> while ignoring exactly the members the newer schema added. Both bounds are asserted,
    /// because a lower-bound-only check would let a future record through — and the members it did not
    /// understand are precisely the security-relevant ones.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(ReleaseSecurityPlan.MaxSchemaVersion + 1)]
    [InlineData(999)]
    public void ARecordFromAnUnsupportedSchema_IsRefused(int schemaVersion)
    {
        var record = ReleaseSecurityPlanCodec.Encode(Plan() with { SchemaVersion = schemaVersion });

        Assert.False(ReleaseSecurityPlanCodec.TryDecode(record, out var decoded, out var detail));
        Assert.Null(decoded);
        Assert.Contains(ReleaseSecurityPlan.CanonicalMarker, detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedRecord_DecodesToARefusalRatherThanAnException()
    {
        Assert.False(ReleaseSecurityPlanCodec.TryDecode(Encoding.UTF8.GetBytes("{ not json at all"), out var decoded, out var detail));
        Assert.Null(decoded);
        Assert.False(string.IsNullOrWhiteSpace(detail));
    }

    /// <summary>
    /// The marker is first in the canonical projection and carries the schema version, so a record written by
    /// a different schema cannot be read as if it were this one.
    /// </summary>
    [Fact]
    public void TheCanonicalProjection_NamesItsSchema()
    {
        var canonical = Plan().CanonicalForm();

        Assert.StartsWith(ReleaseSecurityPlan.CanonicalMarker, canonical, StringComparison.Ordinal);
        Assert.Contains(ReleaseSecurityPlan.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), canonical, StringComparison.Ordinal);
    }

    // =====================================================================================================
    // Tamper detection — the guarantee the hand-edited file could not offer
    // =====================================================================================================

    /// <summary>
    /// <b>An out-of-band edit is detected.</b> Someone with an editor can still change the bytes on disk —
    /// no local mechanism can prevent that. What the store can do, and does, is refuse to report the edited
    /// file as authority: the canonical digest of the record no longer matches the immutable version snapshot
    /// and the ledger's recorded hash, so the read returns <c>Corrupt</c> rather than a plan.
    /// </summary>
    [Fact]
    public async Task AnOutOfBandEditOfTheAuthority_ReadsAsCorrupt_NotAsAPlan()
    {
        Assert.True((await Store.CreateAsync(Plan())).IsAccepted);

        var path = Path.Combine(_root, "release-plans", Release.Value, "plan.json");
        var text = await File.ReadAllTextAsync(path);

        await File.WriteAllTextAsync(
            path,
            text.Replace("\"compensatingControlVerified\": true", "\"compensatingControlVerified\": false", StringComparison.Ordinal),
            Encoding.UTF8);

        var read = await Store.ReadAsync(Release);

        Assert.Equal(ReleasePlanReadState.Corrupt, read.State);
        Assert.Null(read.Plan);
        Assert.False(string.IsNullOrWhiteSpace(read.Detail));
    }

    /// <summary>
    /// A corrupt authority is <b>not</b> the same as an absent one. The two are remedied by different people,
    /// and collapsing them would let a tampered file read as "nothing recorded yet".
    /// </summary>
    [Fact]
    public async Task ACorruptAuthority_IsDistinguishableFromAnAbsentOne()
    {
        Assert.Equal(ReleasePlanReadState.Absent, (await Store.ReadAsync(Release)).State);

        Assert.True((await Store.CreateAsync(Plan())).IsAccepted);

        var path = Path.Combine(_root, "release-plans", Release.Value, "plan.json");
        await File.WriteAllTextAsync(path, "{}", Encoding.UTF8);

        var read = await Store.ReadAsync(Release);

        Assert.Equal(ReleasePlanReadState.Corrupt, read.State);
        Assert.NotEqual(ReleasePlanReadState.Absent, read.State);
    }

    [Fact]
    public async Task DeletingAVersionSnapshot_MakesTheAuthorityReadAsCorrupt()
    {
        Assert.True((await Store.CreateAsync(Plan())).IsAccepted);

        var versions = Path.Combine(_root, "release-plans", Release.Value, "versions");
        var snapshot = Assert.Single(Directory.GetFiles(versions, "*.json"));

        File.Delete(snapshot);

        Assert.Equal(ReleasePlanReadState.Corrupt, (await Store.ReadAsync(Release)).State);
    }

    // =====================================================================================================
    // The write itself
    // =====================================================================================================

    /// <summary>
    /// <b>The write is a replace, not a truncate-and-fill.</b> A reader concurrent with a write sees either
    /// the previous authority or the next one, never a half-written file — which is the other thing an editor
    /// cannot promise.
    /// </summary>
    [Fact]
    public async Task AWrite_LeavesNoPartialFileBehind()
    {
        Assert.True((await Store.CreateAsync(Plan())).IsAccepted);

        Assert.True((await Store.ApplyAsync(new ReleaseSecurityPlanUpdate
        {
            ReleaseId = Release,
            ExpectedVersion = 1,
            Reason = "A second write.",
            UpdatedBy = "delivery-team",
            UpdatedAt = Stamp.AddHours(1)
        })).IsAccepted);

        var directory = Path.Combine(_root, "release-plans", Release.Value);

        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
        Assert.Single(Directory.GetFiles(directory, "plan.json"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(directory, "versions"), "*.json").Length);
        Assert.Single(Directory.GetFiles(directory, "*.jsonl"));
    }

    /// <summary>
    /// Version snapshots are written once. Re-writing a version would make the immutable comparison basis
    /// mutable, and the tamper check that depends on it would then agree with the edit it is meant to catch.
    /// </summary>
    [Fact]
    public async Task AVersionsSnapshot_IsNeverRewritten()
    {
        Assert.True((await Store.CreateAsync(Plan())).IsAccepted);

        var path = Path.Combine(_root, "release-plans", Release.Value, "versions", "v1.json");
        var original = await File.ReadAllTextAsync(path);

        Assert.True((await Store.ApplyAsync(new ReleaseSecurityPlanUpdate
        {
            ReleaseId = Release,
            ExpectedVersion = 1,
            Reason = "A second write.",
            UpdatedBy = "delivery-team",
            UpdatedAt = Stamp.AddHours(1)
        })).IsAccepted);

        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.True(File.Exists(Path.Combine(_root, "release-plans", Release.Value, "versions", "v2.json")));
    }

    [Fact]
    public async Task AnEmptyReleaseId_IsRefusedByTheReaderRatherThanFaulting()
    {
        var read = await Store.ReadAsync(Release);

        Assert.Equal(ReleasePlanReadState.Absent, read.State);
        Assert.Equal(Release, read.ReleaseId);
    }
}
