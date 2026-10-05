# Physical Layer Map

**Status:** CURRENT and authoritative for physical file/folder ownership.
**Owner:** DELIVERY (Layer 07), reflecting the layer definitions in `LAYER_MODEL.md`.
**Last updated:** 2026-10-05 — W10.0A canonical Platform reconciliation (W5G AI boundary applied,
W9 Delivery re-homed into the layered layout, smoke host retired, solution coverage made total).
**Authoritative for:** exactly which folder on disk holds each numbered Platform layer's
projects, and which projects/tests are deliberately cross-cutting rather than owned by a single
layer. `LAYER_MODEL.md` remains authoritative for what each layer *means*, *owns*, and *targets*;
this document is authoritative only for *where its current code physically lives*.

**Rule:** layer ownership → folder location → project/assembly → namespace agree wherever
practical. Project/assembly/namespace names are **not** renamed to force this — see
`NAMING_STANDARDS.md` and the "no cosmetic renames" principle — only their physical folder
location was moved.

---

## 1. Repository-level ownership (unchanged, cross-referenced from `LAYER_MODEL.md` §1/§4a)

| Owner | Physical root |
|---|---|
| Layers 01, 02\*, 03, 05\*\*, 06, 07, 08, 09\*\* | `D:\NEXUS\Platform\` (this repository) |
| Layer 04 AI | `D:\NEXUS\Intelligence\` |
| Layer 10 EXPERIENCE | `D:\NEXUS\Products\Experience\` |
| Nexus Forge (outside numbered layers) | `D:\NEXUS\Forge\` |
| Products, incl. Nexus Developer (outside numbered layers) | `D:\NEXUS\Products\Developer\` |

\* Layer 02 DATA has no dedicated project yet — see §3.
\*\* Layers 05 AUTOMATION and 09 OPERATIONS have no project at all yet — see §3.

## 2. Physical map — this repository (`D:\NEXUS\Platform`)

```
D:\NEXUS\Platform\
    src\
        01-Core\
            Nexus.Platform.Contracts\
            Nexus.Platform.Core\            <- incl. Secrets\EnvironmentSecretResolver.cs (W5E)
            Nexus.Platform.Identity\
            Nexus.Platform.Persistence\     <- resolved to 01 CORE, see §3
            Nexus.Platform.Tools\
        03-Governance\
            Nexus.Governance.Contracts\
            Nexus.Governance.Core\
        06-SharedPlatform\
            Nexus.ProductCore.Contracts\    <- incl. DevelopmentControl\ (W8B/W8D-R4)
            Nexus.ProductCore.Core\         <- W8D-R4 TASK 2; added by the W10.0A integration
            Nexus.ProductCore.Scope\
        07-Delivery\
            Nexus.Delivery.Contracts\       <- W9.1
            Nexus.Delivery.Core\            <- W9.1; added by the W10.0A integration
        08-Assurance\
            Nexus.Assurance.Contracts\
    tests\
        03-Governance\
            Nexus.Governance.Tests\
        06-SharedPlatform\
            Nexus.ProductCore.DevelopmentControl.Tests\  <- W8D TASK 3
            Nexus.ProductCore.Scope.Tests\
        07-Delivery\
            Nexus.Delivery.Tests\           <- W9.1-W9.6; added by the W10.0A integration
        08-Assurance\
            Nexus.Assurance.Tests\
        Nexus.Platform.Architecture.Tests\  <- cross-cutting, see §4
    tools\                                  <- governed drivers; NOT layer-numbered
        Nexus.Delivery.Build\  Nexus.Delivery.Deploy\  Nexus.Delivery.Release\  Nexus.Delivery.ReleasePlan\
        W8dFinalReconcile\  W94DeploymentLineage\  W95PromotionLineage\  W96IdentityBackfill\  W9LineageClosure\
