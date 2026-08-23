# Technology Stack

> **Status:** CURRENT — describes what is on disk and building on 2026-08-21
> **Owner:** Layer 03 GOVERNANCE (registry of record) / Layer 08 DELIVERY (enforcement in build)
> **Last updated:** 2026-08-21
> **Layer:** Cross-cutting (03 GOVERNANCE owns the record, 08 DELIVERY owns the pipeline)
> **Authoritative for:** which technologies are approved for Nexus, what each is for, where each is used, and which technologies are explicitly not yet chosen

---

## 1. How to read this document

Every entry is one of four states.

| Marker | Meaning |
|---|---|
| **CURRENT** | On disk, referenced by a project that compiles today. Safe to use now. |
| **TARGET** | Decided direction, not yet true in the repositories. Named milestone closes the gap. |
| **TRANSITION** | Both the old and the new exist simultaneously. The document says which to write new code against. |
| **NOT SELECTED** | No decision has been made. Do not introduce it. Section 7 lists these and what would decide them. |

Version pinning mechanics, upgrade cadence and deprecation live in **STACK_VERSION_POLICY.md** and are not repeated here. Naming rules for projects and packages live in **NAMING_STANDARDS.md**. Database engine rules live in **DATABASE_STANDARDS.md**.

**Ownership caveat.** No named human owner is recorded for any technology anywhere in the repositories. Ownership below is stated by layer. Named ownership becomes real when the GOVERNANCE Technology Catalogue lands — **M-03-2.1 Technology catalogue**.

**Version caveat.** Only `net10.0` is a verified pinned version. Everything else is recorded as *unpinned — see STACK_VERSION_POLICY.md* because the exact package versions were not verified from `.csproj` or lock files. Writing a guessed version here would be worse than writing none.

---

## 2. Stack at a glance

| Technology | Purpose | Version | State | Owner layer |
|---|---|---|---|---|
| .NET / `net10.0` | Runtime and SDK for all backend code | .NET 10 (`net10.0`) | CURRENT | 08 DELIVERY |
| C# | Backend language | Ships with .NET 10 | CURRENT | 08 DELIVERY |
| ASP.NET Core Minimal APIs | HTTP surface for all three APIs | Ships with .NET 10 | CURRENT | 11 EXPERIENCE / 04 AI |
| EF Core | ORM, code-first schema authority | 9.x-era assemblies — unpinned | CURRENT | 02 DATA |
| Microsoft.Data.SqlClient | SQL Server / Azure SQL driver | unpinned | CURRENT | 02 DATA |
| Azure SQL / SQL Server LocalDB | Relational store (prod / dev) | unpinned | CURRENT | 02 DATA |
| Swashbuckle.AspNetCore | OpenAPI document and Swagger UI | unpinned | CURRENT | 11 EXPERIENCE |
| OpenAI SDK (`OpenAI.dll`) | Model invocation, OpenAI provider | unpinned | CURRENT | 04 AI |
| System.ClientModel | Client primitives under the OpenAI SDK | unpinned | CURRENT | 04 AI |
| React | Frontend UI library | unpinned | CURRENT | 11 EXPERIENCE |
| TypeScript | Frontend language | unpinned | CURRENT | 11 EXPERIENCE |
| Vite | Frontend build and dev server | unpinned | CURRENT | 11 EXPERIENCE |
| TanStack Query | Server-state cache for the frontend | unpinned | CURRENT | 11 EXPERIENCE |
| NetArchTest | Architecture boundary tests | unpinned | CURRENT | 09 ASSURANCE |
| Git + GitHub | Source control and remote hosting | n/a | CURRENT | 08 DELIVERY |
| NuGet | .NET package management | n/a | CURRENT | 08 DELIVERY |
| PowerShell (`.ps1`) | Local developer scripting | Windows PowerShell / pwsh — unpinned | CURRENT | 08 DELIVERY |
| Azure.Identity / Azure.Core | Azure credential and client primitives | unpinned | **UNCONFIRMED** — see §6 | 08 DELIVERY |
| Dataverse client stack | Legacy persistence | unpinned | **BEING REMOVED** — see §6 | 02 DATA |

---

## 3. Runtime, language and web

### .NET 10

