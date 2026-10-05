# Canonical Platform Integration — W10.0A Owner Decision

**Status:** Active record of the 2026-10-05 canonical Platform reconciliation.
**Owner:** DELIVERY (Layer 07) for the physical result; the Owner for the decision it implements.
**Authoritative for:** why the canonical Platform tree is shaped the way it is, what came from where,
and which of its properties are proven rather than asserted. `PHYSICAL_LAYER_MAP.md` remains
authoritative for *where each project lives*; this document is authoritative for *how it got there*.

---

## 1. The decision this implements

W10.0A returned `HUMAN_DECISION_REQUIRED` on a branch-topology question: there was no single canonical
Platform line, and the two candidates were **structurally incompatible** — `origin/main` carried the
layered physical migration (`src/01-Core/…`), while `w9.5/test-promotion` carried the accepted W9
Delivery implementation in a **flat** layout that predated it.

**The Owner's ruling: the layered structure is canonical.** The flat W9 layout must not become
canonical; the accepted W9 work must be re-homed into the layers instead. Neither historical line is
rewritten — both remain as evidence, and both are still reachable.

## 2. The shape of the change

```
origin/main  da92d34  ──┐  layered layout, provider present, W9 absent
                        ├── merge-base 540beed
f139e5d (W5G line) ─────┘  provider removed, model domain moved to AI Head
                              ↓ 45 commits
                       w9.5/test-promotion  df9b43f   (W9 Delivery, FLAT layout)

                    ┌──────────────────────────────────────────────┐
                    │  integration/v3-platform-canonical            │
                    │  = origin/main's layered structure           │
                    │  + the accepted W5G AI-Head boundary         │
                    │  + the complete accepted W9 Delivery         │
                    │  + the W8B/W9 hardening that came with it    │
                    └──────────────────────────────────────────────┘
```

Integration branch `integration/v3-platform-canonical`, base `da92d34`, created in an isolated
worktree. **No rebase, no force push, no history rewrite, no direct protected-main mutation.**

## 3. What moved, and what was proved about it

**193 files re-homed, 193 byte-identical to their source on `w9.5/test-promotion`** — verified by blob
hash, not by inspection. The re-home is a relocation, not a reimplementation:

| From (W9 line) | To (canonical layered) | Layer |
|---|---|---|
| `src/Nexus.Delivery.Contracts/` | `src/07-Delivery/Nexus.Delivery.Contracts/` | 07 DELIVERY |
| `src/Nexus.Delivery.Core/` | `src/07-Delivery/Nexus.Delivery.Core/` | 07 DELIVERY |
| `src/Nexus.ProductCore.Core/` | `src/06-SharedPlatform/Nexus.ProductCore.Core/` | 06 SHARED PLATFORM |
| `tests/Nexus.Delivery.Tests/` | `tests/07-Delivery/Nexus.Delivery.Tests/` | 07 DELIVERY |
| `tests/Nexus.ProductCore.DevelopmentControl.Tests/` | `tests/06-SharedPlatform/Nexus.ProductCore.DevelopmentControl.Tests/` | 06 SHARED PLATFORM |
| `tools/Nexus.Delivery.{Build,Deploy,Release,ReleasePlan}/` | *(unchanged — `tools/` is not layer-numbered)* | — |
| `tools/{W8dFinalReconcile,W94DeploymentLineage,W95PromotionLineage,W96IdentityBackfill,W9LineageClosure}/` | *(unchanged)* | — |

Shared projects also took W9's additions at their layered paths: the three W8B governance contracts,
`DeterministicGovernanceEvaluator`, the `Nexus.ProductCore.Contracts/DevelopmentControl/` surface, the
`IGovernanceEvaluator` registration, and the new Governance and secret-scan test files.

## 4. The AI boundary (W5G), applied to the layered tree

The provider implementation **was present on `origin/main`** at `src/01-Core/Nexus.Platform.Providers.*`.
It is now removed, along with the model domain the same OWNER DECISION F-01 relocated:

- `src/01-Core/Nexus.Platform.Providers.OpenAI/` — removed (W5G / F-01)
- `src/01-Core/Nexus.Platform.Providers.Anthropic/` — removed (W5 / W4L-202)
- `src/01-Core/Nexus.Platform.Core/Models/` — removed (aggregating catalog, catalog source, named
  gateway, in-memory meter, routing gateway)
- `src/01-Core/Nexus.Platform.Contracts/Models/IModelCatalog.cs` — removed
- `AddNexusAi` deleted, and `AddNexusPlatform`'s call to it with it
- `src/01-Core/Nexus.Platform.Core/Secrets/EnvironmentSecretResolver.cs` — **added** (W5E, the neutral
  secret-access boundary)