```

**`samples\` and the two cross-cutting live-test projects are gone.** See §4a — the W10.0A owner
decision retired `Nexus.Platform.SmokeHost` and `Nexus.Platform.SmokeTests`, and F-01 had already
left `Nexus.Platform.Tests` with no test files at all.

A developer opening `D:\NEXUS\Platform\src\` or `\tests\` can determine the layer owner of any
project from its immediate parent folder name (`01-Core`, `03-Governance`, `06-SharedPlatform`,
`07-Delivery`, `08-Assurance`) without reading any code or asking anyone. `02-Data`, `05-Automation`
and `09-Operations` folders are **not created yet** — no project exists for them today, and an empty
folder would be decorative, not informative (see §3). Create each only when its first real project
lands.

## 3. Classification of every project (Task 1–3)

| Project | Current physical location | Layer owner | Classification |
|---|---|---|---|
| `Nexus.Platform.Contracts` | `src/01-Core/` | 01 CORE | SAFE_MOVE_NOW — done |
| `Nexus.Platform.Core` | `src/01-Core/` | 01 CORE | SAFE_MOVE_NOW — done |
| `Nexus.Platform.Identity` | `src/01-Core/` | 01 CORE | SAFE_MOVE_NOW — done |
| ~~`Nexus.Platform.Providers.Anthropic`~~ | **removed** | — | **RELOCATED to the AI Head** (W5/W4L-202) |
| ~~`Nexus.Platform.Providers.OpenAI`~~ | **removed** | — | **RELOCATED to the AI Head** (W5G / F-01) |
| `Nexus.Platform.Tools` | `src/01-Core/` | 01 CORE | SAFE_MOVE_NOW — done |
| `Nexus.Governance.Contracts` | `src/03-Governance/` | 03 GOVERNANCE | SAFE_MOVE_NOW — done |
| `Nexus.Governance.Core` | `src/03-Governance/` | 03 GOVERNANCE | SAFE_MOVE_NOW — done |
| `Nexus.ProductCore.Contracts` | `src/06-SharedPlatform/` | 06 SHARED PLATFORM | SAFE_MOVE_NOW — done |
| `Nexus.ProductCore.Scope` | `src/06-SharedPlatform/` | 06 SHARED PLATFORM | SAFE_MOVE_NOW — done |
| `Nexus.Delivery.Contracts` | `src/07-Delivery/` | 07 DELIVERY | SAFE_MOVE_NOW — done |
| `Nexus.Delivery.Core` | `src/07-Delivery/` | 07 DELIVERY | **RE-HOMED 2026-10-05** — W10.0A integration; was flat `src/Nexus.Delivery.Core/` on the historical W9 line |
| `Nexus.ProductCore.Core` | `src/06-SharedPlatform/` | 06 SHARED PLATFORM | **RE-HOMED 2026-10-05** — W10.0A integration; was flat `src/Nexus.ProductCore.Core/` |
| `Nexus.Assurance.Contracts` | `src/08-Assurance/` | 08 ASSURANCE | SAFE_MOVE_NOW — done |
| `Nexus.Platform.Persistence` | `src/01-Core/` | 01 CORE | RESOLVED 2026-09-09 — see below |
| ~~`Nexus.Platform.Tests`~~ | **removed** | — | **RETIRED 2026-10-05** — zero test files since F-01 relocated `ModelCatalogTests` |
| `Nexus.Governance.Tests` | `tests/03-Governance/` | 03 GOVERNANCE | SAFE_MOVE_NOW — done |
| `Nexus.ProductCore.DevelopmentControl.Tests` | `tests/06-SharedPlatform/` | 06 SHARED PLATFORM | **RE-HOMED 2026-10-05** — W10.0A integration; was flat `tests/Nexus.ProductCore.DevelopmentControl.Tests/` |
| `Nexus.ProductCore.Scope.Tests` | `tests/06-SharedPlatform/` | 06 SHARED PLATFORM | SAFE_MOVE_NOW — done |
| `Nexus.Delivery.Tests` | `tests/07-Delivery/` | 07 DELIVERY | **RE-HOMED 2026-10-05** — W10.0A integration; was flat `tests/Nexus.Delivery.Tests/` |
| `Nexus.Assurance.Tests` | `tests/08-Assurance/` | 08 ASSURANCE | SAFE_MOVE_NOW — done |
| `Nexus.Platform.Architecture.Tests` | `tests/` (unfoldered) | cross-cutting (tests boundary rules between ALL layers) | COMPATIBILITY_NAME_KEEP — deliberately not nested under one layer |
| ~~`Nexus.Platform.SmokeTests`~~ | **removed** | — | **RETIRED 2026-10-05** — see §4a |
| ~~`Nexus.Platform.SmokeHost`~~ | **removed** | — | **RETIRED 2026-10-05** — see §4a |

**`Nexus.Platform.Persistence` — RESOLVED to 01 CORE (2026-09-09).** File-level inspection (not a
name-based guess) found the project contains exactly one source file, `PlatformStore.cs`: a single
empty marker interface, `IPlatformStore`, with **zero implementation** — no repository class, no
database/storage code, no persistence adapter, no data access, no serialization, no migration, no
provider-specific persistence code. Its own `TODO` comment scopes its eventual contents to "tenants,
users, products, entitlements, provider configuration, the usage ledger and the audit log" —
every one of those is a responsibility 01 CORE's own `Owns` list in `LAYER_MODEL.md` §4 claims
(identity, tenancy, audit, secrets, usage metering), and **none** overlaps 02 DATA's `Owns` list
(documents, knowledge, embeddings, retrieval). Per the ownership rule ("CORE owns genuinely
foundational, provider-neutral interfaces/contracts... DATA owns actual persistence
implementation"), a zero-implementation contract for CORE-owned entities is a CORE artifact, full
stop — there is no persistence implementation present to justify placing it in, or splitting it
with, `02-Data/`. Moved to `src/01-Core/Nexus.Platform.Persistence/` intact (no split needed, since
there is nothing DATA-owned inside it to split out). When real persistence implementation is later
built to satisfy `M-02-1.5`, it becomes a **new** project (`Nexus.Data.Persistence`, per
`LAYER_MODEL.md`'s own 02 DATA target name) under `src/02-Data/` at that time — this project's role
remains the CORE-side contract, and `02-Data/` is created then, not now (an empty folder today would
be decorative).

Renaming any project's assembly/namespace to add a layer prefix (e.g. `Nexus.01Core.*`) was
**not done** — Rule 8 (do not rename stable namespaces for cosmetic consistency) and the fact that
`Nexus.Platform`/`Nexus.Governance`/`Nexus.ProductCore`/`Nexus.Delivery`/`Nexus.Assurance` prefixes
are the repository/capability-family names, not layer names, and remain correct regardless of which
layer-numbered folder currently holds them.

## 4. Cross-cutting exceptions (not owned by a single layer)

| Project | Why it's not layer-foldered |
|---|---|
| `Nexus.Platform.Architecture.Tests` | Its entire purpose is to assert dependency-direction rules *between* layers — nesting it under any one layer folder would misrepresent it as scoped to that layer. |
| `tools\*` | Governed drivers and closure instruments, not layer implementations. They read across the tree (source roots, artifact stores, git refs), so no single layer owns them. |

## 4a. Retirement of the Platform smoke host (2026-10-05)

**`Nexus.Platform.SmokeHost` and `Nexus.Platform.SmokeTests` were removed, not repaired.**

The host's premise was *"Platform owns a provider and routes a chat turn through it"*: it composed
`AddNexusPlatform` + `AddOpenAIModelProvider`, resolved `IModelGateway`, called the live OpenAI API,
recorded usage through `InMemoryUsageMeter`, and persisted the assistant message so a fresh process
could read it back. **W5G and F-01 removed every one of those dependencies from Platform** — the
provider, the model gateway, the catalog and the meter all moved to the AI Head. After the W5G
application the host's `ProjectReference` pointed at a project that no longer exists, and
`AddNexusPlatform` no longer registers `IModelGateway` at all.

**The Owner's ruling (W10.0A TASK 7):** its old Platform/provider form is
`SUPERSEDED_BY_ARCHITECTURE`. It must **not** be repaired by pointing Platform at the AI provider —
that would reintroduce the very dependency W5G exists to prevent. **No provider-independent Platform
smoke responsibility remains**: all three checks the host proved (chat works end to end, usage
recorded, message survives a process restart) require a live provider call, and the persistence half
depends on the turn that produced the record.

**Recorded as a future AI-Head-owned capability gap, not implemented here.** The three checks map to
`NEXUS_MIGRATION_RUNBOOK.md` verification items 12–14 and are still worth having; they now belong to
the AI Head, which owns the provider and the model domain. Nothing was built to take their place in
this milestone — that is the directive's explicit instruction.

**Why this is not "deleting tests to get green".** Both projects were outside `Nexus.Platform.slnx`
and outside the CI run by design (they need an API key and cost money), so no green count changed
either way. The change that made them unbuildable was the architecture, not the test result.

## 4b. Why the solution now lists every project

`Nexus.Platform.slnx` is asserted to be **exactly** the set of projects on disk, by
`tests/Nexus.Platform.Architecture.Tests/ProjectCoverageTests.cs`. There is no exclusion list.

The defect it closes: SmokeHost sat outside the solution, so when W5G moved the provider out of
Platform, **nothing could surface its now-dangling `ProjectReference`** — the estate build stayed
green. A project outside the solution is built by nothing and checked by nothing. The same shape
appeared a second time as a test project with zero tests. See §6 for the live negative control.

## 5. What moved, and what didn't

**Physically relocated (folder path changed via `git mv`, history preserved; project/assembly/
namespace unchanged):** all 12 original SAFE_MOVE_NOW rows in §3, plus `Nexus.Platform.Persistence`
once its ownership was resolved (13 total).

**Not moved:** the cross-cutting projects (§4). No
duplicate implementation was created anywhere — every move was a rename/relocate, never a copy.

**W10.0A canonical integration (2026-10-05) — the second physical move.** The Owner ruled that the
layered layout is canonical and that the flat W9 layout must not become canonical, then required the
accepted W9 implementation to be re-homed into it. Every file that came across is **byte-identical to
its source on `w9.5/test-promotion` (193 of 193 checked)**; the only edits are the project-path
rewrites inside `.csproj` files, the boundary-array additions W9.1 had already made, and the two
F-23 `_NexusSharedRoot` depth corrections described in
`src/06-SharedPlatform/Nexus.ProductCore.Core/Nexus.ProductCore.Core.csproj`. See
`docs/CANONICAL_INTEGRATION.md`.

**Updated to match:** `Nexus.Platform.slnx` (solution folders now mirror the physical layer
folders), every `ProjectReference` path whose relative depth changed as a result of a move,
`pack-productcore-local.ps1` (the one active script with a hardcoded `src\Nexus.ProductCore.*` path).
CI (`​.github/workflows/build.yml`) needed no change — it builds via the `.slnx`, not individual
paths. `Directory.Build.props` needed no change — MSBuild resolves it by walking up from any
project, regardless of nesting depth.

## 6. Verification

**Historical (2026-09-09, the first physical move):** `dotnet build Nexus.Platform.slnx -c Release`
→ 0 warnings, 0 errors over 18 `.slnx`-listed projects; `dotnet test` → 82/82 passed.

**Current (2026-10-05, after the W10.0A canonical integration):**

```
dotnet build Nexus.Platform.slnx -c Release
  → Build succeeded, 0 errors, 21 warnings (all pre-existing, carried unchanged from the W9 line:
    19 xUnit1031 in Nexus.Delivery.Tests, 1 CS8602 in Nexus.Delivery.Deploy, 1 CS8604 in
    Nexus.ProductCore.Core. Same sources + identical Directory.Build.props on both lines.)

