# ADR-014 and Migration Plan — Dataverse → Azure SQL

**Status:** Accepted
**Date:** 2026-08-18
**Supersedes:** ADR-002 (Dataverse is the operational source of truth)
**Applies to:** `Nexus.Web` — the Chat product only. Platform and Intelligence are unaffected.

> Merge §1 into `08_DECISIONS_AND_TECHNICAL_DEBT.md` during the Stage 10 docs pass.
> Do **not** delete ADR-002 — the maintenance rule in that document says supersede, never erase.

---

# 1. ADR-014 — Azure SQL replaces Dataverse for the Chat product

## Context

ADR-002 chose Microsoft Dataverse as the operational store, reasoning: structured
relationships, built-in security, Power Platform integration, and enterprise administration.
Those reasons were sound for an internal Power Platform tool. They do not hold for a
multi-user chat product.

The V2.1 restructure (2026-08-17/18) confined all Dataverse code to
`Nexus.Products.Chat.Infrastructure`. The Domain does not know Dataverse exists, the
Application depends only on repository interfaces declared in the Domain, and Intelligence
and Platform are structurally forbidden from referencing it. That makes the store
replaceable at a cost that will never be lower than it is today.

## Decision

The Chat product persists to **Azure SQL Database**. Dataverse is removed entirely from
`Nexus.Web`, including the `Microsoft.PowerPlatform.Dataverse.Client` package.

Access is via **EF Core**, with the Domain kept persistence-ignorant: no EF attributes, no
navigation-property pollution, no base classes. Mapping lives in
`IEntityTypeConfiguration<T>` classes inside Infrastructure, and strongly typed IDs are
handled by value converters.

## Drivers

All four applied, which is why this is not a marginal call:

| Driver | Detail |
|---|---|
| **Cost and licensing** | Dataverse is per-user licensed. A chat product with many light users is close to the worst possible shape for that model. Azure SQL is priced on compute and storage, not seats. |
| **Query power and retrieval** | Knowledge and Memory need full-text and vector retrieval. Dataverse offers neither usefully. Azure SQL can hold operational data and vectors in one store. |
| **Latency and throughput** | Every chat turn reads history, knowledge, ADRs and work items before the model is invoked. That is several throttled Web API round-trips on the hot path. |
| **Independence from Power Platform** | Products must be free to choose their own store (V2.1 §1.3). Vault and ERP may need shapes Dataverse cannot serve. Staying would make Power Platform licensing a dependency of every future product. |

## Consequences

**Positive**

- Removes per-seat licensing from the product's cost model
- Retrieval becomes a first-class capability rather than a workaround
- Removes several network hops from every chat turn
- Removes the `NU1903` high-severity vulnerability inherited transitively via the
  Dataverse client
- EF Core migrations give schema versioning that the Dataverse solution export never did
- Proves the V2.1 boundary claim in practice — see §2, Stage 1

**Negative**

- Loses Dataverse's built-in row-level security. Authorization becomes the product's job,
  and lands on the same critical path as identity (D-1, still unimplemented).
- Loses Power Platform interop: no model-driven apps, flows or connectors over this data
  without building an API surface for them
- Loses the Dataverse audit trail; auditing becomes application-level
- Adds EF Core migrations and an Azure SQL instance to operate and pay for

**Neutral**

- No data migration. The current Dataverse contents are smoke-test records only
  (`PRJ-00000007`, `CON-00000003/4/5`) and are **confirmed disposable**. This is a schema
  rewrite, not a data migration — substantially cheaper.

## What does not change

- The Domain model. Same 11 aggregates, same strongly typed IDs, same invariants.
- Repository interfaces (`IWorkspaceRepository` and siblings) stay in the Domain, unchanged.
- The Application layer. Not one handler should need editing.
- Every API contract and response shape.
- Platform and Intelligence. Neither has ever known what a Conversation is.

**If any of the above needs to change, that is an architecture leak, not a migration task.
Stop and report it.**

## Open question to settle before Stage 5

Azure SQL's vector capabilities have moved quickly. Confirm current support, dimension
limits, index types and pricing against live Microsoft documentation before designing
Knowledge and Memory retrieval. Do not design from memory — including mine.

## Fate of the Dataverse solution

`N_001_Nexus` in the `PRT (Dev)` environment is not deleted by this ADR. It stops being
read or written by `Nexus.Web`. Retire it as a separate, deliberate act once nothing
depends on it.

---

# 2. Migration plan

