# 00 — Documentation Standard

**Added:** 2026-08-20 · **Governs:** every other document in this set

This is the meta-document. It says where documentation lives, how it is numbered, who owns
which subject, and when it gets updated. It deliberately contains **no** architecture, API,
schema or coding content — those belong to the numbered documents below, and duplicating
them here would create a second source of truth that drifts.

---

## 1. Where documentation lives

**One hub, per-repo READMEs.**

The canonical numbered set is the source of truth for everything cross-cutting: vision,
architecture, domain, API contract, conventions, decisions, roadmap. It lives in **one**
place, because the system is three repositories implementing *one* architecture — not three
architectures that share a network boundary. A cross-cutting statement copied into three
repos drifts the first time one copy is edited and the others aren't.

**Target location:** `C:\Personal\NexusAI\docs\`

`Nexus.AI` is the right host: it is the one repo every product already depends on as a
package, and it is not deployed, so nothing about running it competes with the docs for
attention.

Each repo — `Nexus.AI`, `Nexus.Int`, `Nexus.Web`, and every product after them — carries a
`README.md` that orients a new developer in under a minute and **links** to the hub rather
than restating it. A repo earns its own `docs/` folder only when it has detail that
genuinely doesn't belong centrally (a deployment runbook for that specific host, say). Until
then a README is enough; don't create empty folders speculatively.

The `architecture/` folder in the NexusAI Claude project is a **mirror for AI-session
continuity**, not a second source of truth. If the two disagree, the repo wins — it is what
Visual Studio, `dotnet build` and Claude Code actually read.

### 1.1 Current location, and why it must change

As of 2026-08-20 the canonical set is at:

```
C:\Personal\NexusAI\NexusAI Documentation\
    Phase 3 end of phase 1 backend\
        NexusAI_Canonical_Documentation_2026-08-16\
            NexusAI_Canonical_Documentation_2026-08-16\      <- doubled
                01_… through 12_…, README.md
