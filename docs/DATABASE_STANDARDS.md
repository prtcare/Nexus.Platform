# Database Standards

> **SUPERSEDED NUMBERING NOTICE (2026-09-05):** This document's layer-to-schema
> table and "Layer 12 PRODUCTS" wording are built on the v2.1 twelve-layer
> model, in which 07 DEVELOPER and 12 PRODUCTS were numbered Platform layers.
> Per the approved v2.2 renumbering (`LAYER_MODEL.md` §2.2, §4a), Nexus Forge,
> Nexus Developer (the product), and Products all now sit OUTSIDE the ten
> numbered Platform layers, and DELIVERY/ASSURANCE/OPERATIONS/EXPERIENCE are
> renumbered 07/08/09/10. The schema-per-layer convention and the cross-cutting
> dependency rule (08/09/10 in the old numbering -- now 07/08/09) remain valid.
> Re-deriving the layer-to-schema table against the v2.2 numbering is
> Wave-D-adjacent decision work and is explicitly NOT done in this batch.

**Status:** Active
**Owner:** DATA (Layer 02)
**Last updated:** 2026-08-21
**Layer:** 02 DATA — with binding effect on every layer that persists state
**Authoritative for:** relational persistence, EF Core code-first modelling, schema ownership,
database separation, the Id/Seq/Ref pattern, keys, indexes, relationships, audit columns,
concurrency, soft delete, migration naming and ownership, cross-schema and cross-product access,
transactions, stored procedure and view policy, JSON columns, date/time, money, decimals, units,
and reference numbering.

Not authoritative for: connection string storage (see `CONFIGURATION_STANDARDS.md`), credential
handling and encryption key custody (see `SECURITY_STANDARDS.md`), the HTTP shape of the data
(see `API_STANDARDS.md`), when a migration may be merged (see `DEVELOPMENT_WORKFLOW.md` and
`GIT_WORKFLOW.md`), how a schema rule is proven (see `ASSURANCE_STANDARDS.md`).

---

## 1. Position

Azure SQL is the persistence backend for Nexus. This was decided in ADR-014 and it is not an open
question. Every statement in this document that is marked **CURRENT** is running code today;
everything marked **TARGET** names the milestone that closes the gap.

| Decision | State | Evidence or milestone |
|---|---|---|
| Azure SQL / SQL Server LocalDB is the store | CURRENT | `Microsoft.Data.SqlClient` in use; `api_run.log` 2026-08-20 18:09 UTC |
| EF Core, code-first | CURRENT | `NexusChatDbContext`, `Migrations/20260820180802_InitialSqlSchema.cs` |
| Id/Seq/Ref pattern | CURRENT, proven | Two successive `Workspace` inserts returned server-generated `Ref` and `Seq` |
| Schemas replace table-name prefixes | CURRENT | No `T_nnn_` names in `InitialSqlSchema` |
| Layer-schema convention (`core`, `data`, …) | **TARGET — M-02-1.5 Layer schema convention** | First migration used schema `org` |
| Dataverse removed entirely | **TRANSITION — M-02-1.4 Delete Dataverse** | Dataverse implementations still present for 10 of 11 aggregates |
| Per-product database | TARGET — first product build | Only the Chat product database exists |

The single most important consequence of ADR-014: **the database is generated from C#, never the
other way round.** The path is always `domain class → IEntityTypeConfiguration → migration → DDL`.
Nobody writes DDL by hand. Nobody edits a generated migration to change the model; they change the
model and regenerate.

---

## 2. Physical strategy

### 2.1 One platform database, one schema per layer

**TARGET — M-02-1.5.** One database named `NexusPlatform` holds every Nexus layer. Each layer owns
exactly one schema and writes to no other:

| Layer | Schema |
|---|---|
| 01 CORE | `core` |
| 02 DATA | `data` |
| 03 GOVERNANCE | `governance` |
| 04 AI | `ai` |
| 05 AUTOMATION | `automation` |
| 06 PRODUCT CORE | `product_core` |
| 07 DEVELOPER | `developer` |
| 08 DELIVERY | `delivery` |
| 09 ASSURANCE | `assurance` |
| 10 OPERATIONS | `operations` |
| 11 EXPERIENCE | `experience` |

Layer 12 PRODUCTS does not appear in this table: a product gets its own database (§2.3).

The reason for schema-per-layer rather than database-per-layer today is that a schema can be
lifted into its own database later without renaming a single table, configuration or repository.
The schema name is the seam. OPERATIONS is the layer most likely to move first, because its data
is time-series shaped and will outgrow a shared relational store before anything else does.

**CURRENT.** The first migration, `20260820180802_InitialSqlSchema.cs`, created `[org].[Workspace]`.
`org` is not one of the eleven layer schemas. It is a pre-convention name that exists in running
code and must not be silently contradicted: a developer reading this document must still be able to
build what is on disk. M-02-1.5 renames it, and until that milestone lands, `org` is correct in
`Nexus.Products.Chat.Infrastructure` and wrong everywhere else. Do not add a second table to `org`.

**Rule:** new work targets its layer's schema from the first migration. Nothing new goes into `org`.

### 2.2 Schema ownership is enforced, not requested

**TARGET — M-02-1.5.** An architecture test fails any `IEntityTypeConfiguration` that calls
`ToTable(..., schema)` with a schema its assembly does not own. The mapping from assembly to schema
is data, not a chain of `if` statements. This is the same enforcement style already in use for
layer boundaries via NetArchTest (`PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs`,
`BoundaryTests.cs`) — see `ASSURANCE_STANDARDS.md` for how architecture tests are qualified.

### 2.3 Product database separation

Every Layer 12 product gets its own database. A product database contains only that product's
schema; it never contains a copy of a platform table, and no platform database contains a product
table.

This falls directly out of the architectural invariant that products never reference each other.
Two products cannot share a foreign key if they cannot share a database. The separation makes the
invariant physical instead of aspirational.

**CURRENT.** `NexusChatDbContext` in `Nexus.Products.Chat.Infrastructure` is a product DbContext.
It owns the eleven Chat aggregates — `Adr`, `Artifact`, `Branch`, `Conversation`,
`ConversationMessage`, `Knowledge`, `Project`, `Session`, `Snapshot`, `WorkItem`, `Workspace` —
of which only `Workspace` has a SQL configuration and a SQL repository so far
(`WorkspaceConfiguration.cs`, `SqlWorkspaceRepository.cs`). The remaining ten still run on
Dataverse implementations. That is a **TRANSITION** state closed by M-02-1.2, M-02-1.3 and
M-02-1.4.

### 2.4 One DbContext per database

A DbContext maps one-to-one to a database, never to a schema and never to a layer. When the
platform database holds eleven schemas, it is still served by a platform DbContext; each layer
contributes its `IEntityTypeConfiguration` types to that context through assembly scanning of its
own Infrastructure assembly.

The consequence is the parallel-safety rule that is most often got wrong: **two migrations against
one DbContext conflict on the model snapshot even when they touch different tables and different
schemas.** See `DEVELOPMENT_WORKFLOW.md` §parallel safety.

---

## 3. The Id / Seq / Ref pattern

Every entity that a human will ever refer to carries three identity columns. This is the single most
load-bearing convention in the database and it is proven working.

| Column | Type | Role | Visible to users | Stable |
|---|---|---|---|---|
| `Id` | `uniqueidentifier` | Primary key, all joins and foreign keys | No | Yes, forever |
| `Seq` | `int IDENTITY(1,1)` | Allocation only — feeds `Ref` | No | Yes |
| `Ref` | computed, `PERSISTED`, unique | The human-facing reference | Yes | Yes, forever |

### 3.1 The proven SQL

This is the exact expression in `WorkspaceConfiguration.cs`, reproduced verbatim:

```
('WKS-' + RIGHT('00000000' + CAST([Seq] AS varchar(8)), 8))
```

Applied through EF Core:

```csharp
builder.Property<int>("Seq")
       .ValueGeneratedOnAdd()
       .UseIdentityColumn();

builder.Property(w => w.Ref)
       .HasComputedColumnSql(
           "('WKS-' + RIGHT('00000000' + CAST([Seq] AS varchar(8)), 8))",
           stored: true);

builder.HasIndex(w => w.Ref).IsUnique();
```

`Workspace` number 1 is `WKS-00000001`. Number 12345678 is `WKS-12345678`. Number 12345679 overflows
the eight-digit window; the format then widens rather than truncating, and the widening is a
migration, not a runtime surprise. Eight digits is deliberate headroom, not a limit anyone expects
to reach.

### 3.2 Why the database computes it

`Ref` is computed in SQL Server and not in C# because **only the database guarantees uniqueness
under concurrent insert.** A C#-side counter requires either a read-modify-write round trip, a
distributed lock, or optimistic retry — all three are wrong answers to a problem `IDENTITY` already
solved. `PERSISTED` means the value is materialised on disk once at insert, so it can be indexed and
read without recomputation.

### 3.3 Evidence

From `api_run.log`, 2026-08-20 18:09 UTC:

```
INSERT INTO [org].[Workspace] (...) OUTPUT INSERTED.[Ref], INSERTED.[Seq] VALUES (...)
```

Two successive inserts each returned a server-generated `Ref` and `Seq`. This is not a design
proposal. It runs.

### 3.4 `Seq` is a shadow property

`Seq` does not appear on the domain class. It is declared as an EF Core shadow property, exists only
to drive the computed column, and is never surfaced through the API, never used as a foreign key,
and never shown to a user. If application code needs `Seq`, the design is wrong: it wants `Ref`.

### 3.5 Strongly-typed IDs

**CURRENT.** Each aggregate carries a strongly-typed ID type — `WorkspaceId`, `ProjectId`,
`ConversationId` — in its aggregate folder alongside `<Name>.cs`, `<Name>Status.cs` and
`I<Name>Repository.cs`. The type is a wrapper around a `Guid`.

The value converters that map `WorkspaceId` to `uniqueidentifier`, and status enums to their stored
representation, live in exactly one place: `Nexus.Products.Chat.Infrastructure/Sql/Conventions/
StronglyTypedIdConverters.cs`.

**This is a hard boundary rule.** Converters live only in Infrastructure. A converter in Domain
would make the domain model know about relational storage; a converter in Application would leak
persistence concerns into orchestration; a converter duplicated in both would drift. The Domain
project must not reference any EF Core assembly, and the architecture tests
(`BoundaryTests.cs`) are the mechanism that keeps it honest.

### 3.6 Reference numbering

The `Ref` prefix is a short uppercase token identifying the entity type, followed by a hyphen and
eight zero-padded digits.

| Rule | Statement |
|---|---|
| Format | `<PREFIX>-<8 digits>`, e.g. `WKS-00000001` |
| Prefix length | 2–4 uppercase ASCII letters |
| Prefix uniqueness | Globally unique across all layers and all products |
| Allocation | A prefix is registered before first use; two entities never share one |
| Immutability | A `Ref` is never reissued, never renumbered, never reused after delete |
| Sequence scope | Per entity type, per database — sequences are not global |

**CURRENT: exactly one prefix exists — `WKS-` for `Workspace`.** Every other prefix is unallocated.
Do not assume a prefix for an entity you are about to build; allocate it, record it, then use it.

**TARGET.** The prefix registry becomes a GOVERNANCE record under M-03-6.1 Configuration registry.
Until then the registry is this section of this document, and adding a prefix means editing it.

---

## 4. Modelling

### 4.1 Table names

The table name is the C# class name, verbatim, singular, PascalCase. `Workspace` maps to
`[org].[Workspace]` today and to `[product_core].[Workspace]` after M-02-1.5. There is no
pluralisation, no prefix, no numbering.

