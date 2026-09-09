# Physical Layer Map

**Status:** CURRENT and authoritative for physical file/folder ownership.
**Owner:** DELIVERY (Layer 07), reflecting the layer definitions in `LAYER_MODEL.md`.
**Last updated:** 2026-09-09.
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
            Nexus.Platform.Core\
            Nexus.Platform.Identity\
            Nexus.Platform.Providers.Anthropic\
            Nexus.Platform.Providers.OpenAI\
            Nexus.Platform.Tools\
        03-Governance\
            Nexus.Governance.Contracts\
            Nexus.Governance.Core\
        06-SharedPlatform\
            Nexus.ProductCore.Contracts\
            Nexus.ProductCore.Scope\
        07-Delivery\
            Nexus.Delivery.Contracts\
        08-Assurance\
            Nexus.Assurance.Contracts\
        Nexus.Platform.Persistence\        <- NOT layer-foldered yet, see §3
    tests\
        01-Core\
            Nexus.Platform.Tests\
        03-Governance\
            Nexus.Governance.Tests\
        06-SharedPlatform\
            Nexus.ProductCore.Scope.Tests\
        08-Assurance\
            Nexus.Assurance.Tests\
        Nexus.Platform.Architecture.Tests\ <- cross-cutting, see §4
        Nexus.Platform.SmokeTests\         <- cross-cutting/live, see §4
    samples\
        Nexus.Platform.SmokeHost\          <- cross-cutting sample host, see §4
