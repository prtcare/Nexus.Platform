# Database Architecture

**Status:** TRANSITION — the strategy is decided; the one migration that exists predates it, and
every gap below names the milestone that closes it
**Owner:** DATA (Layer 02)
**Last updated:** 2026-08-21
**Layer:** 02 DATA — binding on every layer and every product that persists state
**Authoritative for:** the physical database strategy — how many databases exist and why, which
layer gets which schema, what would justify moving a layer out of the shared database, how access
crosses a schema boundary and why it may never cross a product boundary, how connections are
organised, and who owns a migration.

**This document owns *where data lives*. `DATABASE_STANDARDS.md` owns *how to write schema* —
modelling, the `Id`/`Seq`/`Ref` pattern, keys, indexes, cascade rules, audit columns, concurrency,
column types and migration mechanics.** When a question is "should this be one database or two", it
is here. When it is "what type should this column be", it is there.

> **Known duplication to resolve.** `DATABASE_STANDARDS.md` §2 currently restates the physical
> strategy. Per `DOCUMENTATION_INDEX.md` §1 this document is authoritative for it; §2 there should
> shrink to a link. Until it does, if the two disagree, this one wins.

---

## 1. The shape, in one picture

```
NexusPlatform                     one database, one schema per layer
├── core          01 CORE          identity, tenancy, authorization, audit, usage
├── data          02 DATA          documents, versions, knowledge, retrieval
├── governance    03 GOVERNANCE    products, technology, brand, compliance, licences
├── ai            04 AI            agents, prompts, traces, memory, evaluations
├── automation    05 AUTOMATION    workflows, jobs, queues, approvals
├── product_core  06 PRODUCT CORE  workspace, project, subproject, subscriptions
├── developer     07 DEVELOPER     work graph, workers, runs, reviews, progress
├── delivery      08 DELIVERY      repositories, pipelines, artifacts, environments
├── assurance     09 ASSURANCE     criteria, methods, runs, evidence, verdicts
├── operations    10 OPERATIONS    logs, metrics, traces, health, incidents, cost
└── experience    11 EXPERIENCE    conversations, messages, participants, bindings

NexusChat        ─┐
NexusVault        │  12 PRODUCTS — one database each, no shared tables,
NexusTrips        │  no cross-product foreign keys, removable independently
…                ─┘
```

Two rules, and the whole strategy follows from them:

1. **Layers 01–11 share one database and take a schema each.**
2. **Every Layer 12 product gets its own database.**

---

## 2. Why the layers share one database

Not because it is simpler — because the alternative is wrong for what these layers are.

| Reason | Consequence |
|---|---|
| **One lifecycle** | The eleven layers version and release together. Eleven databases would mean eleven backup schedules and eleven restore procedures for one system |
| **One restore story** | After the 2026-08-20 git incident, "restore the system to a consistent point" is not a hypothetical requirement. Eleven databases means eleven restore points that must agree |
| **Occasional cross-layer transactions** | A work item completing writes a DEVELOPER row and reads a GOVERNANCE row in the same operation. Across databases that needs distributed transactions or an outbox, for a problem that does not exist yet |
| **One migration story** | One `dotnet ef database update` brings the whole platform to a known state |
| **Schema-per-layer is a seam, not a compromise** | Moving a layer to its own database later is a connection-string change, not a rename, because nothing outside a layer references its schema by name |

That last row is the one that makes the decision cheap to reverse, and it is the reason the
convention matters more than the current arrangement: **the schema name is the seam.** A layer that
has always written to `operations.` can be lifted into its own database without touching a table
name, an `IEntityTypeConfiguration`, or a repository.

---

## 3. Why products do not

A product database is a different kind of thing from a layer schema:

- **Its own retention and residency.** A consumer product may carry personal data under obligations
  that no platform table shares. GOVERNANCE records the obligation; the database boundary is what
  makes it enforceable.