**Platform now resolves no model gateway, catalog or meter.** A host that needs the model domain
composes the AI Head at its own composition root. The contract plane is what Platform ships; the code
behind it is not.

The provider-scoped boundary assertions that could not compile without the provider were **relocated,
not deleted** — they assert properties of an assembly that is no longer here, and they are asserted
from the AI Head, which now owns it.

## 5. Two corrections the integration had to make, and why they mattered

**The F-23 `_NexusSharedRoot` depth.** `Nexus.ProductCore.Core.csproj` and
`Nexus.ProductCore.Contracts.csproj` carry the W8B remedy for the cross-consumer reproducibility defect
F-23: a `PathMap` keyed on the **repository root**, so the consumers' differing spellings of it
(`D:\NEXUS\…` vs `D:\Nexus\…`) stop reaching the compiler. That key was computed as `..\..\` — correct
at the old `src/<Project>/` depth, **wrong at `src/06-SharedPlatform/<Project>/`**. Left uncorrected it
would have mapped `…\Platform\src\` instead of `…\Platform\`, silently withholding the remedy and
letting F-23 return as a byte mismatch with nothing to announce it. Both depths were corrected together;
if they ever disagree, the two assemblies stop being byte-comparable.

**A guard with no test.** TASK 9 requires that an *unreadable* identity binding is not treated as
*absent*. The behaviour was implemented in `FileArtifactStore.ReadIdentity` and covered by **no test** —
the estate's recurring "named safety check that cannot fail" shape. A control was added
(`AnUnreadableIdentityBindingIsNotTreatedAsAbsent`) and **fault-injected to prove it fires**: with the
invariant collapsed to `return null`, the test fails with *"No exception was thrown"*; restored, it
passes.

## 6. What was retired, and what is owed

| | Disposition |
|---|---|
| `samples/Nexus.Platform.SmokeHost` | **RETIRED** — `SUPERSEDED_BY_ARCHITECTURE`. Not repaired: re-pointing it at the AI provider is the dependency W5G exists to prevent. No provider-independent Platform smoke responsibility remained. |
| `tests/Nexus.Platform.SmokeTests` | **RETIRED** — the live-provider half of the same capability. The three checks (runbook items 12–14) are recorded as a **future AI-Head-owned capability gap**; nothing was built to replace them in this milestone. |
| `tests/01-Core/Nexus.Platform.Tests` | **RETIRED** — zero test files since F-01 relocated `ModelCatalogTests`; a project that builds and cannot fail. W5G's `D-13` (CORE's neutral implementations have no unit coverage) remains **open**. |
| `mutation-battery-w94*.sh` | **Never carried.** Temporary W9 execution artifacts; they exist on the historical line only. Removing them there would need the history rewrite this decision forbids, so they are left as history. |
| `fix/w94-secret-scanner` | **Left as history.** Its scanner work is present on the W9 line in later form; no competing implementation was reintroduced. |

**Forward obligation, recorded not fixed:** Forge's and Nexus.Developer's shared-control imports build
their `ProjectReference`s from `$(NexusPlatformRoot)\src\<Project>\…` — the *flat* layout. When those
adaptive lanes are merged they must be re-pointed at the layered paths. Measured: **the live Forge and
Developer trees contain no such props file today** — the adaptation exists only on unmerged branches —
so this is an obligation for whoever merges them, not a live breakage. Changing another repository is
outside this milestone.

## 7. What is proven, and what is only carried

| Claim | How it was established |
|---|---|
| The re-home changed no W9 source | 193/193 blob-hash identity |
| W9 behaviour survives integration | every W9 suite count reproduced exactly (59/16/149/590); total 839/0/0 |
| Platform contains no provider-specific AI implementation | no provider path or namespace under `src/`; the neutrality scan runs on every build |
| The solution cannot hide a dangling project | `ProjectCoverageTests`, with a **live** failing negative control |
| An unrelated repository is refused as a source root | `SourceRootIsInsideAnUnrelatedWorkTree` re-run live against `D:\NEXUS_V3_AUDIT` |
| `ArtifactId` binds to exactly one payload | idempotent / refused / survives payload deletion, all re-run live, plus the recovery case |
| The unreadable-binding invariant holds | new control, fault-injected to prove it fails when the invariant is removed |
| W9.6 PROD deferral unchanged | `DEFERRED_BY_OWNER` / `NOT_STARTED`; nothing in this change touches it |

**Historical certification ≠ canonical-integration verification.** `W9.4 PASS`, `W9.5 PASS` and
`W9.6 DEFERRED_BY_OWNER` stand as verdicts on the code that still exists; none was reopened or
rewritten. What changed is that the *evidence paths* they name (`w9.5/test-promotion`, flat `tools/…`)
are now historical rather than current. If integration verification had failed, the W9 history would
have remained historical truth while this baseline stayed blocked.
