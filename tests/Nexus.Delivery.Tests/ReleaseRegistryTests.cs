using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The release registry's immutability rule: that a release is written once, that identical content is a
/// no-op rather than a second release, and that changed content under the same id is refused.
/// </summary>
public sealed class ReleaseRegistryTests : IDisposable
{
    private readonly TempRoot _root = new("registry");

    public void Dispose() => _root.Dispose();

    private FileReleaseRegistry Registry => new(System.IO.Path.Combine(_root.Path, "store"));

    [Fact]
    public async Task Register_AcceptsAFreshRelease_AndReadsItBackUnchanged()
    {
        var registry = Registry;
        var release = ReleaseTestData.Record();

        var outcome = await registry.RegisterAsync(release);

        Assert.True(outcome.IsAccepted);
        Assert.False(outcome.IsAlreadyPresent);
        Assert.Equal(ReleaseLifecycleState.ReleaseDraft, outcome.Entry!.Lifecycle);

        var reopened = await registry.TryOpenAsync(release.ReleaseId);

        Assert.NotNull(reopened);
        Assert.Equal(release.ComputeRecordDigest(), reopened!.ComputeRecordDigest());
    }

    /// <summary>
    /// The same release registered twice is a no-op, not a refusal. Re-assembling identical inputs changes
    /// nothing, so refusing would make a retried pipeline fail while adding no protection.
    /// </summary>
    [Fact]
    public async Task Register_AcceptsIdenticalContentAsANoOp()
    {
        var registry = Registry;
        var release = ReleaseTestData.Record();

        Assert.True((await registry.RegisterAsync(release)).IsAccepted);

        var second = await registry.RegisterAsync(release);

        Assert.True(second.IsAccepted);
        Assert.True(second.IsAlreadyPresent);
        Assert.Contains("identical content", second.Detail!, StringComparison.Ordinal);
    }

    /// <summary>
    /// TASK 3's rule. The release id is derived from the identity alone, so metadata outside the identity can
    /// change without changing the id — which is exactly why the registry compares the record digest. Without
    /// that comparison, this release would silently overwrite its predecessor.
    /// </summary>
    [Fact]
    public async Task Register_RefusesTheSameReleaseIdWithChangedMetadata()
    {
        var registry = Registry;
        var original = ReleaseTestData.Record();
        var changed = ReleaseTestData.Record(configurationSchemaVersion: "runtime-config-v2");

        Assert.Equal(original.ReleaseId, changed.ReleaseId);

        Assert.True((await registry.RegisterAsync(original)).IsAccepted);

        var second = await registry.RegisterAsync(changed);

        Assert.False(second.IsAccepted);
        Assert.Equal(ReleaseRegistryRefusalReason.ReleaseIdExistsWithDifferentContent, second.RefusalReason);
        Assert.Contains("never an overwrite", second.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_RefusesAReleaseWhoseArtifactHashChanged()
    {
        var registry = Registry;
        var original = ReleaseTestData.Record();

        Assert.True((await registry.RegisterAsync(original)).IsAccepted);

        // A different artifact digest is a different identity, so this is a different release id and would
        // be accepted as a NEW release — which is the rule working, not failing. The registry's refusal is
        // for the same id with different content, asserted above; this asserts the other half: the store
        // does not quietly conflate the two.
        var changedArtifact = ReleaseTestData.Record(identity: ReleaseTestData.Identity(digestFill: 'b'));

        Assert.NotEqual(original.ReleaseId, changedArtifact.ReleaseId);
        Assert.True((await registry.RegisterAsync(changedArtifact)).IsAccepted);
        Assert.Equal(2, (await registry.ListAsync()).Count);
    }

    [Fact]
    public async Task Verify_ConfirmsAnUntouchedRelease()
    {
        var registry = Registry;
        var release = ReleaseTestData.Record();

        await registry.RegisterAsync(release);

        var verification = await registry.VerifyAsync(release.ReleaseId);

        Assert.True(verification.IsIntact);
        Assert.True(verification.RegistryContentIntact);
        Assert.Equal(ReleaseLifecycleState.ReleaseDraft, verification.Lifecycle);
    }

    /// <summary>
    /// The corruption finding, separated from absence because the two demand opposite responses: an absent
    /// release is a missing promotion input, while a tampered one means the registry's own contents changed
    /// and every release in it is suspect.
    /// </summary>
    [Fact]
    public async Task Verify_ReportsTamperedContentAsNotIntact_RatherThanAsAbsent()
    {
        var registry = Registry;
        var release = ReleaseTestData.Record();

        await registry.RegisterAsync(release);

        var recordPath = System.IO.Path.Combine(_root.Path, "store", "releases", release.ReleaseId.Value, "release-bundle.json");
        var text = await File.ReadAllTextAsync(recordPath);
        await File.WriteAllTextAsync(recordPath, text.Replace("runtime-config-v1", "runtime-config-v9", StringComparison.Ordinal));

        var verification = await registry.VerifyAsync(release.ReleaseId);

        Assert.False(verification.IsIntact);
        Assert.False(verification.RegistryContentIntact);
        Assert.NotNull(verification.RecordedDigest);
        Assert.NotNull(verification.RecomputedDigest);
        Assert.Contains("corruption finding", verification.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_ReportsAnAbsentReleaseDistinctly()
    {
        var verification = await Registry.VerifyAsync(ReleaseId.Parse("rel-" + new string('0', 16)));

        Assert.False(verification.IsIntact);
        Assert.Null(verification.RecordedDigest);
        Assert.Contains("No such release", verification.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetLifecycle_RequiresAReason_AndRecordsIt()
    {
        var registry = Registry;
        var release = ReleaseTestData.Record();

        await registry.RegisterAsync(release);

        await Assert.ThrowsAsync<ArgumentException>(
            () => registry.SetLifecycleAsync(release.ReleaseId, ReleaseLifecycleState.ReadyForDev, "   "));

        var updated = await registry.SetLifecycleAsync(
            release.ReleaseId,
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            "Credential rotation outstanding.");

        Assert.Equal(ReleaseLifecycleState.ReadyForDevPendingSecurityAction, updated.Lifecycle);
        Assert.Equal("Credential rotation outstanding.", updated.LifecycleReason);
    }

    /// <summary>Withdrawal is Owner-reserved and terminal. A new release is the remedy, which is the point of an immutable identity.</summary>
    [Fact]
    public async Task SetLifecycle_RefusesToReopenAWithdrawnRelease()
    {
        var registry = Registry;
        var release = ReleaseTestData.Record();

        await registry.RegisterAsync(release);
        await registry.SetLifecycleAsync(release.ReleaseId, ReleaseLifecycleState.Withdrawn, "Owner withdrew it.");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.SetLifecycleAsync(release.ReleaseId, ReleaseLifecycleState.ReadyForDev, "Reinstating it."));

        Assert.Contains("terminal", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_IsOrderedByReleaseId_SoAdaptersAgreeOnTheSequence()
    {
        var registry = Registry;

        await registry.RegisterAsync(ReleaseTestData.Record(identity: ReleaseTestData.Identity(digestFill: 'a')));
        await registry.RegisterAsync(ReleaseTestData.Record(identity: ReleaseTestData.Identity(digestFill: 'b')));
        await registry.RegisterAsync(ReleaseTestData.Record(identity: ReleaseTestData.Identity(digestFill: 'c')));

        var listed = await registry.ListAsync();

        Assert.Equal(3, listed.Count);
        Assert.Equal(listed.Select(e => e.ReleaseId.Value).OrderBy(v => v, StringComparer.Ordinal), listed.Select(e => e.ReleaseId.Value));
    }
}