| Field | Value |
|---|---|
| **Purpose** | Single runtime and SDK for every backend project in Nexus. |
| **Approved version** | `net10.0`. This is the only verified pinned version in the stack. |
| **Where used** | Every project in NexusAI, Nexus.Int and Nexus.Web: `Nexus.Platform.Contracts`, `Nexus.Platform.Core`, `Nexus.Intelligence.Core`, `Nexus.Products.Chat.Domain`, and the rest. |
| **Why selected** | Not recorded in any ADR. The observable rationale: one runtime across three repositories, first-class Minimal APIs, and EF Core support. |
| **Alternatives rejected** | Not recorded. Do not claim any. |
| **Upgrade policy** | SDK band pinned via `global.json` per repository. See STACK_VERSION_POLICY.md §2. |
| **Compatibility requirements** | Every project targets exactly `net10.0`. No multi-targeting anywhere. A project that needs a second TFM is an architecture decision requiring an ADR (next number: ADR-016). |
| **Owner** | 08 DELIVERY |

### C#

| Field | Value |
|---|---|
| **Purpose** | Language for all backend code. |
| **Approved version** | The default language version for `net10.0`. Not explicitly overridden anywhere that was verified. |
| **Where used** | All backend projects. |
| **Why selected** | Consequence of choosing .NET. |
| **Alternatives rejected** | None recorded. |
| **Upgrade policy** | Moves with the SDK band. See STACK_VERSION_POLICY.md. |
| **Compatibility requirements** | Language rules — records vs classes, nullability, async, LINQ — are in **CSHARP_STANDARDS.md**. Nullable reference types are expected to be enabled repository-wide via `Directory.Build.props`; verify before relying on it. |
| **Owner** | 08 DELIVERY |

### ASP.NET Core Minimal APIs

| Field | Value |
|---|---|
| **Purpose** | The HTTP surface of every API host. |
| **Approved version** | Ships in the .NET 10 shared framework. |
| **Where used** | `Nexus.Products.Chat.Api` (`Program.cs`, `ChatProductModule.cs`, `Endpoints/`), `Nexus.Intelligence.Api` (`Program.cs`, `Endpoints/`). |
| **Why selected** | Endpoint-per-file organisation matches the `<Name>Endpoint.cs` + `Map<Name>Endpoints(this IEndpointRouteBuilder app)` convention already in use across both APIs. |
| **Alternatives rejected** | MVC controllers are not used anywhere. No ADR records the decision, but no controller exists in any repository — treat Minimal APIs as the only approved style. |
| **Upgrade policy** | Moves with the runtime. |
| **Compatibility requirements** | Route grammar (`/api/v1/workspaces`, `/intelligence/v1`) is fixed in NAMING_STANDARDS.md. Handler shape is fixed in CSHARP_STANDARDS.md. |
| **Owner** | 11 EXPERIENCE for the Chat API surface, 04 AI for the Intelligence API surface |

### Swashbuckle.AspNetCore

| Field | Value |
|---|---|
| **Purpose** | Generates the OpenAPI document and serves Swagger UI. |
| **Approved version** | unpinned — see STACK_VERSION_POLICY.md |
| **Where used** | Present as `Swashbuckle.AspNetCore`, `.Swagger`, `.SwaggerGen`, `.SwaggerUI` assemblies in the API hosts. |
| **Why selected** | Not recorded. |
| **Alternatives rejected** | Not recorded. `Microsoft.AspNetCore.OpenApi` is *not* in use — do not mix the two. |
| **Upgrade policy** | Minor and patch freely; major only with a check that the generated document still matches the published route grammar. |
| **Compatibility requirements** | Must track the ASP.NET Core major version. |
| **Owner** | 11 EXPERIENCE |

---

## 4. Data

### EF Core

