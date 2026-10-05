using Nexus.DevelopmentControl.Safety;
using Nexus.ProductCore.Contracts.DevelopmentControl;
using Nexus.ProductCore.Core.DevelopmentControl;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// The shared lock's mutual exclusion must be a property the COMPONENT enforces, not a convention every
/// caller must independently keep.
///
/// <para><b>The defect these tests close.</b> The lock's file name came from the canonical store
/// identity, but its <i>directory</i> came from the caller. Two callers naming two directories therefore
/// produced two unrelated files for one logical lock — neither visible to the other — and both would
/// hold "the lock". This was measured before the fix, on the live authority: two independent files
/// (different inodes, link count 1 each) carrying the same 64-character identity, in
/// <c>DevelopmentControl\locks\</c> and <c>DevelopmentControl\.lock\</c>.</para>
///
/// <para>The rule now is W9.0 D-3's, applied to locking: enforced by the component, not by convention.</para>
/// </summary>
// W10.0A FINAL — LANE: WindowsOnly.
//
// This class exercises `DevelopmentControlStoreIdentity`'s path canonicalisation, which is
// Windows-shaped BY CONSTRUCTION, not by accident: separators fold to `\`, the anchor is a drive root
// or a UNC share, and `IsFullyQualifiedLocal` requires `X:\`. A POSIX path cannot satisfy it — the
// component reports `/tmp/x` as "relative", which is true of nothing on the platform the path came
// from. These tests therefore build their store under `Path.GetTempPath()`, which is drive-rooted on
// Windows and is refused on Linux.
//
// The classification is deliberate and is the `WINDOWS_SPECIFIC_INTEGRATION` case: the production
// component is the canonical DevelopmentControl lock for Nexus Forge and Nexus.Developer, both
// Windows desktop hosts operating on estate workbooks. Its behaviour is real and load-bearing there,
// and it is not expressible on Linux — so the tests run in the Windows lane rather than being
// weakened, skipped inside the portable lane, or "fixed" by teaching a shared lock-identity
// implementation a second path grammar it has no consumer for.
//
// Nothing here is silently dropped: the Windows lane runs these, and the portable lane does not
// claim them. See PORTABLE_TEST_ARCHITECTURE.md for the two-lane contract.
[Trait("Lane", "WindowsOnly")]
public sealed class AtomicWriterLockCanonicalDirectoryTests : IDisposable
{
    private readonly string _root;

    public AtomicWriterLockCanonicalDirectoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexus-lock-canonical-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A store path under this test's root. The workbook need not exist: a lock guards a name.</summary>
    private string Store(string name = "NEXUS_DEVELOPMENT_CONTROL.xlsx") => Path.Combine(_root, name);

    // =============================================================================================
    // 1. Same authority, same process path -> the same lock.
    // =============================================================================================

    [Fact]
    public void SameAuthority_ResolvesToTheSameLockPath_EveryTime()
    {
        var store = Store();

        var first = AtomicWriterLock.LockPathFor(store);
        var second = AtomicWriterLock.LockPathFor(store);

        Assert.Equal(first, second);
        Assert.Equal(Path.Combine(_root, ".lock"), AtomicWriterLock.CanonicalLockDirectoryFor(store));
        Assert.StartsWith(Path.Combine(_root, ".lock"), first, StringComparison.Ordinal);
    }

    /// <summary>Two spellings of one workbook must reach one file, because the name comes from the identity.</summary>
    [Fact]
    public void SameAuthority_DifferentSpellings_ResolveToTheSameLockPath()
    {
        var direct = Path.Combine(_root, "NEXUS_DEVELOPMENT_CONTROL.xlsx");
        var roundabout = Path.Combine(_root, ".", "sub", "..", "NEXUS_DEVELOPMENT_CONTROL.xlsx");

        Assert.Equal(AtomicWriterLock.LockPathFor(direct), AtomicWriterLock.LockPathFor(roundabout));
    }