dotnet test Nexus.Platform.slnx -c Release --no-build
  → 839 passed / 0 failed / 0 skipped across 6 suites:
      Nexus.Delivery.Tests                        591
      Nexus.ProductCore.DevelopmentControl.Tests  149
      Nexus.Governance.Tests                       59
      Nexus.Platform.Architecture.Tests            17
      Nexus.ProductCore.Scope.Tests                16
      Nexus.Assurance.Tests                         7
```

**Equivalence with the historical W9 line, measured rather than asserted.** The same suites were run
on `w9.5/test-promotion` @ `df9b43f`: Governance 59, Scope 16, DevelopmentControl 149, Delivery 590,
Architecture 16 → **830 / 0 / 0**, exactly the W9.6 recorded total. The integrated tree reproduces
every one of those counts unchanged; the difference is `+1` from `ProjectCoverageTests` (§4b) and
`+7` from `Nexus.Assurance.Tests`, which was on the W9 line's disk but outside its solution. The
Architecture suite's `16 → 17` is the whole of the `+1`.

**Live negative control for §4b** — a stray `tools/StrayProbe/StrayProbe.csproj` was created and
`dotnet test --filter ProjectCoverageTests` failed with *"Project(s) exist on disk but are NOT in
Nexus.Platform.slnx … Missing: tools/StrayProbe/StrayProbe.csproj"*; removing the probe restored the
pass. A guard that has never failed is not a guard.