| Field | Value |
|---|---|
| **Purpose** | Object-relational mapping and, critically, the **authority on schema shape**. Nexus is code-first: domain class → `IEntityTypeConfiguration` → migration → DDL. |
| **Approved version** | 9.x-era assemblies observed: `Microsoft.EntityFrameworkCore.dll`, `.Relational.dll`, `.SqlServer.dll`, `.Abstractions.dll`. Exact package version unpinned — see STACK_VERSION_POLICY.md. |
| **Where used** | `Nexus.Products.Chat.Infrastructure/Sql/`: `NexusChatDbContext`, `NexusChatDbContextFactory`, `Configurations/WorkspaceConfiguration.cs`, `Conventions/StronglyTypedIdConverters.cs`, `Migrations/20260820180802_InitialSqlSchema.cs`. |
| **Why selected** | ADR-014. Code-first migration is the mechanism that made the Id/Seq/Ref pattern reproducible and the Dataverse exit possible. |
| **Alternatives rejected** | **Dataverse** (see §6) — rejected in ADR-014. Dapper and raw ADO.NET are not used; no ADR records considering them. |
| **Upgrade policy** | Never upgrade the EF Core major version in the same work item as a migration. A model-snapshot change plus a provider change makes a failure undiagnosable. |
| **Compatibility requirements** | EF Core major must be compatible with the .NET 10 runtime and with `Microsoft.Data.SqlClient`. All rules on entity configuration, cascade behaviour and the Id/Seq/Ref pattern live in **DATABASE_STANDARDS.md** — not repeated here or in CSHARP_STANDARDS.md. |
| **Owner** | 02 DATA |

### Microsoft.Data.SqlClient

| Field | Value |
|---|---|
| **Purpose** | The SQL Server / Azure SQL wire protocol driver beneath EF Core. |
| **Approved version** | unpinned — see STACK_VERSION_POLICY.md |
| **Where used** | Transitively under `Microsoft.EntityFrameworkCore.SqlServer` in `Nexus.Products.Chat.Infrastructure`. |
| **Why selected** | Required by the SQL Server EF provider. Not an independent choice. |
| **Alternatives rejected** | `System.Data.SqlClient` is legacy and must not be added. |
| **Upgrade policy** | Security patches applied on release. Major versions have historically changed TLS and certificate-trust defaults — a major upgrade requires a verified local LocalDB connection **and** a verified Azure SQL connection before merge. |
| **Compatibility requirements** | Must satisfy the EF Core SQL Server provider's minimum. Do not pin it independently unless resolving a security advisory. |
| **Owner** | 02 DATA |

### Azure SQL and SQL Server LocalDB

| Field | Value |
|---|---|
| **Purpose** | The relational store. Azure SQL is the deployment target; LocalDB is the development instance. |
| **Approved version** | unpinned — see STACK_VERSION_POLICY.md |
| **Where used** | Development runs against LocalDB. The proven insert evidence (`api_run.log`, 2026-08-20 18:09 UTC) came from this path: `INSERT INTO [org].[Workspace] (...) OUTPUT INSERTED.[Ref], INSERTED.[Seq] VALUES (...)`, twice, each returning server-generated values. |
| **Why selected** | ADR-014. The decisive property is that only the database guarantees `Ref` uniqueness under concurrent insert, which C#-side generation cannot. |
| **Alternatives rejected** | **Dataverse** — ADR-014 Stage 3 removes it. PostgreSQL, SQLite and any document store: not recorded as considered. |
| **Upgrade policy** | Azure SQL compatibility level is a schema-affecting change and needs a migration-equivalent record. |
| **Compatibility requirements** | The computed-column expression `('WKS-' + RIGHT('00000000' + CAST([Seq] AS varchar(8)), 8))` is T-SQL and PERSISTED. It ties Nexus to SQL Server dialect. Any engine change invalidates the Id/Seq/Ref pattern wholesale. Physical strategy — one `NexusPlatform` database with a schema per layer, plus one database per product — is **TARGET**; the shipped migration used schema `org`. **M-02-1.5 Layer schema convention** closes that gap. |
| **Owner** | 02 DATA |

---

## 5. AI providers, frontend, build and test

### OpenAI SDK and System.ClientModel

| Field | Value |
|---|---|
| **Purpose** | Invoke OpenAI models. `System.ClientModel` supplies the client, pipeline and result primitives the SDK is built on. |
| **Approved version** | unpinned — see STACK_VERSION_POLICY.md |
| **Where used** | `Nexus.Platform.Providers.OpenAI`, behind `IModelGateway` / `INamedModelGateway`, reached through `RoutingModelGateway` and `AggregatingModelCatalog` in `Nexus.Platform.Core/Models/`. |
| **Why selected** | Not recorded. It is the first provider wired end to end; **M-01-6.1 OpenAI path verified end to end** is the milestone that proves it. |
| **Alternatives rejected** | None rejected — this is a multi-provider design. `Nexus.Platform.Providers.Anthropic/AnthropicModelGateway.cs` exists as a **306-byte stub** and must not be described as working. **M-01-6.2 Multi-provider routing** makes routing real. |
| **Upgrade policy** | Provider SDKs move fast. Upgrade only behind the `IModelGateway` boundary; no provider type may leak into `Nexus.Platform.Contracts`. |
| **Compatibility requirements** | Provider SDK types must never appear in `Nexus.Platform.Contracts` or `Nexus.Intelligence.Contracts`. The no-shared-kernel invariant depends on it, and `PlatformBoundaryTests.cs` is where that is enforced. API keys today come from `set-openai-key.ps1`; **TARGET** is `ISecretResolver` — **M-01-5.1 Real secret resolver**. |
| **Owner** | 04 AI |