    // =============================================================================================
    // 2. Same authority, DIFFERENT caller lock directory -> one canonical lock, or a refusal.
    //    This is the defect itself.
    // =============================================================================================

    /// <summary>
    /// The reproduction. Under the old rule these two calls produced two different paths — two files,
    /// same name, neither visible to the other — and both callers held "the lock". They now produce one
    /// path, so a second directory cannot become a second lock domain.
    /// </summary>
    [Fact]
    public void DifferentCallerLockDirectories_CannotFormASecondLockDomain()
    {
        var store = Store();

        var viaFirstCaller = AtomicWriterLock.LockPathFor(store, Path.Combine(_root, "locks"));
        var viaSecondCaller = AtomicWriterLock.LockPathFor(store, Path.Combine(_root, "elsewhere"));

        Assert.Equal(viaFirstCaller, viaSecondCaller);
        Assert.Equal(Path.Combine(_root, ".lock"), Path.GetDirectoryName(viaFirstCaller));
    }

    /// <summary>
    /// And the claim is REFUSED rather than quietly taken elsewhere: a caller that names the wrong
    /// directory is told so at the call that made the mistake, instead of two writers proceeding
    /// concurrently and nobody finding out until the workbook is corrupt.
    /// </summary>
    [Fact]
    public void ACallerNamingANonCanonicalDirectory_IsRefusedDeterministically()
    {
        var store = Store();
        var wrong = Path.Combine(_root, "elsewhere");

        var result = AtomicWriterLock.TryAcquire(
            store,
            wrong,
            new ReservationLease { ReservationId = "r1", WorkerId = "t", LeaseStart = DateTimeOffset.UtcNow },
            MachineHostIdentity.Instance);

        Assert.False(result.Acquired);
        Assert.Equal(AcquireOutcome.LockDirectoryNotCanonical, result.Outcome);
        Assert.Null(result.Lock);

        // The refusal names both directories, so the remedy is in the message.
        Assert.Contains("not the one this store's identity derives", result.Detail, StringComparison.Ordinal);
        Assert.Contains(AtomicWriterLock.CanonicalLockDirectoryFor(store), result.Detail, StringComparison.Ordinal);

        // And nothing was created in the directory that was refused.
        Assert.False(Directory.Exists(wrong));
    }

    [Fact]
    public void IsCanonicalLockDirectory_AcceptsTheDerivedPath_AndNoOpinion_AndRefusesOthers()
    {
        var store = Store();

        Assert.True(AtomicWriterLock.IsCanonicalLockDirectory(store, null));
        Assert.True(AtomicWriterLock.IsCanonicalLockDirectory(store, string.Empty));
        Assert.True(AtomicWriterLock.IsCanonicalLockDirectory(store, AtomicWriterLock.CanonicalLockDirectoryFor(store)));
        Assert.False(AtomicWriterLock.IsCanonicalLockDirectory(store, Path.Combine(_root, "locks")));
    }

    // =============================================================================================
    // 3. Different authority -> different lock.
    // =============================================================================================

    [Fact]
    public void DifferentAuthorities_ResolveToDifferentLocks()
    {
        var first = AtomicWriterLock.LockPathFor(Store("NEXUS_DEVELOPMENT_CONTROL.xlsx"));
        var second = AtomicWriterLock.LockPathFor(Store("NEXUS_PRODUCTS_DEVELOPMENT_CONTROL.xlsx"));

        Assert.NotEqual(first, second);

        // The near-miss case the store-identity rule already protected against, kept asserted here so a
        // future "simplification" of the name cannot reintroduce it: a name that CONTAINS another's must
        // not collide with it.
        var containing = AtomicWriterLock.LockPathFor(Store("NEXUS_DEVELOPMENT_CONTROL_OLD.xlsx"));
        Assert.NotEqual(first, containing);
    }

    // =============================================================================================
    // 4. A concurrent second writer is refused according to the contract.
    // =============================================================================================