The Dataverse-era `T_nnn_` numbering scheme is dead. It existed because Dataverse had one flat
namespace; SQL Server has schemas, and **schemas replace prefixes**. Anyone who proposes reviving
numeric prefixes is proposing to solve a problem that no longer exists.

### 4.2 Column names

Column name equals property name. No Hungarian notation, no type suffixes, no abbreviations that
are not already in the domain vocabulary.

### 4.3 Nullability

A column is nullable only when the domain permits absence. `string?` in C# is `NULL`-able in SQL;
`string` is `NOT NULL`. Nullable reference types are enabled and are the source of truth — do not
override nullability in configuration to paper over a domain modelling mistake.

### 4.4 String lengths

Every `nvarchar` column declares an explicit maximum length. `nvarchar(max)` is permitted only for
genuinely unbounded text — a document body, a message content, a stack trace — and never for a name,
a code, a status or anything that will be indexed or filtered.

| Kind | Length |
|---|---|
| `Ref` and codes | `nvarchar(32)` |
| Names and titles | `nvarchar(200)` |
| Descriptions | `nvarchar(2000)` |
| Free text and bodies | `nvarchar(max)` |
| Email | `nvarchar(320)` |
| URL | `nvarchar(2048)` |

### 4.5 Enums

A status enum is stored as its `int` value by default, converted in `StronglyTypedIdConverters.cs`.
An enum stored as a string is permitted where the value is read directly by humans in query tools,
but the choice is made once per enum and recorded in its configuration, never mixed.

Adding an enum member is additive and safe. Renumbering existing members is a data migration and is
treated as a breaking change.

### 4.6 Domain rehydration

**CURRENT.** Every aggregate exposes `public static <Name> Restore(...)` with a private constructor.
The repository calls `Restore` when materialising from SQL. EF Core never sets private state through
reflection into a constructor the domain did not sanction, and the domain never gains a public
parameterless constructor purely to satisfy an ORM.

---

## 5. Keys, indexes and relationships

### 5.1 Keys

| Kind | Rule |
|---|---|
| Primary key | Always `Id`, always `uniqueidentifier`, always clustered=false |
| Clustered index | On `Seq`, because it is monotonically increasing and avoids page splits |
| Natural keys | Modelled as unique indexes, never as the primary key |
| Composite primary keys | Not used, including on join tables — a join table gets its own `Id` |

The primary key is a non-clustered `uniqueidentifier` and the clustered index is on `Seq`. A
clustered index on a random `Guid` fragments every page it touches; `Seq` exists anyway and is
perfectly ordered.

### 5.2 Indexes

Every index is declared in the entity configuration, never created by hand in a migration and never
created directly against a database. Index at minimum:

- every foreign key column;
- `Ref` (unique);
- every column used in a tenant filter (see `SECURITY_STANDARDS.md` for the tenant filter itself);
- any column a listing endpoint sorts or filters by (see `API_STANDARDS.md` §pagination).

Index naming follows EF Core's generated convention. Do not rename indexes for cosmetic reasons; a
renamed index is a migration for no benefit.

### 5.3 Relationships and the cascade rule

This is the ADR-014 decision most likely to be lost, so it is stated in full.

SQL Server rejects a schema in which a delete can reach the same row by more than one path. The
error is:

```
Msg 1785 — Introducing FOREIGN KEY constraint '...' on table '...' may cause cycles
or multiple cascade paths.
```

EF Core's default `DeleteBehavior` for a required relationship is `Cascade`, so the default will
eventually produce this error in any model with more than a trivial graph. The rule that avoids it:

| Relationship kind | `DeleteBehavior` | Rationale |
|---|---|---|
| Owning parent → owned child | `Cascade` | Exactly one owner may cascade |
| Any other reference | `Restrict` | Deleting a referenced row must fail loudly |
| Self-reference | `NoAction` | Cascading into yourself is always a cycle |