- **Its own lifecycle.** Products are proposed, active, sunsetting, retired. Layers are not.
- **It must be removable.** Retiring a product should be dropping a database, not a surgical delete
  across shared tables with foreign keys pointing in from four layers.
- **It makes an invariant physical.** *Products never reference each other* is a rule someone can
  break in an afternoon if the tables are adjacent. **Two products cannot share a foreign key if
  they cannot share a database.** The separation converts an aspiration into a constraint the
  database engine enforces.

---

## 4. Schema per layer

**TARGET — `M-02-1.5` Layer schema convention.** Each layer owns exactly one schema and writes to no
other. The schema name is the short name, lowercased.

| # | Layer | Schema | Exists today |
|---|---|---|---|
| 01 | CORE | `core` | No |
| 02 | DATA | `data` | No |
| 03 | GOVERNANCE | `governance` | No |
| 04 | AI | `ai` | No |
| 05 | AUTOMATION | `automation` | No |
| 06 | PRODUCT CORE | `product_core` | No |
| 07 | DEVELOPER | `developer` | No |
| 08 | DELIVERY | `delivery` | No |
| 09 | ASSURANCE | `assurance` | No |
| 10 | OPERATIONS | `operations` | No |
| 11 | EXPERIENCE | `experience` | No |
| 12 | PRODUCTS | — its own database | `NexusChatDbContext` exists |

**Schemas replaced prefixes.** The Dataverse-era `T_nnn_` numbering is gone; a table name is the C#
class name verbatim, and the schema carries the ownership. This is already true in
`20260820180802_InitialSqlSchema.cs` — no prefixed names appear in it.

### 4.1 CURRENT — the `org` schema

The only migration that exists created **`[org].[Workspace]`**. `org` is not one of the eleven layer
schemas. It is a pre-convention name that exists in running, proven code — `api_run.log` at
2026-08-20 18:09 UTC records two successful inserts against it — and it must not be silently
contradicted, because a developer reading this document still has to be able to build what is on
disk.

| | |
|---|---|
| **CURRENT** | `[org].[Workspace]`, configured by `WorkspaceConfiguration.cs`, served by `SqlWorkspaceRepository.cs`, inside `NexusChatDbContext` |
| **TARGET** | `Workspace` moves to Layer 06 and to the `product_core` schema — `M-06-1.1` moves the entity, `M-02-1.5` establishes the convention |
| **The rule in the meantime** | `org` is correct in `Nexus.Products.Chat.Infrastructure` and wrong everywhere else. **Do not add a second table to `org`.** New work targets its layer's schema from its first migration |

### 4.2 Schema ownership is enforced, not requested

**TARGET — `M-02-1.5`.** An architecture test fails any `IEntityTypeConfiguration` that calls
`ToTable(..., schema)` with a schema its assembly does not own. The assembly-to-schema mapping is
data, not a chain of `if` statements — the same principle that bans product branching.

Today there is no such test, and there is no pipeline that would run it if there were.
`DEPENDENCY_RULES.md` §7 carries the full enforcement inventory.

---

## 5. Cross-schema access

Inside `NexusPlatform`, a schema boundary is a real boundary even though the tables are adjacent.

| Situation | Rule |
|---|---|
| Reading another layer's table directly | **Forbidden.** Go through the owning layer's repository or API. Adjacency is not permission |
| A foreign key crossing schemas | **Allowed only where `DEPENDENCY_RULES.md` §4 allows a reference** — downward, or to a cross-cutting layer |
| A foreign key the direction forbids | **Polymorphic and constraint-free.** Store layer, type and id as plain columns with no FK. Referential integrity becomes the owning layer's problem, enforced in application code and proven by test |
| A query joining two layers' tables | Treat exactly as a foreign key: allowed downward, otherwise compose in application code |

The standing example of the third row is ASSURANCE pointing at a DEVELOPER work item. ASSURANCE is
cross-cutting and must not reference DEVELOPER, so `TraceabilityLink` stores what it verified as
plain columns. The trade is deliberate: losing database-enforced integrity is cheaper than welding
two layers together in the schema, because the weld is what stops either from moving later.