```

Four levels deep, inside a folder named for a phase that has since completed, with the
extraction folder nested inside a duplicate of itself. `docs\` exists at the repo root and
is **empty**.

`07_DEVELOPMENT_GUIDE.md` already forbids exactly this — *"Do not add `ReadmeV4`,
`Roadmap-New`, or nested ZIP archives"* and *"Was obsolete documentation removed rather than
copied into a new version?"*. The rule was right; the folder simply escaped it, the way
untested rules do. `Phase 2\` still holds `ReadmeV2.md` **and** `ReadmeV3.md` for the same
reason.

The fix is a `git mv` of the twelve numbered files into `docs\`, keeping `Phase 1` and
`Phase 2` where they are as genuine historical snapshots. That is a code-side operation with
history to preserve, so it belongs to Claude Code, not to a file copy — see
`DOCS_CONSOLIDATION_PROMPT.md`.

## 2. The numbered set

Confirmed by reading the folder on 2026-08-20. Every slot 01–12 is occupied; nothing here
is inferred.

| # | File | Owns |
|---|---|---|
| **00** | `00_DOCUMENTATION_STANDARD.md` | **This document** — structure, numbering, ownership, maintenance |
| 01 | `01_VISION_AND_PRODUCT_MODEL.md` | Product purpose, layers, principles, success criteria |
| 02 | `02_ARCHITECTURE_AND_MODULES.md` | Technical architecture, dependency rules, runtime flows |
| 03 | `03_DOMAIN_AND_DATAVERSE.md` | Domain hierarchy, persistence model, schema, relationships |
| 04 | `04_API_CONTRACT.md` | Frontend-facing HTTP contract |
| 05 | `05_FRONTEND_PRODUCT_DESIGN.md` | Information architecture, screens, behaviour, sequence |
| 06 | `06_ENVIRONMENTS_CONFIGURATION_DEPLOYMENT.md` | Dev/Test/Prod, secrets, deployment |
| 07 | `07_DEVELOPMENT_GUIDE.md` | **Setup, coding standards, naming conventions, review checklist** |
| 08 | `08_DECISIONS_AND_TECHNICAL_DEBT.md` | ADRs and current debt |
| 09 | `09_ROADMAP_AND_MILESTONES.md` | Completed work, current milestone, future sequence |
| 10 | `10_CHANGELOG.md` | Consolidated history |
| 11 | `11_FUTURE_OF_NEXUS_AI.md` | Long-range direction |
| 12 | `12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md` | Entity model and relationships |
| — | `README.md` | Index and getting-started; the map, not the territory |

**Two renames are due**, both consequences of ADR-014:

- `03_DOMAIN_AND_DATAVERSE.md` → `03_DOMAIN_AND_PERSISTENCE.md`. Dataverse is being deleted;
  a document named after a dependency outlives that dependency badly.
- `12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md` stays, but its scope note changes: all 21
  tables belong to the **Chat product**, not to the platform. It becomes multi-product only
  when product #2 exists.

A new cross-cutting topic takes number **13** and upward. Never a decimal insert (`08a`),
never a renumber — renumbering breaks every "see 04" reference already written, including
the ones inside `07`'s own checklist.

## 3. ADRs — one global sequence

ADR-002 … ADR-014 exist today; **ADR-015 is next**, regardless of which repo it concerns.

The last four make the case on their own: ADR-011 (Platform scope corrected to backbone
only), ADR-012 (decide/execute split), ADR-013 (three solutions, Platform as NuGet),
ADR-014 (Azure SQL replaces Dataverse). Every one spans repos. A per-repo sequence would
have forced ADR-013 — the decision that *created* the three repos — to be filed under one of
the repos it brought into existence.

- Home is `08_DECISIONS_AND_TECHNICAL_DEBT.md`. Anything long enough to need its own file
  (as ADR-014 did) lives beside it as `ADR-0nn_TITLE.md` and is indexed from 08.
- **Supersede, never delete.** ADR-002 and ADR-009 are marked superseded, not removed. The
  record of what was believed, and why it changed, is worth more than a tidy index. This was
  already 08's own rule; it is restated here because it is the rule most often broken under
  time pressure.

## 4. Conventions are owned by 07, not by this document

**Coding standards and naming conventions live in `07_DEVELOPMENT_GUIDE.md`.** They are
already there, in detail: layer-by-layer standards, naming rules, the vertical-slice
workflow, the review checklist, and the guidance for working with coding agents. This
document does not restate them, and neither should any other.

An earlier draft of this standard did restate them — collecting the conventions scattered
across the migration documents into a §4 here. That was a mistake, and worth recording as
one: it would have created precisely the second source of truth this document exists to
prevent. The conventions were correct; the location was not. They belong **in 07**, added to
what is already there.

### 4.1 What in 07 is now stale

07 was written against V1 — one solution, Dataverse. Six things it says are no longer true:

| 07 currently says | Reality after V2.1 and ADR-014 |
|---|---|
| `dotnet restore NexusAI.slnx`, `dotnet run --project src/NexusAI.Api` | Three solutions: `Nexus.AI.slnx` (libraries, packed not run), `Nexus.Int.slnx` (`/intelligence/v1`), `Nexus.Web.slnx` (`/api/v1` + React client) |
| Prerequisite: "Access to the Development Dataverse environment" | SQL Server LocalDB today; Azure SQL at Stage 4; Dataverse deleted at Stage 3 |
| "Dataverse implementations: `XDataverseRepository`" | `XSqlRepository` under `Sql/Repositories`; the Dataverse variants are deleted at Stage 3 |
| "Keep Dataverse logical names centralized" | One `IEntityTypeConfiguration` per aggregate under `Sql/Configurations` |
| "Confirm the live Dataverse schema/names before coding" | EF code-first: Domain class → configuration → migration → DDL. Nobody hand-writes DDL a migration doesn't know about |
| Vertical slice ends "… → Dataverse verification → Tests → Documentation → Commit" | Same sequence, but "persistence verification". The slice discipline itself is sound and stays |

### 4.2 What is missing from 07

Decided during V2.1 and the SQL migration, not yet written down anywhere canonical. These
belong in 07, not here:

- **The three-solution rule** — Intelligence decides, Platform executes, products own the
  data and the experience. Enforced by NetArchTest architecture tests, and every boundary
  claim in this system has been *proven* by deliberately breaking the test to watch it fail.
  A boundary test nobody has seen fail is a boundary test nobody has verified.
- **No shared kernel** — `Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never
  reference a product type; a product never references an Intelligence-internal type. Two
  layers that seem to need the same shape need a mapper, not a shared class.