Stated as a single sentence: **only the owning parent cascades; all reference foreign keys are
`Restrict`; all self-references are `NoAction`.**

`Restrict` means a delete that would orphan a reference throws. That is the correct behaviour: the
application decides what to do about the reference, the database does not silently guess.

Every relationship declares its `DeleteBehavior` explicitly, even when the explicit value equals the
default. Relying on the default is how error 1785 gets discovered at migration time by whoever is
unluckiest.

### 5.4 Cross-schema references

**TARGET — M-02-1.5.** A foreign key may cross schemas within `NexusPlatform` only when the
dependency direction permits it: a layer may reference only layers below it, and 08 DELIVERY,
09 ASSURANCE and 10 OPERATIONS are cross-cutting and may be referenced from anywhere.

Where a reference would violate the direction — for example ASSURANCE pointing at a DEVELOPER work
item — the link is **polymorphic and constraint-free**: store the layer, the type and the id as
plain columns, with no foreign key. The referential integrity is then the owning layer's problem,
enforced in application code and proven by test, which is the correct trade for not welding two
layers together in the schema.

### 5.5 Cross-product data access

There are no cross-product foreign keys, because products do not share a database. A product that
needs another product's data obtains it through that product's API (`API_STANDARDS.md`), never by
opening its database. A product never receives a connection string to another product's database;
`CONFIGURATION_STANDARDS.md` §what must never enter Git governs where those strings live.

---

## 6. Audit columns

Every persisted entity carries the same four audit columns, plus tenant ownership where applicable.

| Column | Type | Set by | Mutable |
|---|---|---|---|
| `CreatedAtUtc` | `datetime2(3)` | Persistence, on insert | No |
| `CreatedBy` | `uniqueidentifier` | Persistence, from the resolved identity | No |
| `ModifiedAtUtc` | `datetime2(3)` | Persistence, on every update | Yes |
| `ModifiedBy` | `uniqueidentifier` | Persistence, from the resolved identity | Yes |

These are set in `SaveChanges`/`SaveChangesAsync` interception, never by the caller and never by the
domain. A caller who can set `CreatedBy` can forge attribution.

**TRANSITION.** `CreatedBy` and `ModifiedBy` become meaningful only when there is a real user to
attribute to. Until **M-01-1.2 Authentication flow** and **M-01-2.1 Organisation and tenant with
enforced isolation**, `ResolvedIdentity` is not backed by a real principal. The columns are created
now so that the schema does not change later, and populated with the resolved identity as soon as
one exists.

These four columns are structural audit — who last touched the row. They are not the audit *log*.
The durable audit log is a separate CORE concern (`IAuditLog`, `AuditEntry` in
`Nexus.Platform.Contracts/Governance/`), currently backed by `ConsoleAuditLog` and made durable at
**M-01-4.1 Durable audit log**. What must be audited, and what must never appear in an audit
record, is in `SECURITY_STANDARDS.md`.

---

## 7. Concurrency

Every mutable entity carries a `rowversion` concurrency token:

```csharp
builder.Property<byte[]>("RowVersion").IsRowVersion();
```

`rowversion` is maintained by SQL Server, costs nothing to keep current, and makes EF Core throw
`DbUpdateConcurrencyException` when a row changed between read and write.

| Situation | Handling |
|---|---|
| Concurrent update detected | Surface as `409 Conflict` — see `API_STANDARDS.md` |
| Retry | Never automatic on a write; the caller decides |
| Optimistic token exposure | The token is opaque; it is sent as an ETag, never as a raw byte array |

Pessimistic locking, `UPDLOCK` hints and explicit transaction escalation are not used. If a design
appears to need them, the aggregate boundary is wrong.

---

## 8. Soft delete

**Position: hard delete is the default; soft delete is opt-in per entity and must be justified.**

An entity is soft-deleted only when at least one of these is true:

1. It is referenced by records the system must retain (an audit trail, a financial record);
2. A retention obligation applies to it;
3. Its `Ref` has been communicated outside the system and must remain resolvable.

