# PROVENANCE — DevelopmentControlStoreIdentity.cs

**W1 Closure Task 4.** This file records where the identity derivation came from, what pins it,
and what the transitional arrangement is. It is deliberately a separate file rather than a header
block: a provenance header written *into* the source would make the two repositories' copies differ
byte-for-byte, and byte-identity is the property being relied on.

## What is pinned

| | |
|---|---|
| File | `DevBridge/src/DevBridge.Workspace.Locking/Safety/DevelopmentControlStoreIdentity.cs` |
| SHA-256 | `55db2d8c0e864143e02f2c5bf7fee835e890680d76d0cf28d4c42889971a2b56` |
| Bytes | 21715 |
| Algorithm declared | SHA-256 (`DevelopmentControlStoreIdentity.HashAlgorithmName`) |

The identical pin is recorded in `Nexus.Developer/src/Nexus.Developer.Core/DevelopmentControl/`
`PROVENANCE.md`. **Each host asserts its own copy against its own pin**, so a divergence
introduced on either side fails that side's test suite with no cross-repository access.

### The pin is over LF-normalised content, and that is deliberate

Both repositories set `core.autocrlf=true` and neither has a `.gitattributes`. Git therefore
stores this file with LF in the object database and writes it back as **CRLF into every fresh
checkout**. This was measured in W1 Closure Task 6's clean-clone check, not assumed: a tracked
blob that is LF in the index arrives in the working tree as CRLF.

A pin taken over raw working-tree bytes would therefore pass in the worktree where it was taken
and then fail on every clean checkout — a check that is green exactly where it is written and red
everywhere it matters. Both hosts fold CRLF to LF before hashing, so the pin describes the
**source** rather than the checkout policy.

The pin still fails on any real edit: a changed character, a changed line or an inserted blank
line all change the digest. Only the CRLF/LF spelling is folded away. Both behaviours were
verified by probe — a one-line appended comment and a one-token change to the hash input each
turned the checks red, and the file was restored byte-identically afterwards.

## Where the copy in the other repository is

- Forge copy: `DevBridge/src/DevBridge.Workspace.Locking/Safety/DevelopmentControlStoreIdentity.cs`
- Developer copy: `src/Nexus.Developer.Core/DevelopmentControl/DevelopmentControlStoreIdentity.cs`

Both are compiled by their own host. Neither build resolves a path into the other's worktree.

## Why two copies, and why this is transitional

W1-01 requires exactly ONE implementation of the lock identity, because two implementations that
merely "agree" is the defect V3_M1D_LOCK_IDENTITY.md measured. Before W1 Closure that was achieved
with a `<Compile Include>` in `Nexus.Developer.Core.csproj` naming the Forge worktree by relative
path (`..\..\..\Forge\DevBridge\...`). That is the cross-worktree coupling W1 Closure Task 4 exists
to remove, and it is removed.

Removing it while keeping one physical copy was not available:

- W1-11 forbids the Platform / Layer-06 extraction that would give the file one neutral home.
- Publishing a shared package needs a feed. `github-prtcare` is unreachable from this device and
  its credentials are out of the agent-readable trust boundary; `C:\Personal\LocalNuGet` is
  excluded by W1 Closure Task 6, which requires the build not to depend on it.

So the file is present in both repositories, byte-identical, and the "one algorithm" property is
enforced by measurement instead of by there being one file:

1. **Pin assertion, per host.** Each host hashes its own copy and compares to the literal above.
   Editing either copy fails that host's own suite. A pin updated on one side only leaves the two
   literals unequal, which the cross-host dump comparison in Task 5 surfaces.
2. **Frozen vectors, per host.** Both hosts assert the 19 canonical vectors frozen by
   `V3_M1D_LOCK_IDENTITY.md` §3 against the same expected hashes, so both are held to the recorded
   behaviour rather than to each other.
3. **Byte-for-byte dump comparison.** Both hosts can emit the vector table they computed. Task 5
   diffs the two dumps; an empty diff is the cross-host identity proof.

**Residual tension, stated plainly:** this is two physical copies, so LI-1 ("one implementation")
is satisfied by enforcement rather than by construction. The correct end state remains one
referenced assembly — `W1_CHANGE_MANIFEST.md` D-2 — and this arrangement is transitional to it.

## Change control

This file is a lock-identity source. Changing it changes which kernel object two processes contend
on. Per LI-7 that is a **migration, not an edit**: a writer holding the old name and a writer
holding the new one over the same workbook is the silent dual-writer defect the lock exists to
prevent. Any change here must

1. update the pin in **both** `PROVENANCE.md` files,
2. keep the frozen-vector expectations in **both** hosts passing, or record the migration that
   explains why they changed, and
3. use `DevelopmentControlStoreIdentity.InteropObjectNames` so a transitional acquirer holds both
   names for the duration.