---

## 6. Cross-product access — forbidden

| | |
|---|---|
| Cross-product foreign keys | **Cannot exist.** Different databases |
| Cross-product joins | **Cannot exist.** Same reason |
| A product reading another product's database | **Forbidden.** Through that product's API, per `API_STANDARDS.md` |
| A product holding another product's connection string | **Forbidden.** It never receives one. `CONFIGURATION_STANDARDS.md` governs where connection strings live |
| A platform table inside a product database | **Forbidden.** Copying `Product` or `Workspace` into a product database is the boundary being broken, not an optimisation |
| A product table inside `NexusPlatform` | **Forbidden.** Including "just this one lookup table" |

---

## 7. What would justify splitting a layer out

A layer leaves `NexusPlatform` for its own database when a specific, measured condition holds — not
because it feels large.

| Trigger | Why it is decisive |
|---|---|
| **A different data shape** | Time-series and append-only workloads have different indexing, retention and compaction needs than relational entity storage. Sharing a store means one of them is being served badly |
| **A different retention or residency obligation** | If GOVERNANCE records an obligation that applies to one layer's data and not the rest, the boundary should be physical |
| **Write volume that affects other layers** | Contention, log growth or backup duration degrading unrelated layers is a measurement, not an opinion |
| **A different availability requirement** | A layer that must survive the platform database being down cannot live in it |
| **A different scaling axis** | Read replicas or a purpose-built store that only one layer needs |

Two candidates are already predictable. **Neither is decided.**

| Candidate | Argument | Status |
|---|---|---|
| **`operations`** | Time-series shaped, append-heavy, with retention and compaction needs nothing else in the platform has. The most likely to move first, and probably to a purpose-built store rather than another SQL database | **Predicted, not decided.** Revisit after `M-10-2.2` Metrics and distributed tracing, when the volume is real rather than estimated |
| **`developer`** | Will carry the highest write volume of any layer once autonomous runs begin at `M-07-3.2` — every run, build record, test run and state transition. The candidate name is `NexusDeveloper` | **Predicted, not decided.** Revisit at P3, when autonomous run volume is measured rather than estimated |

**Do not pre-split either one.** The schema convention is what makes both splits cheap; splitting
early costs the cross-layer transaction and the single restore point for a benefit nobody has
measured. `M-02-1.5` exists precisely so that this decision stays deferrable.

---

## 8. Connection management

**Mostly TARGET.** Today there is one database, reached from one host, on one machine.

| Rule | State |
|---|---|
| One connection string per database, per environment | CURRENT for the single database that exists |
| **One `DbContext` per database** — never per schema, never per layer | CURRENT. `NexusChatDbContext` is a product context. The platform context will hold eleven schemas |
| Each layer contributes its `IEntityTypeConfiguration` types to the platform context by scanning its own Infrastructure assembly | **TARGET — `M-02-1.5`** |
| A host receives connection strings only for databases it may reach | **TARGET.** `CONFIGURATION_STANDARDS.md` owns the mechanism and what may never enter git |
| Connection strings are never in source, never in a document, never in a migration | Binding now. `SECURITY_STANDARDS.md` §5 |
| Local development targets SQL Server LocalDB | CURRENT. `LOCAL_DEVELOPMENT.md` has the exact topology |
| Design-time contexts exist so EF tooling never boots a host | CURRENT — `NexusChatDbContextFactory`. It is a design-time type and is never used at runtime |

**The consequence people trip over:** because a `DbContext` maps to a *database* and not to a layer,
two migrations generated against the platform context conflict on the model snapshot **even when
they touch different schemas and different tables.** That is why "no shared schema mutation" is a
hard parallel-safety rule and not advice — `DEVELOPMENT_WORKFLOW.md` §4.1 owns the scheduling
consequence, `GIT_WORKFLOW.md` owns the conflict procedure, `DATABASE_STANDARDS.md` §9.3 owns the
resolution.

