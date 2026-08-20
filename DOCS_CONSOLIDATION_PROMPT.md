# Documentation consolidation — Claude Code prompt

Run once, in PowerShell at `C:\Personal\NexusAI`. Commit first — this moves git-tracked
files and you want a clean point to return to.

```powershell
git status --porcelain
git add -A; git commit -m "checkpoint before docs consolidation"
```

---

## Part 1 — Move the canonical set into `docs\`

```
Consolidate the Nexus documentation. Use git mv throughout so history follows the files -
do NOT copy and delete, and do not rewrite any document's content in this part.

CURRENT STATE
  docs\  exists at the repo root and is EMPTY.
  The canonical numbered set is four levels deep, inside a completed phase folder, in an
  extraction folder nested inside a duplicate of itself:

    NexusAI Documentation\Phase 3 end of phase 1 backend\
        NexusAI_Canonical_Documentation_2026-08-16\
            NexusAI_Canonical_Documentation_2026-08-16\
                01_VISION_AND_PRODUCT_MODEL.md
                02_ARCHITECTURE_AND_MODULES.md
                03_DOMAIN_AND_DATAVERSE.md
                04_API_CONTRACT.md
                05_FRONTEND_PRODUCT_DESIGN.md
                06_ENVIRONMENTS_CONFIGURATION_DEPLOYMENT.md
                07_DEVELOPMENT_GUIDE.md
                08_DECISIONS_AND_TECHNICAL_DEBT.md
                09_ROADMAP_AND_MILESTONES.md
                10_CHANGELOG.md
                11_FUTURE_OF_NEXUS_AI.md
                12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md
                README.md

DO
1. git mv all thirteen files (twelve numbered + README.md) into docs\.
   docs\00_DOCUMENTATION_STANDARD.md already exists - leave it alone.

2. git mv these four loose root-level documents into docs\ as well:
       ADR-014_AZURE_SQL_MIGRATION.md
       NEXUS_ARCHITECTURE_V2.md
       NEXUS_MIGRATION_RUNBOOK.md
       DATAVERSE_SCHEMA_REFERENCE.md
   ADR-014 is an ADR with its own file, which 00's section 3 allows; the other three are
   migration references that belong with the docs, not scattered at the repo root.

3. Remove the now-empty nested folders under "Phase 3 end of phase 1 backend". If that
   leaves the Phase 3 folder itself empty, remove it too.

4. LEAVE ALONE: "NexusAI Documentation\Phase1 Complete 08-08-2026\" and
   "NexusAI Documentation\Phase 2\". These are genuine historical snapshots and are kept
   for provenance. Do not move, merge or delete them.

5. In docs\README.md, update the "Documentation map" table: add 00_DOCUMENTATION_STANDARD.md
   as the first row, and add the four documents moved in step 2. Change nothing else in
   that file yet - its content staleness is Part 2's problem.

6. Search the whole repo for references to the old nested path or to the moved filenames
   and update them. Report every reference you changed.

ACCEPTANCE
  1. git status shows renames (R), not delete+add pairs. Paste git status --porcelain.
  2. docs\ contains 00 through 12, README.md, and the four moved documents.
  3. "Phase 3 end of phase 1 backend" is gone; Phase 1 and Phase 2 folders are untouched.
  4. dotnet build Nexus.AI.slnx still succeeds (nothing should reference a .md, but confirm).
```

```powershell
git add -A; git commit -m "docs: consolidate canonical set into docs\"
```

---

## Part 2 — Bring `07_DEVELOPMENT_GUIDE.md` up to V2.1

07 owns coding standards and naming conventions for all three repos. It was written against
V1 — one solution, Dataverse — so parts of it are now false, and several conventions decided
since have never been written down anywhere canonical.

```
Update docs\07_DEVELOPMENT_GUIDE.md to V2.1. Edit it IN PLACE - do not create a
07_DEVELOPMENT_GUIDE_V2.md. The document's own rule says update the canonical subject file
in place, and it also says "Do not add ReadmeV4, Roadmap-New, or nested ZIP archives".

Read docs\00_DOCUMENTATION_STANDARD.md sections 4.1 and 4.2 first - they list exactly what
is stale and what is missing. Then:

PART A - correct these six stale items:

1. Standard local commands. There is no NexusAI.slnx any more. Three solutions:
       dotnet build Nexus.AI.slnx     (libraries - packed via .\pack-local.ps1, never run)
       dotnet build Nexus.Int.slnx    (runs at /intelligence/v1)
       dotnet build Nexus.Web.slnx    (runs at /api/v1, plus the React client)
   Remove the "If NexusAI.Host becomes the canonical process" line - Host is gone.

2. Prerequisites: replace "Access to the Development Dataverse environment" with SQL Server
   LocalDB. Note Azure SQL arrives at migration Stage 4.

3. Naming: "Dataverse implementations: XDataverseRepository" becomes XSqlRepository under
   Sql\Repositories. Note the Dataverse variants are deleted at Stage 3.

4. Infrastructure standards: "Keep Dataverse logical names centralized" becomes one
   IEntityTypeConfiguration per aggregate under Sql\Configurations.

5. "Adding a new feature" step 3: "Confirm the live Dataverse schema/names before coding"
   becomes EF code-first - Domain class, then configuration, then migration, then DDL.
   Nobody hand-writes DDL a migration doesn't know about.

6. The vertical slice sequence: "Dataverse verification" becomes "persistence verification".
   Everything else about the slice discipline is sound - keep it exactly as it is.

PART B - add the conventions listed in 00 section 4.2. Put them where they belong within
07's existing structure rather than appending a new section at the end:
  - the three-solution rule and its architecture tests
  - no shared kernel
  - the Id / Seq / Ref pattern
  - SQL schemas not prefixes
  - C# enum is authoritative for every status/type
  - provider credentials belong to Nexus.Intelligence.Api only
  - secrets never via dotnet user-secrets set
  - prompt discipline for coding agents (07 already has a "Working with coding agents"
    section - extend it, do not duplicate it)
  - PowerShell native calls through Invoke-Native

PART C - update the review checklist at the end so it tests the new rules, not the old
ones. "Are enum numeric values aligned with live choices?" was a Dataverse question; the
equivalent now is whether the C# enum and the EF converter agree.

CONSTRAINT: do not restate anything from 00_DOCUMENTATION_STANDARD.md about where documents
live or how they are numbered. 00 owns that; 07 owns how code is written. Keeping those
separate is the entire point of having both.

ACCEPTANCE:
  1. paste the full updated 07 back to me
  2. confirm no NexusAI.slnx, NexusAI.Host or "live Dataverse schema" reference survives
  3. confirm you edited in place and created no new file
```

```powershell
git add -A; git commit -m "docs: 07 development guide updated to V2.1"
```

---

## Part 3 — the two remaining renames

Do **not** run this yet. `03` is rewritten at SQL migration Stage 3, when Dataverse is
actually deleted — renaming it before its content changes just means touching it twice.

Recorded here so it isn't forgotten:

- `03_DOMAIN_AND_DATAVERSE.md` → `03_DOMAIN_AND_PERSISTENCE.md`, contents rewritten for
  Azure SQL.
- `12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md` keeps its name; its scope note changes to say
  all 21 tables belong to the **Chat product**, not to the platform.
- `Phase 2\ReadmeV2.md` and `Phase 2\ReadmeV3.md` — 07 explicitly forbids this pattern.
  They are inside a historical snapshot folder, so they can stay as history, but nothing new
  should ever be named that way again.