### React, TypeScript and Vite

| Field | Value |
|---|---|
| **Purpose** | The web client. React renders, TypeScript types, Vite builds and serves. |
| **Approved version** | unpinned — see STACK_VERSION_POLICY.md |
| **Where used** | `Nexus.Web.Client/src/` in its entirety: `App.tsx`, `main.tsx`, `layouts/AppLayout.tsx`, `routes/AppRoutes.tsx`, the `features/chat`, `features/projects`, `features/workspaces` and `features/system` folders, and eleven pages from `ChatPage` to `WorkspacesPage`. |
| **Why selected** | Not recorded. Vite is evidenced by `vite.svg` and the `VITE_` environment-variable prefix in `config/environment.ts`. |
| **Alternatives rejected** | Not recorded. Next.js, Angular and Vue are not present. |
| **Upgrade policy** | React major upgrades are a whole-client change and get their own work item. Vite majors are routine. |
| **Compatibility requirements** | Only variables prefixed `VITE_` reach the client bundle — see NAMING_STANDARDS.md §15 for environment variables. All folder, component, hook and API-client rules are in **TYPESCRIPT_REACT_STANDARDS.md**. |
| **Owner** | 11 EXPERIENCE |

### TanStack Query

| Field | Value |
|---|---|
| **Purpose** | Server-state cache and request lifecycle for the frontend. It is the only sanctioned way to hold data that came from the API. |
| **Approved version** | unpinned — see STACK_VERSION_POLICY.md |
| **Where used** | `app/queryClient.ts` builds the client, `app/AppProviders.tsx` installs it. Every `use*` hook consumes it: `useConversations.ts`, `useConversationMessages.ts`, `useSendChat.ts`, `useProjects.ts`, `useWorkspaces.ts`, `useCreateWorkspace.ts`, `useSystemHealth.ts`. |
| **Why selected** | Not recorded. The observable effect: no bespoke fetch-and-store code exists in the client. |
| **Alternatives rejected** | Redux, Zustand, MobX and SWR are not present. No ADR records rejecting them. |
| **Upgrade policy** | Majors change the hook option shape; upgrade in one work item touching every `use*` hook. |
| **Compatibility requirements** | Query-key grammar and the cache-invalidation rules are in TYPESCRIPT_REACT_STANDARDS.md. |
| **Owner** | 11 EXPERIENCE |

### NetArchTest

| Field | Value |
|---|---|
| **Purpose** | Executable enforcement of the dependency-direction and no-shared-kernel invariants. |
| **Approved version** | unpinned — see STACK_VERSION_POLICY.md |
| **Where used** | `Nexus.Platform.Architecture.Tests/PlatformBoundaryTests.cs`, `Nexus.Intelligence.Architecture.Tests/BoundaryRuleTests.cs`, `Nexus.Products.Chat.Architecture.Tests/BoundaryTests.cs`. |
| **Why selected** | Not recorded. These three files are the only automated defence the architecture currently has. |
| **Alternatives rejected** | Not recorded. |
| **Upgrade policy** | Low churn. Upgrade when it blocks a runtime upgrade. |
| **Compatibility requirements** | Must load `net10.0` assemblies. These tests run in CI once **M-08-1.2 Pipelines on every repository** exists; **M-08-1.4 Branch protection and architecture gate** makes them blocking. |
| **Owner** | 09 ASSURANCE |