---

## 9. Migration ownership

| Rule | Statement |
|---|---|
| A migration belongs to the layer whose schema it changes | A migration touching two schemas is two migrations, or the boundary is wrong |
| One migration per work item, one author | A work item producing two migrations was two work items |
| A pushed migration is immutable | Correct it with a new migration, never by editing |
| Generated, never hand-written | Change the model and regenerate. Data movement is the only exception |
| Applying migrations | **CURRENT: entirely manual.** Every migration to date was applied by a developer running the CLI against LocalDB. CI application is **TARGET — `M-08-1.2`**; deployed environments are **TARGET — `M-08-5.1`** |

`DATABASE_STANDARDS.md` §9 owns naming, the `Down` requirement, data migrations and the model
snapshot procedure. It is not restated here.

---

## 10. Current state, in full

| Claim | State |
|---|---|
| Azure SQL / SQL Server LocalDB is the store | **CURRENT.** `Microsoft.Data.SqlClient`, ADR-014 |
| EF Core code-first: class → configuration → migration → DDL | **CURRENT.** Nobody writes DDL by hand |
| `Id`/`Seq`/`Ref` allocated by the database | **CURRENT and proven.** `api_run.log`, 2026-08-20 18:09 UTC |
| Schemas replace table prefixes | **CURRENT** |
| One database exists | **CURRENT** — it holds the Chat product |
| `NexusPlatform` database | **TARGET.** Does not exist |
| Layer-schema convention | **TARGET — `M-02-1.5`.** The one migration used `org` |
| Dataverse removed | **TRANSITION — `M-02-1.4`.** Ten of eleven Chat aggregates still run on Dataverse implementations, behind the `Nexus:Persistence` switch |
| Per-product database separation | **TARGET.** One product exists, so the rule has never been tested |
| Schema-ownership architecture test | **TARGET — `M-02-1.5`** |
| Automated migration application | **TARGET — `M-08-1.2`, then `M-08-5.1`.** None exists anywhere |
| Backup and restore | **TARGET — `M-08-7.1`, `M-08-7.2`.** No database backup procedure exists. The only backup event in this system's history was an unplanned git recovery |

That last row deserves to be read twice. There is a proven insert path and no proven restore path.

---

## 11. Open decisions

| Question | Decided by | Status |
|---|---|---|
| Does `operations` leave `NexusPlatform`, and for what kind of store? | Layer 10 sizing, after `M-10-2.2` | Not yet decided |
| Does `developer` split to `NexusDeveloper`? | P3, on measured autonomous run volume | Not yet decided |
| Vector storage mechanism for embeddings | `M-02-4.2` | Not yet decided |
| Full-text search mechanism | `M-02-4.1` | Not yet decided |
| Outbox mechanism for cross-database consistency | `M-01-8.1`, then beyond | Not yet decided — and not needed while there is one database |
| Read replica or a separate reporting store | Not raised | Not yet decided |
| Cloud provider and hosting shape for the database | `M-08-4.2` provisioning, P2 | Genuinely open — nothing is deployed anywhere |

---

## 12. References

- `DATABASE_STANDARDS.md` — how to write schema, migrations, keys and the `Id`/`Seq`/`Ref` pattern.
- `LAYER_MODEL.md` — which layer owns which schema, and what each layer is.
- `DATA_OWNERSHIP.md` — which entities go into each schema.
- `DEPENDENCY_RULES.md` — the reference direction that governs which foreign keys may cross a
  schema.
- `CONFIGURATION_STANDARDS.md` — connection strings, per-environment targets, what may never enter
  git.
- `SECURITY_STANDARDS.md` — tenant isolation, encryption, and the query filter.
- `DEVELOPMENT_WORKFLOW.md` §4 — the parallel-safety consequence of one `DbContext` per database.
- `LOCAL_DEVELOPMENT.md` — the exact local database topology today.
- ADR-014 Azure SQL migration — the originating decision record. The next ADR number is ADR-016.