Same discipline as the V2 runbook: each stage ends with a green build and its own commit,
prompts stay small, `/clear` between stages.

**Repo:** `C:\Personal\Nexus.Web` — branch `feat/azure-sql` off `arch/v2`.

**Prerequisite:** Stages 9 and 10 of the V2 migration are complete and all three repos are
pushed. Do not run two migrations at once.

## Stage 0 — Provision and prepare

```powershell
cd C:\Personal\Nexus.Web
git checkout arch/v2
git pull
git checkout -b feat/azure-sql
```

Local development database — a container gives production parity and costs nothing:

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<strong password>" `
  -p 1433:1433 --name nexus-sql -d mcr.microsoft.com/mssql/server:2022-latest
```

Provision an **Azure SQL serverless** database for dev. Auto-pause keeps the idle cost near
zero. Do not use a production tier yet.

Connection string goes in user secrets, never `appsettings.json`:

```powershell
cd src\Nexus.Products.Chat.Api
dotnet user-secrets set "ConnectionStrings:NexusChat" "<connection string>"
```

## Stage 1 — The leak test: one aggregate only

**This is the most important stage in the plan.** It is deliberately tiny, and its purpose
is to falsify the architecture claim before you commit to the whole port.

```
Add an EF Core / Azure SQL persistence path to Nexus.Products.Chat.Infrastructure,
implementing ONE aggregate only: Workspace. Leave every Dataverse class in place and
working - both implementations will coexist until the port is complete.

Read C:\Personal\NexusAI\NEXUS_ARCHITECTURE_V2.md section 1.3 and
C:\Personal\NexusAI\ADR-014_AZURE_SQL_MIGRATION.md first.

HARD CONSTRAINT: do not modify anything in Nexus.Products.Chat.Domain or
Nexus.Products.Chat.Application. Not one file. If you believe a change is required there,
STOP and report exactly what and why - that is an architecture leak and I need to know
about it rather than have it papered over.

1. Add to Nexus.Products.Chat.Infrastructure:
     Microsoft.EntityFrameworkCore.SqlServer
     Microsoft.EntityFrameworkCore.Design
   Pin versions matching the .NET 10 SDK in global.json.

2. Create Sql/NexusChatDbContext.cs. It applies configurations from the assembly; it does
   not declare mappings inline.

3. Create Sql/Conventions/StronglyTypedIdConverters.cs. The Domain uses wrapper ID types
   (WorkspaceId, ProjectId, ...) around Guid. Provide EF ValueConverters for them. Do NOT
   add EF attributes or [Key] to the Domain types - configuration lives here.

4. Create Sql/Configurations/WorkspaceConfiguration.cs implementing
   IEntityTypeConfiguration<Workspace>. Map to a table named Workspace - no du_ prefix,
   no T_001_ numbering. That was Dataverse naming and it is not coming with us.
   The aggregate has private setters and behaviour methods; bind through the private
   parameterless constructor or field access. Do not relax the Domain's encapsulation.

5. Create Sql/Repositories/SqlWorkspaceRepository.cs implementing IWorkspaceRepository -
   the SAME interface the Dataverse repository implements. Same semantics, same nullability.

6. Add the first migration:
     dotnet ef migrations add InitialWorkspace -p src\Nexus.Products.Chat.Infrastructure -s src\Nexus.Products.Chat.Api
     dotnet ef database update -p src\Nexus.Products.Chat.Infrastructure -s src\Nexus.Products.Chat.Api

7. In the Infrastructure DI extension, register the SQL implementation for
   IWorkspaceRepository ONLY when configuration key "Nexus:Persistence" equals "Sql".
   Default remains Dataverse. Every other repository stays on Dataverse regardless.
   Add ConnectionStrings:NexusChat, and EnableRetryOnFailure - transient faults are normal
   on Azure SQL and an unhandled one looks like a bug.

ACCEPTANCE - report each:
  1. dotnet build Nexus.Web.slnx succeeds
  2. dotnet test passes, unchanged
  3. git diff --stat shows changes ONLY under src\Nexus.Products.Chat.Infrastructure
     and its csproj. Any file touched in Domain or Application is a FAILURE - report it,
     do not fix it.
  4. with Nexus:Persistence=Sql, POST /api/v1/workspaces then GET /api/v1/workspaces/{id}
     round-trips through Azure SQL
  5. with the setting absent, Dataverse still works
