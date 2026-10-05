using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// TASK 1 — <b><c>ArtifactId</c> → exactly one immutable payload digest.</b>
///
/// <para>
/// <b>What these tests are for.</b> The W9.5 remediation removed an uncertified payload from the governed
/// store — with a filesystem delete, because the store had no other way to express "discard this" — and a
/// materially different payload subsequently occupied the same <c>ArtifactId</c>. The binding between an id
/// and its bytes lived in <c>entry.json</c>, <i>inside</i> the directory that was removed, so removing the
/// bytes removed the evidence that the id had been used.
/// </para>
///
/// <para>
/// <b>The incident is a control in this file</b> — <see cref="DeletingThePayloadDoesNotFreeTheArtifactId"/>
/// performs exactly that removal and asserts the id is still taken. Everything else here is a mutation of
/// that invariant or its non-vacuity twin: a store that refused <i>every</i> publish would satisfy every
/// "is refused" test, so each refusal is paired with the case that must be accepted.
/// </para>
/// </summary>
public sealed class W96ArtifactIdentityTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;

    public W96ArtifactIdentityTests()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "w96-identity-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(scratch, "store");
        _source = Path.Combine(scratch, "source");

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_source);
    }

    public void Dispose()
    {
        var scratch = Path.GetDirectoryName(_root)!;

        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private FileArtifactStore Store => new(_root);

    /// <summary>Stages bytes and returns the artifact describing them, hashing what was actually written.</summary>
    private PackagedArtifact Stage(string content, string version = "0.1.0", BuildId? buildId = null)
    {
        var path = Path.Combine(_source, $"artifact-{version}-{content.GetHashCode()}.zip");
        File.WriteAllText(path, content);

        ArtifactDigest digest;
        using (var stream = File.OpenRead(path))
        {
            digest = ArtifactDigest.Compute(stream);
        }

        return new PackagedArtifact(
            ArtifactId.For(DeploymentUnitId.Parse("marketsurvey.api"), ArtifactType.DotnetApplication, "marketsurvey.api", version),
            digest,
            new FileInfo(path).Length,
            buildId ?? BuildTestData.Identity().BuildId,
            "marketsurvey.api.zip");
    }

    private string StagedPath(string content, string version = "0.1.0")
        => Path.Combine(_source, $"artifact-{version}-{content.GetHashCode()}.zip");

    private string ArtifactDirectory(string artifactIdValue)
    {
        // Mirrors the store's own layout: artifacts/<unit>/<type>/<name>@<version>.
        var id = ArtifactId.Parse(artifactIdValue);

        return Path.Combine(_root, "artifacts", id.UnitId.Value, "dotnet-app", $"{id.Name}@{id.Version}");
    }

    // =============================================================================================
    // The invariant, and the incident it comes from
    // =============================================================================================

    /// <summary>The first registration of an id is allowed. Without this, every refusal below is vacuous.</summary>
    [Fact]
    public async Task TheFirstRegistrationOfAnArtifactIdIsAllowed()
    {
        var artifact = Stage("round one");
        var outcome = await Store.PublishAsync(artifact, StagedPath("round one"));

        Assert.True(outcome.IsAccepted);
        Assert.False(outcome.IsAlreadyPresent);
        Assert.Equal(artifact.ContentDigest, outcome.Entry!.ContentDigest);
    }

    /// <summary>Same id, byte-identical payload: idempotent. A retried pipeline run succeeds; it does not fail for having already succeeded.</summary>
    [Fact]
    public async Task RepublishingIdenticalBytesIsIdempotent()
    {
        var artifact = Stage("round one");
        var path = StagedPath("round one");

        var first = await Store.PublishAsync(artifact, path);
        var second = await Store.PublishAsync(artifact, path);

        Assert.True(first.IsAccepted);
        Assert.True(second.IsAccepted);
        Assert.True(second.IsAlreadyPresent);
        Assert.Equal(first.Entry!.ContentDigest, second.Entry!.ContentDigest);
    }

    /// <summary>Same id, different payload: a typed refusal, and the reason names <i>which</i> of the two situations it is.</summary>
    [Fact]
    public async Task DifferentBytesUnderTheSameArtifactIdAreRefused()
    {
        var first = Stage("round one");
        await Store.PublishAsync(first, StagedPath("round one"));

        var second = Stage("round two");
        var outcome = await Store.PublishAsync(second, StagedPath("round two"));

        Assert.False(outcome.IsAccepted);
        Assert.Equal(ArtifactPublishRefusalReason.ArtifactIdExistsWithDifferentBytes, outcome.RefusalReason);
        Assert.Contains("changed bytes always mean a new build", outcome.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>THE W9.5 INCIDENT, AS A CONTROL.</b> Publish; delete the payload directory with a filesystem delete
    /// — which is what the W9.5 remediation did — then publish different bytes under the same id.
    ///
    /// <para>
    /// Before W9.6 this <b>succeeded</b>, because the only binding was <c>entry.json</c> inside the directory
    /// that had just been removed. It must now be refused, and the refusal must say that the binding was read
    /// from the identity record rather than from the artifact.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DeletingThePayloadDoesNotFreeTheArtifactId()
    {
        var first = Stage("round one");
        await Store.PublishAsync(first, StagedPath("round one"));

        var directory = ArtifactDirectory(first.ArtifactId.Value);
        Assert.True(Directory.Exists(directory), "the payload must have been written for this control to mean anything");

        Directory.Delete(directory, recursive: true);
        Assert.False(Directory.Exists(directory));

        var second = Stage("round two");
        var outcome = await Store.PublishAsync(second, StagedPath("round two"));

        Assert.False(outcome.IsAccepted);
        Assert.Equal(ArtifactPublishRefusalReason.ArtifactIdExistsWithDifferentBytes, outcome.RefusalReason);

        // The refusal must say WHY it could still answer: the payload is gone, so an entry.json read could not
        // have produced this. Without this assertion the control would pass against a store that merely
        // happened to have left a stale directory behind.
        Assert.Contains("does not free the id", outcome.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The non-vacuity twin of the control above: after the same deletion, the <b>same</b> bytes still
    /// publish, and the payload is repaired. A store that refused everything after a deletion would fail here.
    /// </summary>
    [Fact]
    public async Task DeletingThePayloadStillAllowsTheSameBytesBack()
    {
        var artifact = Stage("round one");
        var path = StagedPath("round one");
        await Store.PublishAsync(artifact, path);

        Directory.Delete(ArtifactDirectory(artifact.ArtifactId.Value), recursive: true);

        var outcome = await Store.PublishAsync(artifact, path);

        Assert.True(outcome.IsAccepted);
        Assert.True(outcome.IsAlreadyPresent);
        Assert.True(Directory.Exists(ArtifactDirectory(artifact.ArtifactId.Value)), "the payload should have been repaired");
    }

    // =============================================================================================
    // Withdrawal: the governed replacement for deleting a payload
    // =============================================================================================

    /// <summary>A withdrawn id is spent — never reusable, whatever happens to the payload.</summary>
    [Fact]
    public async Task AWithdrawnArtifactIdCanNeverBeWrittenToAgain()
    {
        var artifact = Stage("round one");
        var path = StagedPath("round one");
        await Store.PublishAsync(artifact, path);

        var withdrawal = await Store.WithdrawAsync(artifact.ArtifactId, "The build inputs were defective.", "w9.6-test");

        Assert.True(withdrawal.IsWithdrawn);
        Assert.False(withdrawal.PayloadRemoved);

        // The SAME bytes are refused too: withdrawal is not "these bytes are wrong", it is "this version is
        // spent". That distinction is the reason the two refusal reasons are separate.
        var outcome = await Store.PublishAsync(artifact, path);

        Assert.False(outcome.IsAccepted);
        Assert.Equal(ArtifactPublishRefusalReason.ArtifactIdWithdrawn, outcome.RefusalReason);
    }

    /// <summary>Withdrawal removes the payload only when asked, and the identity survives either way.</summary>
    [Fact]
    public async Task WithdrawalSurvivesRemovalOfThePayload()
    {
        var artifact = Stage("round one");
        var path = StagedPath("round one");
        await Store.PublishAsync(artifact, path);

        var withdrawal = await Store.WithdrawAsync(artifact.ArtifactId, "Superseded before promotion.", "w9.6-test", removePayload: true);

        Assert.True(withdrawal.IsWithdrawn);
        Assert.True(withdrawal.PayloadRemoved);
        Assert.False(Directory.Exists(ArtifactDirectory(artifact.ArtifactId.Value)));

        var outcome = await Store.PublishAsync(Stage("round two"), StagedPath("round two"));

        Assert.False(outcome.IsAccepted);
        Assert.Equal(ArtifactPublishRefusalReason.ArtifactIdWithdrawn, outcome.RefusalReason);
    }

    /// <summary>An unexplained withdrawal is refused: it is indistinguishable from an accident.</summary>
    [Theory]
    [InlineData("", "w9.6-test")]
    [InlineData("   ", "w9.6-test")]
    [InlineData("a reason", "")]
    public async Task AWithdrawalWithoutAReasonOrActorIsRefused(string reason, string by)
    {
        var artifact = Stage("round one");
        await Store.PublishAsync(artifact, StagedPath("round one"));

        var withdrawal = await Store.WithdrawAsync(artifact.ArtifactId, reason, by);

        Assert.False(withdrawal.IsWithdrawn);
        Assert.Equal(ArtifactIdentityRefusalReason.WithdrawalNotPermitted, withdrawal.Reason);
    }

    /// <summary>Withdrawing an id that was never published refuses — an unused id is not a withdrawn one.</summary>
    [Fact]
    public async Task WithdrawingAnUnpublishedArtifactIdIsRefused()
    {
        var withdrawal = await Store.WithdrawAsync(Stage("never published").ArtifactId, "reason", "w9.6-test");

        Assert.False(withdrawal.IsWithdrawn);
        Assert.Equal(ArtifactIdentityRefusalReason.WithdrawalNotPermitted, withdrawal.Reason);
    }

    // =============================================================================================
    // The version bump: a new version is a new ArtifactId, and therefore always writable
    // =============================================================================================

    /// <summary>
    /// <b>The remedy the refusal is pointing at actually works.</b> A refused publish is not a dead end: the
    /// same unit at a new version is a different <see cref="ArtifactId"/> and publishes freely. This is the
    /// non-vacuity twin of every refusal in this file, and it is the behaviour W9.5 should have taken.
    /// </summary>
    [Fact]
    public async Task ANewVersionIsANewArtifactIdAndIsAlwaysWritable()
    {
        var atOne = Stage("round one", "0.2.0");
        await Store.PublishAsync(atOne, StagedPath("round one", "0.2.0"));

        // 0.2.0 is now bound and a different payload under it is refused...
        var conflicting = await Store.PublishAsync(Stage("round two", "0.2.0"), StagedPath("round two", "0.2.0"));
        Assert.False(conflicting.IsAccepted);

        // ...and the same different payload at 0.2.1 is accepted, because that is a different identity.
        var atTwo = Stage("round two", "0.2.1");
        var bumped = await Store.PublishAsync(atTwo, StagedPath("round two", "0.2.1"));

        Assert.True(bumped.IsAccepted);
        Assert.NotEqual(atOne.ArtifactId.Value, atTwo.ArtifactId.Value);

        // Both exist, side by side, each holding its own bytes. Neither replaced the other.
        var storedOne = await Store.ResolveByArtifactIdAsync(atOne.ArtifactId);
        var storedTwo = await Store.ResolveByArtifactIdAsync(atTwo.ArtifactId);

        Assert.Equal(atOne.ContentDigest, storedOne!.ContentDigest);
        Assert.Equal(atTwo.ContentDigest, storedTwo!.ContentDigest);
    }

    /// <summary>
    /// <b>A <c>ReleaseId</c> can never resolve an <c>ArtifactId</c> ambiguously</b>, because the id resolves
    /// to exactly one digest for the life of the store.
    ///
    /// <para>
    /// The ambiguity W9.5 created was temporal: the id resolved to one payload, then to a different one. A
    /// release recorded against the first would have followed the reference to the second. This asserts the
    /// property directly — a digest recorded at first sight is still the digest resolved after every
    /// subsequent attempt, including refused ones and a deletion.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnArtifactIdResolvesToExactlyOneDigestForTheLifeOfTheStore()
    {
        var artifact = Stage("round one");
        await Store.PublishAsync(artifact, StagedPath("round one"));

        var recorded = (await Store.ResolveByArtifactIdAsync(artifact.ArtifactId))!.ContentDigest;

        // Every attempt a lane could make to change what this id means.
        await Store.PublishAsync(Stage("round two"), StagedPath("round two"));
        await Store.WithdrawAsync(artifact.ArtifactId, "attempted redefinition", "w9.6-test");
        await Store.PublishAsync(Stage("round three"), StagedPath("round three"));

        var resolved = (await Store.ResolveByArtifactIdAsync(artifact.ArtifactId))!.ContentDigest;

        Assert.Equal(recorded, resolved);
        Assert.Equal(artifact.ContentDigest, resolved);
    }

    /// <summary>
    /// The identity binding is written OUTSIDE the payload. Asserted structurally, because the whole control
    /// depends on it and a future edit that moved it back inside would keep every test above passing until
    /// the day a payload was deleted.
    /// </summary>
    [Fact]
    public async Task TheIdentityBindingLivesOutsideThePayloadDirectory()
    {
        var artifact = Stage("round one");
        await Store.PublishAsync(artifact, StagedPath("round one"));

        var identities = Path.Combine(_root, "identities");
        var payload = ArtifactDirectory(artifact.ArtifactId.Value);

        Assert.True(Directory.Exists(identities), "the identity directory must exist");
        Assert.Single(Directory.GetFiles(identities, "*.json"));
        Assert.False(Path.GetFullPath(identities).StartsWith(Path.GetFullPath(payload), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <b>TASK 9 — "an unreadable binding does not mean absent."</b>
    ///
    /// <para>
    /// The invariant is implemented in <c>FileArtifactStore.ReadIdentity</c>, which throws rather than
    /// returning <c>null</c> when a binding file exists but cannot be deserialised. The distinction it draws
    /// is the whole point of the identity record: <i>absent</i> means "this id was never used, so publishing
    /// to it is a first registration"; <i>unreadable</i> means "the only record that can answer that question
    /// is damaged". Collapsing the second into the first is precisely how W9.5's payload deletion freed an
    /// id that was already spent.
    /// </para>
    ///
    /// <para>
    /// <b>Why this control exists at all.</b> Every other invariant in this file was already covered by a
    /// named test; this one was not, so "does not mean absent" rested on a code path nothing exercised — the
    /// estate's recurring "named safety check that cannot fail" shape. It is added here rather than asserted
    /// from the report because an unexercised branch is not a proof.
    /// </para>
    ///
    /// <para>
    /// <b>Non-vacuity, first.</b> The store must refuse different bytes while the binding is still readable,
    /// so the exception below cannot be attributed to a store that had simply stopped working.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnUnreadableIdentityBindingIsNotTreatedAsAbsent()
    {
        var artifact = Stage("round one");
        await Store.PublishAsync(artifact, StagedPath("round one"));

        // Non-vacuity: the same shape of attempt succeeds in being refused while the binding is intact.
        var before = await Store.PublishAsync(Stage("round two"), StagedPath("round two"));
        Assert.False(before.IsAccepted);
        Assert.Equal(ArtifactPublishRefusalReason.ArtifactIdExistsWithDifferentBytes, before.RefusalReason);

        // Damage the only record that can answer "has this id been used?".
        var binding = Assert.Single(Directory.GetFiles(Path.Combine(_root, "identities"), "*.json"));
        File.WriteAllText(binding, "{ this is not a readable identity binding");

        var publish = async () => await Store.PublishAsync(Stage("round three"), StagedPath("round three"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(publish);

        // The failure must NAME the distinction, not merely fail - and it must not be the refusal above,
        // which would mean the binding had been read successfully and this control proved nothing.
        Assert.Contains("not treated as absent", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(artifact.ArtifactId.Value, thrown.Message, StringComparison.Ordinal);
    }
}