Soft delete is implemented as `DeletedAtUtc` (`datetime2(3)`, nullable) plus `DeletedBy`, with an EF
Core global query filter excluding deleted rows. It is never implemented as an `IsDeleted` boolean —
a boolean loses when and by whom, which are the only two facts anyone ever wants afterwards.

A soft-deleted row keeps its unique constraints. This means a soft-deleted `Workspace` still holds
its `Ref`, which is correct: the reference must remain resolvable, and reissuing it would be a lie.

**Not yet decided:** which entities are soft-deleted, and what the retention period is for each.
This is decided by **M-02-5.1 Classification and retention**, which introduces the classification
and retention model. Until that milestone, any proposal to soft-delete an entity must be recorded in
the entity's configuration with a one-line justification, and no entity is soft-deleted by default.

---

## 9. Migrations

### 9.1 Naming

`<timestamp>_<PascalCaseName>.cs`, exactly as EF Core generates it. The proven example:

```
Migrations/20260820180802_InitialSqlSchema.cs
```

The name describes the change, not the ticket. `AddTenantIdToWorkspace` is a good name.
`WI0712` is not. `Update3` is not.

### 9.2 Ownership

| Rule | Statement |
|---|---|
| One migration per work item | A work item that produces two migrations was two work items |
| One author per migration | Migrations are never co-authored or merged by hand |
| Never edited after push | A pushed migration is immutable; correct it with a new migration |
| Never edited to fix a model | Change the model, drop the migration, regenerate |
| Generated, not written | Nobody hand-writes `Up`/`Down` except for data movement |
| Down must work | Every migration is reversible or explicitly documents why it is not |

### 9.3 Migration and the model snapshot

The model snapshot file is a single shared file per DbContext. Two migrations generated in parallel
against the same DbContext will both modify it, and git cannot merge the result meaningfully. The
resolution is never a manual merge of the snapshot; it is: rebase, delete the losing migration,
regenerate it against the updated snapshot, and re-verify.

This is why **no shared schema mutation** is a parallel-safety rule and not merely advice.
`DEVELOPMENT_WORKFLOW.md` owns the scheduling consequence; `GIT_WORKFLOW.md` owns the conflict
procedure.

### 9.4 Applying migrations

| Environment | How |
|---|---|
| Local development | `dotnet ef database update` against LocalDB, by the developer |
| CI | **TARGET — M-08-1.2 Pipelines on every repository.** No CI exists today |
| Deployed environments | **TARGET — M-08-5.1 Automated deployment** |

**CURRENT reality: there is no automated migration application anywhere.** Every migration to date
has been applied by a developer running the CLI against LocalDB. Any document that describes a
migration pipeline is describing the future.

`NexusChatDbContextFactory` exists so that the EF Core design-time tooling can construct the context
without booting the API host. It is a design-time type; it is never used at runtime.

### 9.5 Data migrations

A migration that moves data, rather than only changing shape, is written as explicit SQL inside the
migration's `Up`, is idempotent, and is tested against a restored copy of realistic data before it
runs anywhere that matters. Data migrations are never combined with schema migrations in the same
file — when the data step fails, you want the schema step already committed and the failure isolated.

---

## 10. Access patterns

### 10.1 Repositories

**CURRENT.** Persistence is reached through one repository interface per aggregate, defined in
Domain (`IWorkspaceRepository` in the aggregate folder) and implemented in Infrastructure
(`SqlWorkspaceRepository`). The naming convention is `Sql<Name>Repository`; EF configurations are
`<Name>Configuration`. Application code depends on the interface and never on `NexusChatDbContext`.

An `IQueryable` never leaves the repository. Returning `IQueryable` moves query composition — and
therefore the tenant filter, the soft-delete filter and the `N+1` risk — outside the only place that
understands them.

### 10.2 Transactions