```

**If check 3 fails, stop the migration and tell me.** A leak found here is cheap. The same
leak found at aggregate ten is not.

## Stage 2 — Port the remaining aggregates

Three sub-stages. Do not attempt all ten at once — the V2 migration stalled three times on
oversized prompts, always the largest one.

| Sub-stage | Aggregates | Note |
|---|---|---|
| **2a** | Project, Conversation, ConversationMessage | The chat hot path. Highest value, exercised by every turn. |
| **2b** | Knowledge, Adr, WorkItem, Artifact | Everything `ChatContextBundleMapper` reads |
| **2c** | Branch, Snapshot, Session | Lower traffic |

Each sub-stage: configurations, repositories, one EF migration, DI registrations, then build
and test. Same hard constraint — Domain and Application untouched.

Index the hot query paths in 2a and 2b. These are the reads on every chat turn:

| Table | Index |
|---|---|
| `ConversationMessage` | `(ConversationId, CreatedOn)` — history load, the hottest read |
| `Conversation` | `(ProjectId)` |
| `Project` | `(WorkspaceId)` |
| `Knowledge` | `(WorkspaceId, Status)` |
| `WorkItem` | `(ProjectId, Status)` |
| `Artifact` | `(WorkItemId)` |
| `Branch` | `(ConversationId)` |
| `Snapshot` | `(BranchId)` |
| `Session` | `(ConversationId)` |

## Stage 3 — Delete Dataverse

Only once every repository is on SQL and the smoke test passes end to end.

```
Remove Dataverse from Nexus.Web entirely.

  - delete src/Nexus.Products.Chat.Infrastructure/Dataverse/** (client, context, entities,
    mappers, repositories, options)
  - remove the Microsoft.PowerPlatform.Dataverse.Client PackageReference
  - remove the System.Security.Cryptography.Xml pin added in V2 Stage 6 - it existed only
    to patch a transitive dependency of the Dataverse client, and should disappear with it.
    Confirm NU1903 no longer appears.
  - remove the Dataverse section from appsettings.json and the Dataverse:ClientSecret user
    secret
  - remove the Nexus:Persistence switch and its Dataverse branch - SQL is now the only path
  - in tests/Nexus.Products.Chat.Architecture.Tests, rename Domain_MustNotReference_Dataverse
    to Domain_MustNotReference_Persistence and assert the Domain depends on no
    EntityFrameworkCore type either. The rule was never about Dataverse specifically - it
    was about the Domain not knowing how it is stored. Do the same for the Application rule.

ACCEPTANCE: solution builds with zero NU1903; no file under src contains "Dataverse";
all tests pass; a full chat turn still works end to end.
```

## Stage 4 — Azure hardening

- `EnableRetryOnFailure` verified on the production connection
- Connection string from Azure Key Vault or App Service configuration, not user secrets
- EF migration bundle produced in CI (`dotnet ef migrations bundle`) — never run
  `database update` against production from a developer machine
- Command timeout set deliberately
- Query performance checked against the indexes above with realistic message volume
- Backup and point-in-time-restore confirmed on the Azure SQL instance

## Stage 5 — Out of scope here

Vector retrieval for Knowledge and Memory is a separate piece of work, after Stages 1–4 are
green and after the chat UI exists to measure whether retrieval quality actually improves.
Settle the open question in §1 first.

---

# 3. Verification

| # | Check |
|---|---|
| 1 | `dotnet build Nexus.Web.slnx` clean, zero `NU1903` |
| 2 | `dotnet test` all pass |
| 3 | Architecture tests fail when deliberately broken |
| 4 | **No file in Domain or Application was modified across the whole migration** — `git diff --stat arch/v2..feat/azure-sql` |
| 5 | No `Dataverse` reference remains anywhere in `Nexus.Web` |
| 6 | Full journey: workspace → project → conversation → chat → reply with citations |
| 7 | Reload the conversation; both messages persisted in Azure SQL |
| 8 | Chat turn latency measurably better than the Dataverse baseline — measure before Stage 3 so you have the comparison |

Item 4 is the one that matters architecturally. It is the empirical proof that V2.1 works.

# 4. Rollback

Every stage is a commit; `git reset --hard HEAD~1` undoes one. Until Stage 3, both
implementations coexist and `Nexus:Persistence` switches between them at runtime — so a
failed SQL path is a config change away from being reverted, with no redeploy.

After Stage 3 the rollback is `git revert` plus reinstating the Dataverse credentials.
Do not start Stage 3 until Stage 2 has run against real usage for a while.