    [Fact]
    public void ASecondWriter_OnTheSameAuthority_IsRefusedWhileTheFirstHolds()
    {
        var store = Store();
        var canonical = AtomicWriterLock.CanonicalLockDirectoryFor(store);

        using var held = AtomicWriterLock
            .TryAcquire(store, canonical, new ReservationLease { ReservationId = "r1", WorkerId = "first" }, MachineHostIdentity.Instance)
            .Lock;

        Assert.NotNull(held);

        var second = AtomicWriterLock.TryAcquire(
            store,
            canonical,
            new ReservationLease { ReservationId = "r2", WorkerId = "second", LeaseStart = DateTimeOffset.UtcNow },
            MachineHostIdentity.Instance);

        Assert.False(second.Acquired);
        Assert.Equal(AcquireOutcome.BusyLive, second.Outcome);

        // And the claim is visible without acquiring, which is what a probe is for.
        Assert.True(AtomicWriterLock.IsHeldByAnotherProcess(store));
    }

    /// <summary>
    /// The probe ignores the directory argument too. A probe from a caller holding a different
    /// directory in mind must still report the truth about the canonical lock, or it would answer a
    /// question nobody asked.
    /// </summary>
    [Fact]
    public void TheProbe_AnswersAboutTheCanonicalLock_WhateverDirectoryTheCallerNames()
    {
        var store = Store();
        var canonical = AtomicWriterLock.CanonicalLockDirectoryFor(store);

        using var held = AtomicWriterLock
            .TryAcquire(store, canonical, new ReservationLease { ReservationId = "r1", WorkerId = "first" }, MachineHostIdentity.Instance)
            .Lock;

        Assert.True(AtomicWriterLock.IsHeldByAnotherProcess(store, Path.Combine(_root, "irrelevant")));
    }

    // =============================================================================================
    // 5. Released and reclaimed leases still behave.
    // =============================================================================================

    /// <summary>
    /// These two go through the lock SERVICE rather than hand-building a lease: the service is what
    /// stamps a real expiry from the lease policy, and a hand-built lease with no expiry looks expired
    /// the moment it is written, which tests the stale path by accident rather than the released one.
    /// </summary>
    [Fact]
    public void AReleasedLease_LetsTheNextWriterIn()
    {
        var store = Store();
        var canonical = AtomicWriterLock.CanonicalLockDirectoryFor(store);
        var service = new DevelopmentControlLockService();

        var first = service.TryAcquire(store, canonical, "first");
        Assert.NotNull(first.Reservation);
        first.Reservation!.Dispose();

        var second = service.TryAcquire(store, canonical, "second");

        Assert.Equal(DevelopmentControlLockOutcome.Acquired, second.Outcome);
        Assert.NotNull(second.Reservation);
        second.Reservation!.Dispose();
    }

    /// <summary>After a release the next holder's epoch advances, so two holders are distinguishable in the record.</summary>
    [Fact]
    public void AReacquiredLease_AdvancesTheEpoch()
    {
        var store = Store();
        var canonical = AtomicWriterLock.CanonicalLockDirectoryFor(store);
        var service = new DevelopmentControlLockService();

        var first = service.TryAcquire(store, canonical, "first");
        Assert.NotNull(first.Reservation);
        var firstAcquiredAt = first.Reservation!.Lease.AcquiredAt;
        first.Reservation!.Dispose();

        var second = service.TryAcquire(store, canonical, "second");
        Assert.NotNull(second.Reservation);

        // A new holder, not the same one handed back: the service stamps a fresh lease each time it
        // grants, and the acquirer is recorded by name.
        Assert.True(second.Reservation!.Lease.AcquiredAt >= firstAcquiredAt);
        Assert.Equal("second", second.Reservation!.Lease.Owner);
        second.Reservation!.Dispose();
    }