| Rule | Statement |
|---|---|
| Unit of work | One `SaveChangesAsync` per request is the target; EF Core wraps it in a transaction |
| Explicit transactions | Only when two `SaveChanges` calls must succeed together |
| Scope | A transaction never spans an HTTP call, a model invocation, or a queue publish |
| Distributed transactions | Not used. Ever. Across databases, use an outbox |
| Isolation level | SQL Server default (`READ COMMITTED`); raising it requires a recorded reason |

An aggregate is the transaction boundary. If a use case must atomically change two aggregates, that
is a signal the aggregate boundary is drawn in the wrong place — fix the model before reaching for a
wider transaction.

### 10.3 Stored procedure policy

**Stored procedures are not used.** The model is code-first; a stored procedure is schema that EF
Core does not know about, cannot generate, cannot diff and cannot roll back. It also silently
becomes a second place where business rules live.

The narrow exception: a set-based data operation whose row count makes a round trip per row
untenable — a bulk archive, a large backfill. Such an operation is written as raw SQL inside a
migration, not as a persisted stored procedure, and it is reviewed as carefully as domain code.

There are no stored procedures in the database today.

### 10.4 View policy

**Views are not used for application reads.** A view is a query with a schema-shaped disguise; it
hides its cost and it is not versioned with the code that depends on it.

The narrow exception: a read model whose query is genuinely expensive and genuinely stable — a
reporting projection. Such a view is created in a migration, named `vw_<Name>`, mapped with
`ToView` and `HasNoKey`, and mapped only to a dedicated read type that no writer touches.

There are no views in the database today.

### 10.5 Raw SQL

Raw SQL is permitted through `FromSqlInterpolated` or `ExecuteSqlInterpolated` only. String
concatenation into SQL is prohibited without exception. Any raw SQL in a repository carries a
comment stating why LINQ could not express it.

---

## 11. Column type standards

### 11.1 Date and time

| Rule | Statement |
|---|---|
| Storage type | `datetime2(3)` — millisecond precision, no more |
| Time zone | **UTC always.** No local time is ever stored |
| CLR type | `DateTimeOffset` for instants, `DateOnly` for dates without time |
| Naming | Instant columns end in `Utc`: `CreatedAtUtc`, `DeletedAtUtc`, `StartedAtUtc` |
| `datetime` | Never used — it is a legacy type with 3.33 ms rounding |
| Defaults | No `GETDATE()` defaults; the application sets timestamps so tests can control them |
| Display | Conversion to local time happens in the frontend, never in SQL |

The `Utc` suffix is not decoration. It is the only thing that makes a wrong value obvious in a query
result.

### 11.2 Money

**TARGET — no monetary column exists in Nexus today.** The standard is set now so that the first one
is right:

| Rule | Statement |
|---|---|
| Storage type | `decimal(19,4)` |
| CLR type | `decimal` — never `double`, never `float` |
| Currency | Every money column is accompanied by an ISO 4217 currency column, `char(3)` |
| Naming | `<Thing>Amount` and `<Thing>Currency` |
| Rounding | Rounding rules are the domain's, applied before persistence, never left to SQL |

A money value without an adjacent currency is a defect, not a shortcut. The first place this will
be exercised is per-turn cost attribution (**M-04-4.1 Per-turn cost attribution**) and product
plans (**M-06-3.1 Plans and subscriptions**); whichever lands first sets the precedent, and it must
be this one.

### 11.3 Decimals generally

Every non-money `decimal` declares explicit precision and scale in its configuration. EF Core warns
about undeclared decimal precision and the warning is treated as an error. Choose the smallest
precision that the domain actually needs — precision is a claim about measurement accuracy, and an
over-wide column is a claim you cannot support.

Floating point (`float`, `real`) is permitted only for values that are genuinely approximate:
embedding vectors, model scores, statistical measures. Never for a quantity anyone will add up.

### 11.4 Units

**A quantity column names its unit or is wrong.** `DurationMs`, `SizeBytes`, `LatencyMs`,
`TokenCount`, `WeightGrams`. Never `Duration`, `Size` or `Length` alone.