> **Unit test framework — deliberately not stated.** The test runner and assertion library are whatever the six `.Tests` `.csproj` files declare, and those declarations were not verified. Do not name a framework in code review or documentation until it is read from the project files. Note the shape of what exists: **exactly two behaviour tests in the entire system** — `Ranking/KeywordContextRankerTests.cs` and `Chat/ChatContextBundleMapperTests.cs`. `Nexus.Platform.Tests` is a `.csproj` containing zero `.cs` files.

### Git, GitHub, NuGet and PowerShell

| Field | Value |
|---|---|
| **Purpose** | Source control (`git`), remote hosting and future CI (GitHub), .NET package distribution (NuGet), developer scripting (PowerShell). |
| **Approved version** | n/a — tools, not dependencies. |
| **Where used** | Remotes `github.com/prtcare/NexusAI`, `github.com/prtcare/Nexus-Int`, `github.com/prtcare/Nexus-web`. Package flow: `pack-local.ps1` in NexusAI and Nexus.Int → `C:\Personal\LocalNuGet` → consumed via `nuget.config`. Scripts: `pack-local.ps1`, `set-openai-key.ps1`, `nexus-v2-restructure.ps1`, `run-migration.ps1`. |
| **Why selected** | Not recorded. |
| **Alternatives rejected** | Not recorded. |
| **Upgrade policy** | Tooling upgrades are unmanaged and do not require an ADR. |
| **Compatibility requirements** | **`C:\Personal\LocalNuGet` is not a git repository and is unreachable from any build agent.** No CI can restore against it. **TARGET: GitHub Packages — M-08-1.1 Package feed reachable from CI.** Until then, packaging is a local-only workflow. Two further constraints are real and permanent: a git worktree nested inside an agent's working directory cannot be renamed on Windows while that agent runs, so **worktrees go in a sibling directory**; and all three repositories lost `.git\objects` simultaneously on 2026-08-20, with the recommended antivirus exclusion for `C:\Personal` **never confirmed** — **M-08-2.1 Close the 2026-08-20 recovery**. |
| **Owner** | 08 DELIVERY |