    // =============================================================================================
    // 6. Lock identity remains stable across consumers.
    // =============================================================================================

    /// <summary>
    /// Both hosts derive the identity from the same function, so they contend on the same object. The
    /// directory is no longer a way for them to disagree — which is the whole point, because before this
    /// change two hosts could agree on the IDENTITY and still not see each other.
    /// </summary>
    [Fact]
    public void TheIdentity_IsAFunctionOfTheStore_AndOfNothingElse()
    {
        var store = Store();

        var computed = new DevelopmentControlLockService().LockIdentityFor(store);

        Assert.StartsWith("NexusDevelopmentControl_", computed, StringComparison.Ordinal);
        Assert.Equal(computed, DevelopmentControlStoreIdentity.CanonicalFromWorkbookPath(store).ObjectName);

        // Every caller derives the same identity, and every caller now derives the same directory too.
        Assert.Equal(computed, new DevelopmentControlLockService().LockIdentityFor(store));
        Assert.Equal(
            AtomicWriterLock.CanonicalLockDirectoryFor(store),
            AtomicWriterLock.CanonicalLockDirectoryFor(Path.Combine(_root, ".", "NEXUS_DEVELOPMENT_CONTROL.xlsx")));
    }

    /// <summary>
    /// The end-to-end shape: a lock service call that names the wrong directory gets a typed refusal,
    /// not a reservation for a lock nobody else can see.
    /// </summary>
    [Fact]
    public void TheLockService_RefusesANonCanonicalDirectory_AndGrantsNothing()
    {
        var store = Store();

        var attempt = new DevelopmentControlLockService()
            .TryAcquire(store, Path.Combine(_root, "elsewhere"), "contender");

        Assert.Equal(DevelopmentControlLockOutcome.LockDirectoryNotCanonical, attempt.Outcome);
        Assert.Null(attempt.Reservation);
    }

    // =============================================================================================
    // 7. W9.3: the authority-owned call shape, and the typed refusal the bool could not express.
    // =============================================================================================

    /// <summary>
    /// The preferred shape, and the one every migrated caller now uses: the store is named and the
    /// lock directory is not. The lock lands where the authority places it, with the caller having
    /// expressed no opinion about location at all.
    /// </summary>
    [Fact]
    public void TheAuthorityOwnedCallShape_PlacesTheLockCanonically_WithNoDirectoryNamed()
    {
        var store = Store();

        var attempt = new DevelopmentControlLockService().TryAcquire(store);

        Assert.Equal(DevelopmentControlLockOutcome.Acquired, attempt.Outcome);
        Assert.NotNull(attempt.Reservation);
        Assert.Equal(store, attempt.Reservation!.StorePath);

        // The one canonical file, in the one canonical directory — and nothing the caller named.
        Assert.True(File.Exists(AtomicWriterLock.LockPathFor(store)));
        Assert.Equal(Path.Combine(_root, ".lock"), Path.GetDirectoryName(AtomicWriterLock.LockPathFor(store)));

        attempt.Reservation.Dispose();
    }

    /// <summary>
    /// The store's OWN directory is accepted as a canonical-equivalent spelling, and it is still not
    /// honoured: the lock goes to <c>&lt;store directory&gt;\.lock</c>, not beside the workbook. This
    /// is the negative control for the third accepted form — accepted, and incapable of forming a
    /// second lock domain.
    /// </summary>
    [Fact]
    public void TheStoresOwnDirectory_IsAccepted_AndStillPlacesTheLockInTheCanonicalDirectory()
    {
        var store = Store();

        Assert.True(AtomicWriterLock.IsCanonicalLockDirectory(store, _root));

        var attempt = new DevelopmentControlLockService().TryAcquire(store, _root, "own-directory-caller");
        Assert.Equal(DevelopmentControlLockOutcome.Acquired, attempt.Outcome);
        attempt.Reservation!.Dispose();

        // Beside the thing it guards, not in the directory the caller named: the only entry in the
        // store's own directory is the canonical `.lock` directory itself.
        Assert.Equal(
            new[] { Path.Combine(_root, ".lock") },
            Directory.GetFileSystemEntries(_root));
    }