The unit is chosen once per dimension and used everywhere: milliseconds for durations, bytes for
sizes, UTC for instants. Where a domain genuinely carries variable units — a machine or ERP context
where the same column may be metres or millimetres — the unit is a separate column and the pair is
never split.

This becomes load-bearing at Layer 12 for any product measuring physical quantities. It costs
nothing to get right now and is expensive to retrofit.

### 11.5 JSON columns

JSON is permitted, narrowly:

| Permitted | Prohibited |
|---|---|
| A payload the database will never query into | Anything filtered, sorted or joined on |
| A captured external response stored for provenance | A substitute for modelling a relationship |
| A sparse, genuinely open bag of settings | A place to avoid writing a migration |
| A serialised value object owned by exactly one aggregate | Anything referenced by another entity |

Stored as `nvarchar(max)`. If a JSON column starts being queried into, that is the signal to promote
its fields to real columns — the promotion is a migration and a backfill, and it is always the right
call.

An owned entity configured with `OwnsOne`/`OwnsMany` into real columns is preferred over JSON
whenever the shape is known. JSON is for shapes that are genuinely not known.

---

## 12. What a new persisted entity requires

A checklist, in order. An entity that skips a line is incomplete, not "mostly done".

1. Domain class in its own aggregate folder, with `<Name>Id.cs`, `<Name>Status.cs`,
   `I<Name>Repository.cs`, a private constructor and a `public static <Name> Restore(...)`.
2. `<Name>Configuration.cs` in `Infrastructure/Sql/Configurations/`.
3. Explicit schema — the layer's schema, never `org` for anything new.
4. `Id` primary key, non-clustered.
5. `Seq` shadow property, `IDENTITY(1,1)`, clustered index.
6. `Ref` computed `PERSISTED` column with a registered prefix, unique index.
7. Four audit columns.
8. `rowversion` concurrency token if the entity is mutable.
9. Every relationship with an explicit `DeleteBehavior` per §5.3.
10. Indexes on every foreign key and every filter or sort column.
11. Explicit string lengths and decimal precision.
12. Converters registered in `StronglyTypedIdConverters.cs` — and nowhere else.
13. One migration, correctly named, reversible.
14. A repository implementation, `Sql<Name>Repository`, returning materialised results.
15. Tenant filter where the entity is tenant-owned (`SECURITY_STANDARDS.md`).
16. An acceptance criterion and a verification method (`ASSURANCE_STANDARDS.md`).

---

## 13. Open decisions

| Question | Decided by | Status |
|---|---|---|
| Which entities are soft-deleted, and retention per class | M-02-5.1 Classification and retention | Not yet decided |
| Whether OPERATIONS time-series leaves `NexusPlatform` | Layer 10 sizing, after M-10-2.2 | Not yet decided |
| Vector storage mechanism for embeddings | M-02-4.2 Embeddings and vector retrieval | Not yet decided |
| Full-text search mechanism | M-02-4.1 Full-text and structured search | Not yet decided |
| Outbox implementation for cross-database consistency | M-01-8.1 In-process event bus, then beyond | Not yet decided |
| Read replica or reporting store | Not raised | Not yet decided |

Each of these is genuinely open. None of them is blocked on this document.

---

## 14. References

- `API_STANDARDS.md` — how persisted data is exposed, paginated, filtered and versioned.
- `SECURITY_STANDARDS.md` — tenant isolation and the query filter, encryption, PII, audit content.
- `CONFIGURATION_STANDARDS.md` — connection strings, per-environment database targets.
- `DEVELOPMENT_WORKFLOW.md` — when a migration may be written, and the parallel-safety consequence.
- `GIT_WORKFLOW.md` — migration conflict resolution, model snapshot conflicts.
- `ASSURANCE_STANDARDS.md` — how the schema-ownership and boundary rules are proven.
- ADR-014 Azure SQL migration — the originating decision record. The next ADR number is ADR-016.