> **CI is NOT part of the current stack.** `NexusAI\.github\workflows\` exists and is empty. `Nexus.Web` and `Nexus.Int` have no `.github` directory at all. There is no pipeline, no IaC and no environment definition anywhere. Anything in this document that says "in CI" is TARGET, gated on **M-08-1.2**.

---

## 6. Being removed

**TRANSITION.** These assemblies are still on disk and still referenced. New code must not touch them.

| Technology | Purpose it served | Where it still is | Removed by |
|---|---|---|---|
| `Microsoft.PowerPlatform.Dataverse.Client` | Dataverse connection and CRUD | `Nexus.Products.Chat.Infrastructure` — Dataverse implementations for 10 of the 11 aggregates | **M-02-1.4 Delete Dataverse** |
| `Microsoft.Xrm.Sdk` | Dataverse entity and query model | same | **M-02-1.4** |
| `Microsoft.Crm.Sdk.Proxy` | Dataverse message proxies | same | **M-02-1.4** |
| `System.Security.Cryptography.Xml` (explicit pin) | Transitive pin held only for the Dataverse client | Chat Infrastructure project file | **M-02-1.4** |

Approximately **7.2 MB** of assemblies. ADR-014 Stage 3 is the decision; **M-02-1.4** is the milestone that executes it.

**Rule during the transition.** `Workspace` is the only aggregate migrated to SQL — `SqlWorkspaceRepository.cs` and `WorkspaceConfiguration.cs` are the reference implementations. The other ten aggregates (`Adr`, `Artifact`, `Branch`, `Conversation`, `ConversationMessage`, `Knowledge`, `Project`, `Session`, `Snapshot`, `WorkItem`) still resolve to Dataverse implementations. Do not extend a Dataverse implementation; migrate the aggregate to SQL instead, following `SqlWorkspaceRepository` and the rules in DATABASE_STANDARDS.md. The strangler pattern here is a route, not a destination.

**Also under review, not yet approved.** `Azure.Identity` and `Azure.Core` are present but arrived as Dataverse dependencies. They are **not** confirmed as chosen technologies. Before the Dataverse removal completes, one of two things must happen: either they are deliberately adopted (most plausibly to back `ISecretResolver` at **M-01-5.1**) and recorded here as CURRENT, or they leave with the Dataverse assemblies at **M-02-1.4**. Do not write new code that depends on them until that decision is recorded.

---

## 7. Not yet selected

Nothing in this section has been chosen. Do not introduce any of it, and do not describe any of it as part of Nexus.

| Area | Status | What is true today | What would decide it |
|---|---|---|---|
| **Logging library** | NOT SELECTED | No logging library exists in any repository. No Serilog, no NLog, no structured-logging package. | **M-10-1.1 Correlation across hosts** is the milestone. Its acceptance criteria are the requirements: a correlation id generated at the edge or accepted from the caller, propagated through the Experience API, the Intelligence turn and the model invocation; one request retrievable end to end by that id alone; and no log line containing a secret, a token or a full prompt body. Whatever satisfies those becomes the choice. Until then, CODE_CONVENTIONS.md §11 states only *what* must be logged, not *with what*. |
| **Container tooling** | NOT SELECTED | No Dockerfile, no compose file, no container registry anywhere. | Requires an environment model first — **M-08-4.1 Environment model** and **M-08-4.2 Provisioning**. Deciding a container runtime before the environment model exists would fix the wrong variable. |
| **Cloud provider beyond Azure SQL** | NOT SELECTED | The only cloud dependency proven in use is Azure SQL. No compute, storage, queue, identity or secret service is chosen. **Azure SQL being chosen does not make Azure the cloud provider.** | **M-08-4.1 Environment model**, then **M-08-5.1 Automated deployment**. |
| **Python** | NOT SELECTED | No Python project, package file or script exists in any of the three repositories. | A concrete workload that .NET cannot serve. When one appears it needs an ADR (next: ADR-016) and a Technology Catalogue entry at **M-03-2.1**. Marked *future* in **CODE_CONVENTIONS.md** cross-language tables for exactly this reason. |
| **LinuxCNC** | NOT SELECTED | Nothing related is present. It is a candidate PRODUCTS-layer (12) domain, not a platform technology. | A PRODUCTS-layer decision under **M-12-1.1 Product template and integration checklist**. It must not influence any layer 01–11 choice. |
| **Test framework beyond what the `.csproj` files declare** | NOT SELECTED (as a *standard*) | Six `.Tests` projects exist; their declared framework was not verified. No frontend test framework exists at all — **zero** frontend tests. | Read the `.csproj` files and record the answer here. The frontend gap is called out in TYPESCRIPT_REACT_STANDARDS.md; the testing standard itself belongs to **M-09-3.1 Test plans and test cases**. |
| **Message broker / queue** | NOT SELECTED | AUTOMATION is unimplemented. | **M-01-8.1 In-process event bus** decides whether in-process suffices; **M-05-6.1 Named queues with concurrency and priority** decides if it does not. |
| **Vector store** | NOT SELECTED | No embedding or vector code exists. | **M-02-4.2 Embeddings and vector retrieval**. |
| **CSS framework / design system** | NOT SELECTED | Styling is a single `index.css`. No Tailwind, no CSS-in-JS, no component library. | **M-11-6.1 Design tokens and primitives**. |

---

## 8. Adding a technology

1. Establish that nothing already approved solves the problem. The stack above is small on purpose.
2. Write an ADR. ADRs use **one global sequence**; ADR-014 and ADR-015 exist, so the next is **ADR-016**.
3. Record: purpose, version, where it will be used, why, what was rejected, upgrade policy, compatibility constraints, owning layer.
4. Add it here in the same pull request that first references it. A package reference merged without an entry here is a defect.
5. Pin it per **STACK_VERSION_POLICY.md**.
6. From **M-03-2.1 Technology catalogue** onward, this document stops being the register of record and becomes the human-readable narrative over it; the catalogue holds versions, support windows and end-of-life dates, and **M-03-2.2 Product technology usage** records which product uses what.

## 9. Related documents

| Document | Owns |
|---|---|
| STACK_VERSION_POLICY.md | Pinning mechanics, upgrade cadence, security patches, deprecation |
| NAMING_STANDARDS.md | Names for everything, including projects and packages |
| CODE_CONVENTIONS.md | Cross-language rules |
| CSHARP_STANDARDS.md | C# specifics |
| TYPESCRIPT_REACT_STANDARDS.md | Frontend specifics |
| DATABASE_STANDARDS.md | Schema, migrations, Id/Seq/Ref, cascade rules |
| ADR-014_AZURE_SQL_MIGRATION.md | The Dataverse → Azure SQL decision |