    /// <summary>
    /// The refusal shape added for <see cref="IDevelopmentControlLockService.TryIsHeldByAnotherProcess"/>.
    /// A bare <c>bool</c> cannot say "your directory was refused and nothing was probed", so a caller
    /// reading <c>false</c> would take a refusal for "the lock is free" — the one misreading a
    /// liveness probe must never permit.
    /// </summary>
    [Fact]
    public void TryIsHeldByAnotherProcess_RefusesANonCanonicalDirectory_Deterministically()
    {
        var store = Store();
        var wrong = Path.Combine(_root, "elsewhere");
        var service = new DevelopmentControlLockService();

        var first = service.TryIsHeldByAnotherProcess(store, wrong);
        var second = service.TryIsHeldByAnotherProcess(store, wrong);

        Assert.Equal(DevelopmentControlLockProbeOutcome.LockDirectoryNotCanonical, first.Outcome);
        Assert.Equal(first.Outcome, second.Outcome);

        // The three members keep the refusal apart from an answer. `HeldByAnotherProcess` is false
        // here, and `Answered` is what stops that false from reading as "not held".
        Assert.True(first.Refused);
        Assert.False(first.Answered);
        Assert.False(first.HeldByAnotherProcess);
        Assert.Equal(DevelopmentControlLockProbeResult.NonCanonicalLockLocationToken, first.RefusalToken);
        Assert.Equal("NON_CANONICAL_LOCK_LOCATION", first.RefusalToken);

        // No path was probed, so none is reported: naming one would describe a lock nobody asked about.
        Assert.Null(first.LockPath);

        // The refusal names the remedy, and nothing was created in the refused directory.
        Assert.Contains(AtomicWriterLock.CanonicalLockDirectoryFor(store), first.Detail, StringComparison.Ordinal);
        Assert.Contains("Pass null", first.Detail, StringComparison.Ordinal);
        Assert.False(Directory.Exists(wrong));
    }

    /// <summary>
    /// And with no directory named the same member answers the real question, about the store's own
    /// canonical lock — held while a holder lives, and answered as not-held once it is released.
    /// </summary>
    [Fact]
    public void TryIsHeldByAnotherProcess_AnswersAboutTheCanonicalLock_WhenNoDirectoryIsNamed()
    {
        var store = Store();
        var service = new DevelopmentControlLockService();

        var before = service.TryIsHeldByAnotherProcess(store);
        Assert.Equal(DevelopmentControlLockProbeOutcome.NotHeld, before.Outcome);
        Assert.True(before.Answered);
        Assert.False(before.HeldByAnotherProcess);
        Assert.Null(before.RefusalToken);
        Assert.Equal(AtomicWriterLock.LockPathFor(store), before.LockPath);

        using (var held = service.TryAcquire(store).Reservation)
        {
            Assert.NotNull(held);

            var whileHeld = service.TryIsHeldByAnotherProcess(store);
            Assert.Equal(DevelopmentControlLockProbeOutcome.HeldByAnotherProcess, whileHeld.Outcome);
            Assert.True(whileHeld.HeldByAnotherProcess);
            Assert.True(whileHeld.Answered);
        }

        var after = service.TryIsHeldByAnotherProcess(store);
        Assert.Equal(DevelopmentControlLockProbeOutcome.NotHeld, after.Outcome);

        // The canonical-equivalent spellings reach the same answer rather than a refusal, so the
        // acceptance and the typed refusal are two different paths and not one.
        Assert.True(service.TryIsHeldByAnotherProcess(store, AtomicWriterLock.CanonicalLockDirectoryFor(store)).Answered);
        Assert.True(service.TryIsHeldByAnotherProcess(store, _root).Answered);
    }
}