```

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
| `Nexus.Platform.Providers.Anthropic` | `src/01-Core/` | 01 CORE | SAFE_MOVE_NOW — done |
| `Nexus.Platform.Providers.OpenAI` | `src/01-Core/` | 01 CORE | SAFE_MOVE_NOW — done |
| `Nexus.Platform.Tools` | `src/01-Core/` | 01 CORE | SAFE_MOVE_NOW — done |
| `Nexus.Governance.Contracts` | `src/03-Governance/` | 03 GOVERNANCE | SAFE_MOVE_NOW — done |
| `Nexus.Governance.Core` | `src/03-Governance/` | 03 GOVERNANCE | SAFE_MOVE_NOW — done |
| `Nexus.ProductCore.Contracts` | `src/06-SharedPlatform/` | 06 SHARED PLATFORM | SAFE_MOVE_NOW — done |
| `Nexus.ProductCore.Scope` | `src/06-SharedPlatform/` | 06 SHARED PLATFORM | SAFE_MOVE_NOW — done |
| `Nexus.Delivery.Contracts` | `src/07-Delivery/` | 07 DELIVERY | SAFE_MOVE_NOW — done |
| `Nexus.Assurance.Contracts` | `src/08-Assurance/` | 08 ASSURANCE | SAFE_MOVE_NOW — done |
| `Nexus.Platform.Persistence` | `src/` (unfoldered) | **CONTESTED — see below** | HUMAN_DECISION |
| `Nexus.Platform.Tests` | `tests/01-Core/` | 01 CORE (tests `ModelCatalogTests`, a CORE/model-gateway concept) | SAFE_MOVE_NOW — done |
| `Nexus.Governance.Tests` | `tests/03-Governance/` | 03 GOVERNANCE | SAFE_MOVE_NOW — done |
| `Nexus.ProductCore.Scope.Tests` | `tests/06-SharedPlatform/` | 06 SHARED PLATFORM | SAFE_MOVE_NOW — done |
| `Nexus.Assurance.Tests` | `tests/08-Assurance/` | 08 ASSURANCE | SAFE_MOVE_NOW — done |
| `Nexus.Platform.Architecture.Tests` | `tests/` (unfoldered) | cross-cutting (tests boundary rules between ALL layers) | COMPATIBILITY_NAME_KEEP — deliberately not nested under one layer |
| `Nexus.Platform.SmokeTests` | `tests/` (unfoldered) | cross-cutting (live external-provider smoke test; not in the main `.slnx`) | COMPATIBILITY_NAME_KEEP |
| `Nexus.Platform.SmokeHost` | `samples/` (unchanged) | cross-cutting sample host consuming 01 CORE | ALREADY_CORRECT — `samples/` is its own category, out of scope for the `src/`/`tests/` layer-folder structure |

**`Nexus.Platform.Persistence` — the one HUMAN_DECISION.** `LAYER_MODEL.md` §4 (01 CORE) lists
`Nexus.Platform.Persistence` in CORE's own `Projects (TARGET)` row (CORE needs it to host identity/
session/audit data), while §4 (02 DATA) separately describes the *same physical stub* as 02 DATA's
`Today` state and implies its `Nexus.Data.Persistence` successor is DATA's real `Projects (TARGET)`.
The authoritative docs have not yet resolved which layer this single 308-byte stub belongs to before
it is real enough to split — moving it into `01-Core/` or a not-yet-justified `02-Data/` folder would
assert an ownership call this document set has not made. Left unfoldered at `src/Nexus.Platform.Persistence/`
pending that decision; `Nexus.Platform.slnx` documents the reason inline.

Renaming any project's assembly/namespace to add a layer prefix (e.g. `Nexus.01Core.*`) was
**not done** — Rule 8 (do not rename stable namespaces for cosmetic consistency) and the fact that
`Nexus.Platform`/`Nexus.Governance`/`Nexus.ProductCore`/`Nexus.Delivery`/`Nexus.Assurance` prefixes
are the repository/capability-family names, not layer names, and remain correct regardless of which
layer-numbered folder currently holds them.

## 4. Cross-cutting exceptions (not owned by a single layer)

| Project | Why it's not layer-foldered |
|---|---|
| `Nexus.Platform.Architecture.Tests` | Its entire purpose is to assert dependency-direction rules *between* layers (the `NexusBoundaryGuard`/boundary tests this document's rules are enforced by) — nesting it under any one layer folder would misrepresent it as scoped to that layer. |
| `Nexus.Platform.SmokeTests` | A live, external-provider (OpenAI) integration/smoke test, deliberately excluded from the main `Nexus.Platform.slnx` build/test run; it exercises the sample host, not a single layer's unit surface. |
| `Nexus.Platform.SmokeHost` (`samples/`) | A cross-cutting manual smoke-test host, not a layer implementation; `samples/` is its own top-level category alongside `src/`/`tests/`. |

## 5. What moved, and what didn't

**Physically relocated (folder path changed via `git mv`, history preserved; project/assembly/
namespace unchanged):** all 12 SAFE_MOVE_NOW rows in §3.

**Not moved:** `Nexus.Platform.Persistence` (§3, HUMAN_DECISION), the three cross-cutting projects
(§4), and everything under `samples/` beyond the one entry above (none currently exists). No
duplicate implementation was created anywhere — every move was a rename/relocate, never a copy.

**Updated to match:** `Nexus.Platform.slnx` (solution folders now mirror the physical layer
folders), every `ProjectReference` path whose relative depth changed as a result of a move,
`pack-productcore-local.ps1` (the one active script with a hardcoded `src\Nexus.ProductCore.*` path).
CI (`​.github/workflows/build.yml`) needed no change — it builds via the `.slnx`, not individual
paths. `Directory.Build.props` needed no change — MSBuild resolves it by walking up from any
project, regardless of nesting depth.

## 6. Verification

`dotnet build Nexus.Platform.slnx -c Release` → 0 warnings, 0 errors, all 18 `.slnx`-listed projects
(13 src + 5 tests) plus the 2 non-`.slnx` cross-cutting projects (`samples/Nexus.Platform.SmokeHost`,
`tests/Nexus.Platform.SmokeTests`) build clean. `dotnet test Nexus.Platform.slnx -c Release --no-build`
→ 82/82 passed, 0 failed, 0 skipped, including `Nexus.Platform.Architecture.Tests` (14/14) — the
boundary rules this reorganization is meant to make visible are themselves proven still intact.