- **The `Id` / `Seq` / `Ref` pattern** (ADR-014 Rule 4) — GUID key, `IDENTITY` allocation,
  computed `PERSISTED` reference. `Ref` is computed by the database because only the
  database guarantees uniqueness under concurrent inserts.
- **SQL schemas, not prefixes** (ADR-014 Rule 6) — schema = cluster, table name = C# class
  name verbatim, so code↔database mapping is identity.
- **The C# enum is authoritative** for every status/type value. Never a lookup table.
- **Provider credentials belong to `Nexus.Intelligence.Api` only**, under
  `Platform:Providers:<Provider>:ApiKey`. A product holding a provider key is an
  architectural violation, not a configuration choice. This rule exists because of a real
  incident, not a hypothetical one.
- **Secrets never via `dotnet user-secrets set`** — it parks the value in shell history. Two
  keys leaked that way. Use `set-openai-key.ps1`.
- **Prompt discipline for coding agents** — one concern per prompt, ending in build + test +
  commit, `/clear` between. Never paste staged prompts in sequence: F0–F4 were pasted
  together on 2026-08-19 and only F0 ran, because each stage assumes the previous one's
  build is real. Add *"if you are running low on context, stop at `<boundary>` and say so"*
  to any prompt large enough that a mid-flight stall would be expensive to diagnose.
- **PowerShell native calls** route through an `Invoke-Native`-shaped helper.
  `$ErrorActionPreference='Stop'` plus `2>&1` turns a success message on stderr into a fatal
  error. This has cost time twice.

## 5. When documentation gets updated

Documentation does not get a catch-up pass scheduled for later; later does not arrive.
Updates attach to events that already happen:

- **The final sub-stage of any multi-stage migration** updates the affected numbered
  documents as part of its acceptance criteria — as V2.1 Stage 10 did for 02, 04, 08, 11 and
  12. A migration is not done until its documentation pass is done.
- **Any ADR** is appended to 08 in the session it is decided, never batched.
- **Any convention decision** is added to **07** in the same session — which is how this
  document avoids going stale the way `03_DOMAIN_AND_DATAVERSE.md` did.
- **The Claude-project mirror** is refreshed at the same checkpoint via `project_write`, so
  a fresh AI session is not reading last month's state.

## 6. What every repo's README covers

An orientation and a pointer — not a copy of the hub.

```markdown
# <Repo name>

One sentence: what this repo is, within the three-solution architecture.

## Is / is not
What this repo owns. What it explicitly does not own.

## Local development
How to build and run it today — not aspirationally.

## Documentation
Cross-cutting architecture, conventions and decisions: C:\Personal\NexusAI\docs\
This repo's own detail, if any: ./docs   (only if that folder actually exists)
```

---

## Open items this standard creates

| Item | Where |
|---|---|
| `git mv` the twelve numbered docs into `docs\` | `DOCS_CONSOLIDATION_PROMPT.md` |
| Update 07 — six stale entries (§4.1), nine additions (§4.2) | `DOCS_CONSOLIDATION_PROMPT.md` |
| Rename 03 to `03_DOMAIN_AND_PERSISTENCE.md` and rewrite it | SQL migration Stage 3 |
| Retire `ReadmeV2.md` / `ReadmeV3.md` in `Phase 2\` | With the consolidation |
| Move loose root-level docs (`ADR-014`, `NEXUS_ARCHITECTURE_V2`, `DATAVERSE_SCHEMA_REFERENCE`, `NEXUS_MIGRATION_RUNBOOK`) into `docs\` | With the consolidation |
