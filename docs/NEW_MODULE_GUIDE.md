# New Module Guide

**Status:** CURRENT — every procedure follows a pattern that exists in the repositories today;
steps that depend on something not yet built are marked TARGET
**Owner:** DEVELOPER (Layer 07)
**Last updated:** 2026-08-21
**Layer:** 07 DEVELOPER
**Authoritative for:** the ordered procedure for adding a thing to Nexus — a layer module, a product
module, an API endpoint, a database entity, a migration, a frontend feature, an agent, a workflow, a
test, a documentation file — and the checks each one must pass before it is finished.

Not authoritative for: *why* a rule exists or its full statement. Each step links to the document
that owns it — `NAMING_STANDARDS.md`, `CSHARP_STANDARDS.md`, `DATABASE_STANDARDS.md`,
`API_STANDARDS.md`, `TYPESCRIPT_REACT_STANDARDS.md`, `ASSURANCE_STANDARDS.md`. Creating a whole
*product* is `PRODUCT_DEVELOPMENT_GUIDE.md`; creating an *AI* capability in depth is
`AI_DEVELOPMENT_STANDARDS.md`.

---

## 0. Before any procedure

Five checks that apply to every addition. Skipping them is how a change that looks small becomes a
boundary violation.

| # | Check | Owned by |
|---|---|---|
| 1 | The work item is classified `Can run now` or `Can run together` | `DEVELOPMENT_WORKFLOW.md` §5 |
| 2 | Nothing else in flight mutates the same schema or the same contract | `DEVELOPMENT_WORKFLOW.md` §4 |
| 3 | The thing belongs in the layer you are about to put it in | `REPOSITORY_STRUCTURE.md` §2 and §10 |
| 4 | You are in a worktree in a **sibling** directory | `GIT_WORKFLOW.md` §5.1 |
| 5 | There is an acceptance criterion, and you know what would prove it | `ASSURANCE_STANDARDS.md` §3 |

And three prohibitions that no procedure below repeats because they apply to all of them:

- No product type in `Nexus.Platform.Contracts` or `Nexus.Intelligence.Contracts`. **No shared
  kernel.**
- No reference from a lower layer to a higher one, and no reference between two products.
- No `if (Product == X)`. Capability packs are declared, not coded.

---

## 1. A new layer module

A layer module is a project in `Nexus.Platform` (or `Nexus.Intelligence`) that implements part of a
layer. Most layers have none today — `REPOSITORY_STRUCTURE.md` §2.

1. **Confirm the layer.** Which of the twelve owns this capability, and which schema does it write
   to (`core`, `data`, `governance`, `ai`, `automation`, `product_core`, `developer`, `delivery`,
   `assurance`, `operations`, `experience`)?
2. **Confirm the dependency direction.** The module may reference only layers below it. Write down
   which ones before you add a `ProjectReference`.
3. **Name it** `Nexus.<Capability>.<Role>` — `NAMING_STANDARDS.md` §3. Role is one of `.Contracts`,
   `.Core`, `.Infrastructure`, `.Api`, `.Providers.<Vendor>`, or a focused capability name.
4. **Create it under `src/`**, one directory per project, directory name = project name = assembly
   name = root namespace. Never override the root namespace.
5. **Contracts first.** Interfaces and DTOs go in `*.Contracts`, which references nothing but the
   framework. If you cannot express the capability without referencing something, the boundary is
   wrong.
6. **Implementation in `*.Core`**, mirroring the Contracts folder structure. The existing pattern:
   `Nexus.Platform.Contracts/Models/IModelGateway` is implemented by
   `Nexus.Platform.Core/Models/RoutingModelGateway`.
7. **Add both projects to the repository's `.slnx`.** A project outside the solution is never built
   by a full-solution build.
8. **Register the DI extension** — one `IServiceCollection` extension per module, named for the
   module, following `IntelligenceServiceCollectionExtensions`.
9. **Extend the architecture test.** `PlatformBoundaryTests.cs` or `BoundaryRuleTests.cs` gets a rule
   asserting the new project's allowed dependencies. **This is the step that makes the boundary
   real** — everything above it is convention.
10. **Add a behaviour test** for the first real behaviour, in `tests/<Project>.Tests`, folder
    structure mirroring the production project. Do **not** create an empty test project first —
    `ASSURANCE_STANDARDS.md` §9.3.

**Checks:** solution builds; architecture tests green; no reference upward; `*.Contracts` references
nothing; namespace equals folder path.

---

## 2. A new product module

A product module is a bounded piece of a product's own domain, inside that product's repository. The
pattern to copy is `Nexus.Products.Chat.*`.

1. **Confirm it is product-specific.** If two products would want it, it belongs in PRODUCT CORE
   (layer 06), not in a product.
2. **Choose the project.** Domain concepts → `.Domain`; use-case orchestration → `.Application`;
   persistence and adapters → `.Infrastructure`; HTTP → `.Api`.
3. **Create a folder per aggregate** inside `.Domain` — §4 below is the aggregate procedure.
4. **Application services** orchestrate; they hold no persistence and no HTTP. Business rules live
   on the aggregate.
5. **Infrastructure** gets `Sql/Configurations/`, `Sql/Repositories/`, and nothing that leaks back
   up: `.Domain` never references `.Infrastructure`.
6. **Register in the product module class.** `ChatProductModule.cs` is the pattern — one place where
   the product's services and endpoints are wired, called from `Program.cs`.
7. **Extend `BoundaryTests.cs`** with the module's dependency rule.
8. **Never reference another product.** Not its types, not its database, not its endpoints.

**Checks:** `.Domain` has no infrastructure reference; the module registers in one place;
architecture tests green.

---

## 3. A new API endpoint

The pattern is `Nexus.Products.Chat.Api/Endpoints/` and `Nexus.Intelligence.Api/Endpoints/`.
`API_STANDARDS.md` owns route, verb, status code, error shape, pagination and versioning rules; this
is the mechanical procedure.

1. **Create `Endpoints/<Name>Endpoint.cs`** in the `.Api` project. Plural resource name —
   `WorkspacesEndpoint.cs`, not `WorkSpacesEndpoint.cs` or `WorkspaceEndpoint.cs`.
2. **One extension method:**

   ```csharp
   public static IEndpointRouteBuilder MapWorkspacesEndpoints(this IEndpointRouteBuilder app)
   ```

3. **Routes** are plural, lowercase, versioned: `/api/v1/workspaces`,
   `/api/v1/workspaces/{id:guid}`. Intelligence serves under `/intelligence/v1`.
4. **Request and response records** per `NAMING_STANDARDS.md` §25: `CreateWorkspaceRequest`,
   `CreateWorkspaceResponse`, `GetWorkspaceResponse`, `ListWorkspacesResponse`,
   `UpdateWorkspaceRequest`, `UpdateWorkspaceResponse`. Never expose a domain type over HTTP.
5. **Body is routing, binding, validation invocation and result translation — nothing else.** Past
   roughly twenty lines, logic has leaked in; move it behind an application service or a repository.
6. **Errors as Problem Details** — `API_STANDARDS.md` §7. Never return a raw exception message.
7. **Call `MapWorkspacesEndpoints` once** from `Program.cs`, or from the product module class where
   one exists.
8. **Never return `Seq`.** It is an internal allocation mechanism. `Id` and `Ref` are the external
   identifiers — `DATABASE_STANDARDS.md` §3.
9. **Check backward compatibility** if the resource already exists — `API_STANDARDS.md` §15. The
   frontend is a real consumer.
10. **Verify the behaviour**, not the status code. A 200 with the wrong body is a defect that Swagger
    reports as success.

**Checks:** route matches the file name; DTOs are records, not domain types; no `Seq` in any
response; Problem Details on every failure path; endpoint mapped exactly once.

---

## 4. A new database entity

The full requirement list is `DATABASE_STANDARDS.md` §12 and is not repeated here. This is the file
layout and the order to work in.

1. **Create the aggregate folder** in `.Domain`, named for the aggregate, containing exactly:

   ```
   Workspace/
     Workspace.cs             the aggregate root
     WorkspaceId.cs           strongly-typed id
     WorkspaceStatus.cs       status enum
     IWorkspaceRepository.cs  repository interface
   ```

   This is the pattern used by all eleven Chat aggregates: `Adr`, `Artifact`, `Branch`,
   `Conversation`, `ConversationMessage`, `Knowledge`, `Project`, `Session`, `Snapshot`, `WorkItem`,
   `Workspace`.
2. **Private constructor + `public static Workspace Restore(...)`** for rehydration. The public
   creation path is a factory method that enforces invariants; `Restore` bypasses them because the
   database already holds valid state.
3. **Inherit `AggregateRoot`** (or `Entity` for a non-root) from `Domain/Common/`.
4. **`<Name>Configuration.cs`** in `Infrastructure/Sql/Configurations/`, implementing
   `IEntityTypeConfiguration<T>`. `WorkspaceConfiguration.cs` is the reference.
5. **Explicit schema.** The layer's schema — **never `org`** for anything new. `org` is the legacy
   schema of the single existing migration, and **M-02-1.5** renames it.
6. **Id / Seq / Ref**, per `DATABASE_STANDARDS.md` §3: `Id` `uniqueidentifier` non-clustered PK;
   `Seq` `int IDENTITY(1,1)` shadow property with the clustered index; `Ref` computed **PERSISTED**
   unique column. The proven form:

   ```sql
   ('WKS-' + RIGHT('00000000' + CAST([Seq] AS varchar(8)), 8))
   ```

   Computed in the database, not in C#, because only the database guarantees uniqueness under
   concurrent insert.
7. **Register a `Ref` prefix** that no other entity uses.
8. **Audit columns, `rowversion` if mutable, explicit string lengths and decimal precision.**
9. **Every relationship gets an explicit `DeleteBehavior`.** Only the owning parent cascades;
   reference FKs `Restrict`; self-references `NoAction`. Getting this wrong produces SQL Server
   error 1785 at migration time, and it is the single most common failure in this procedure.
10. **Index every foreign key and every filter or sort column.**
11. **Converters go in `Sql/Conventions/StronglyTypedIdConverters.cs` and nowhere else.**
12. **`Sql<Name>Repository`** in `Sql/Repositories/`, implementing `I<Name>Repository`, returning
    materialised results — never an `IQueryable` across the boundary.
13. **Migration** — §5.
14. **Acceptance criterion and verification method** — `ASSURANCE_STANDARDS.md`.

**Checks:** the four aggregate files exist; schema is explicit and is not `org`; `Ref` is computed
PERSISTED and unique; every relationship has an explicit delete behaviour; converters are in one
file; the repository returns materialised results.

---

## 5. A new migration

1. **Check nobody else is mid-migration on this `DbContext`.** Two migrations on one context
   conflict on the **model snapshot** even when they touch entirely different tables. This is
   parallel-safety rule 3 and it is the one most often got wrong —
   `DEVELOPMENT_WORKFLOW.md` §4.
2. **Confirm the entity work is complete** — §4. A migration generated from a half-configured entity
   produces DDL you will have to reverse.
3. **Generate it:**

   ```powershell
   cd C:\Personal\Nexus.Experience
   dotnet ef migrations add AddProjectAggregate `
     --project src\Nexus.Products.Chat.Infrastructure `
     --startup-project src\Nexus.Products.Chat.Api
   ```

4. **Name it** `<PascalCaseName>` — EF prefixes the timestamp, producing
   `20260820180802_InitialSqlSchema.cs`. The name states what the migration does.
5. **Read the generated file before applying it.** Every time. Check the schema, the computed
   column, the delete behaviours and the indexes.
6. **Inspect the DDL:**

   ```powershell
   dotnet ef migrations script --idempotent `
     --project src\Nexus.Products.Chat.Infrastructure `
     --startup-project src\Nexus.Products.Chat.Api
   ```

7. **Apply it:** `dotnet ef database update` with the same project arguments.
8. **Verify the behaviour, not the DDL.** For an Id/Seq/Ref entity that means two successive inserts
   returning server-generated `Ref` and `Seq` values — the proven evidence pattern is in
   `api_run.log` from 2026-08-20:
   `INSERT INTO [org].[Workspace] (...) OUTPUT INSERTED.[Ref], INSERTED.[Seq] VALUES (...)`.
9. **It must be reversible.** `dotnet ef database update <PreviousMigration>` has to work.
10. **Commit the migration, the designer file and the model snapshot together**, and **push
    immediately**. A migration is exactly the kind of proven, expensive work that was sitting
    uncommitted when the 2026-08-20 incident happened.

**Checks:** one migration per work item; nobody else touching the context; reversible; behaviour
verified against LocalDB; snapshot committed with it.

---

## 6. A new frontend feature

The structure is `TYPESCRIPT_REACT_STANDARDS.md` §1–§2; this is the file-by-file procedure. The
pattern to copy is `features/workspaces/`, not `features/projects/` — the latter historically had a
second HTTP path.

1. **Create `src/features/<feature>/`** — lowercase, plural where the feature is a collection:
   `chat/`, `projects/`, `workspaces/`, `system/`.
2. **Types:** `<Thing>.ts` — `Workspace.ts`, `Project.ts` — or `<feature>.types.ts` where the feature
   has several, as `chat.types.ts` does.
3. **API client:** `<Thing>Api.ts` — `workspacesApi.ts`, `projectsApi.ts`, `chatApi.ts`, `systemApi.ts`.
4. **Every request goes through `api/ApiClient.ts`.** No raw `fetch`, no direct `import.meta.env`
   in a feature file. This is the single-HTTP-path rule and it was a real defect once —
   `TYPESCRIPT_REACT_STANDARDS.md` §5.
5. **Hooks:** one `use<Thing>.ts` per query or mutation — `useWorkspaces.ts`, `useWorkspace.ts`,
   `useCreateWorkspace.ts`, `useUpdateWorkspace.ts`. TanStack Query owns server state; do not mirror
   it into component state.
6. **Components:** PascalCase `.tsx` — `WorkspaceSelector.tsx`, `CreateWorkspaceForm.tsx`.
7. **Pages** go in `src/pages/` as `<Name>Page.tsx` and are routed from `routes/AppRoutes.tsx`.
8. **Loading, error and empty states are part of the feature**, not an afterthought. `ApiError` and
   `RouteErrorBoundary.tsx` exist for this.
9. **Configuration through `config/environment.ts`** and `VITE_`-prefixed variables only.
10. **Match the server contract exactly** — response DTO field names, and the fact that `Seq` is
    never present. If the contract needs to change, that is §3, not a frontend workaround.

**TARGET — testing.** There are **zero frontend tests** and no framework is selected. Do not add one
unilaterally; the gap is recorded in `TYPESCRIPT_REACT_STANDARDS.md` §19 and
`ASSURANCE_STANDARDS.md` §14. Until it closes, a frontend feature's evidence is a described manual
verification.

**Checks:** all HTTP through `ApiClient`; one hook per query; no `import.meta.env` outside
`config/environment.ts`; loading and error states present; route registered.

---

## 7. A new agent

Full architecture in `AI_DEVELOPMENT_STANDARDS.md` §8. The procedure:

1. **Confirm it is an agent.** An agent reasons over supplied context toward a goal. A deterministic
   sequence of steps is a **workflow** (§8), and building it as an agent makes it unpredictable for
   no benefit.
2. **Create `BuiltIn/<Name>Agent.cs`** in `Nexus.Intelligence.Agents`, implementing `IAgent`.
   `DeveloperAgent.cs` is the only existing example and is a **974-byte stub** — copy its shape, not
   its emptiness.
3. **Declare `AgentMetadata`** — identity, `AgentType`, and the capabilities it claims.
4. **Register it in `AgentRegistry`.** Dispatch is `AgentDispatcher`; selection during a turn is
   `AgentSelector`. An agent is never called directly.
5. **Take context only from `AgentContext`.** The agent must not reach into a product's database, a
   repository or an HTTP endpoint for context. **AI never sees product structure** — it receives a
   `ContextBundle`, and `ScopeRef` is opaque to it.
6. **Honour `TrustLevel`.** Untrusted `ContextItem` content is data to be reasoned about, never
   instructions to be followed. An agent that follows retrieved text has been compromised by whoever
   wrote it.
7. **Return `AgentResult`**, with the reasoning trace the turn pipeline records in `DecisionTrace`.
8. **Tools:** an agent invokes a tool only through `IToolGateway` from `IToolCatalog`, and every tool
   declares a `SideEffectClass`. **CURRENT: `EmptyToolCatalog` and `EmptyToolGateway` mean no tool
   can be invoked at all** — **TARGET M-01-7.1**.
9. **Propose, do not act.** Anything with an effect becomes a `ProposedAction` for a human or for
   AUTOMATION to execute. This is the rule that keeps an agent safe while there is no authorization
   layer.
10. **Evaluation, not unit test.** Agent behaviour is proven by scoring against a fixed question set
    — `ASSURANCE_STANDARDS.md` §13, **TARGET M-04-5.1** and **M-09-6.1**.

**Checks:** implements `IAgent`; registered in `AgentRegistry`; context only from `AgentContext`; no
product type referenced; no direct tool call; side effects proposed, not performed.

---

## 8. A new workflow

**TARGET — the AUTOMATION layer has no projects and no code.** There is no workflow engine, no
queue, no job runner and no message broker (`TECHNOLOGY_STACK.md` §7). Written now so the first one
is built in the right place rather than inside whichever product needed it first.

1. **Confirm it is automation, not intelligence.** *Intelligence reasons. Automation executes.* If
   the steps are deterministic, it is a workflow.
2. **Confirm it is not just a method call.** A workflow earns its cost when it needs retry,
   scheduling, approval or escalation. Until then, an explicit state transition in the owning layer
   is the correct implementation, and it is what DEVELOPER V1a deliberately uses.
3. **It belongs in AUTOMATION (layer 05), schema `automation`** — never inside a product.
4. **The workflow does not know the business meaning of what it runs.** A workflow that approves a
   purchase order does not know what a purchase order is; it holds a definition, a state machine and
   a result.
5. **Every step is idempotent**, because retry is the point — `CODE_CONVENTIONS.md` §16.
6. **Irreversible and external effects need an approval gate** — **M-05-5.1**, and
   `SECURITY_STANDARDS.md` §10's `SideEffectClass` table.
7. **AI-proposed actions land here.** A `ProposedAction` from a turn is executed by AUTOMATION under
   a policy, never by the agent itself.

Milestones that make this real: **M-01-8.1** in-process event bus, **M-05-1.2** dispatch loop with
retry and backoff, **M-05-1.3** escalation and dead-letter, **M-05-5.1** approval gates, **M-05-6.1**
named queues.

**Checks (when the layer exists):** in AUTOMATION; no product type referenced; steps idempotent;
approval gate on any irreversible or external effect.

---

## 9. A new test

`ASSURANCE_STANDARDS.md` owns test types, evidence and what counts as proof. The procedure:

1. **Decide what must be proven**, then pick the type. DEVELOPER asks what must be proven; DELIVERY
   executes it repeatably; ASSURANCE decides whether the requirement was satisfied; OPERATIONS
   proves the running system stays healthy.
2. **Put it in the existing test project** for the production project under test:
   `Nexus.Platform.Tests`, `Nexus.Intelligence.Tests`, `Nexus.Products.Chat.Tests`. **Do not create
   a new test project to satisfy a naming table** — `Nexus.Platform.Tests` has sat empty and has
   made the system look tested the whole time. Filling it costs nothing and removes a lie.
3. **Mirror the production folder structure.** `KeywordContextRanker` lives in
   `Nexus.Intelligence.Context/Ranking/`, so its test is `Ranking/KeywordContextRankerTests.cs`.
4. **Name it `<TypeUnderTest>Tests.cs`**; name the method for the behaviour and the expected outcome.
5. **Unit tests do no I/O.** No database, no HTTP, no filesystem.
6. **Architecture rules go in the `*.Architecture.Tests` project** — `PlatformBoundaryTests.cs`,
   `BoundaryRuleTests.cs`, `BoundaryTests.cs`, using NetArchTest. **Any new boundary rule stated in
   any standard should end up here**; these three files are the only mechanical enforcement in the
   system.
7. **Assert the behaviour, not the implementation.** A test that breaks on every refactor of correct
   code is a maintenance cost, not a safety net.
8. **Run the whole solution's tests** before pushing. There are five files; it takes seconds.
9. **Link the test to its acceptance criterion** where one exists — **TARGET M-09-1.1**; there is
   nothing to link to yet.

**Checks:** in an existing project; folders mirror production; no I/O in a unit test; whole suite
still green.

---

## 10. A new documentation file

1. **Check no document already owns the subject.** One subject, one document. If another owns it,
   link by filename and stop — that rule is why this guide is short.
2. **Pick the kind and its name** — `NAMING_STANDARDS.md` §42:

   | Kind | Pattern | Example |
   |---|---|---|
   | Canonical numbered set | `<nn>_<SCREAMING_SNAKE>.md` | `09_ROADMAP_AND_MILESTONES.md` |
   | Standard or reference | `SCREAMING_SNAKE.md` | `DATABASE_STANDARDS.md` |
   | ADR | `ADR-<nnn>_<SCREAMING_SNAKE>.md` | `ADR-014_AZURE_SQL_MIGRATION.md` |
   | Dated incident record | `<SUBJECT>_<yyyy-MM-dd>.md` | `GIT_RECOVERY_2026-08-20.md` |
   | Runbook | `<SUBJECT>_RUNBOOK.md` | `NEXUS_MIGRATION_RUNBOOK.md` |
   | State snapshot | `<SUBJECT>_STATE.md` | `MIGRATION_STATE.md` |

3. **ADRs use one global sequence.** ADR-014 and ADR-015 exist; the next is **ADR-016**. There is no
   per-layer or per-repository numbering, and introducing one breaks the sequence permanently.
4. **Front matter:** `Status`, `Owner`, `Last updated`, `Layer` where relevant, `Authoritative for`
   — and a "not authoritative for" line naming the documents that own the adjacent subjects.
5. **Mark CURRENT / TARGET / TRANSITION** wherever the document differs from what builds today, and
   name the milestone that closes the gap. A developer must never read a target standard and be
   unable to build the current code.
6. **Use real names.** Real type names, real paths, real commands. If a decision has not been made,
   write "Not yet decided" and say what would decide it.
7. **Place it in `C:\Personal\Nexus.Platform\docs\`.** One documentation home — a standard copied into a
   product repository will disagree with itself within a month.
8. **Add an ADR whenever the document records a decision**, especially a new technology: an ADR, a
   `TECHNOLOGY_STACK.md` entry, and the first `PackageReference` land in the same pull request.

**TARGET — M-02-2.1 Document store** makes documents `Document`/`DocumentVersion` records with
links to the milestones they describe. Until then the filesystem is the store and git is the version
history.

---

## 11. The five things most often got wrong

Ranked by how often they actually happen, not by severity.

| # | Mistake | Why it happens | The rule |
|---|---|---|---|
| 1 | A second migration in flight on the same `DbContext` | The two work items touch different tables, so they look independent | The **model snapshot** is shared. Parallel-safety rule 3 |
| 2 | A product type reaching into `*.Contracts` | It removes a mapper and looks like simplification | **No shared kernel.** The mapper is the boundary |
| 3 | New tables in schema `org` | Copied from `WorkspaceConfiguration.cs` | `org` is legacy. Set the schema explicitly — M-02-1.5 |
| 4 | Business logic inside an endpoint | It is where the request already is | Endpoints route, bind, validate, translate. Twenty lines is the signal |
| 5 | A converter registered outside `StronglyTypedIdConverters.cs` | Two places to register means two places to forget | One file, always |

---

## 12. References

- `REPOSITORY_STRUCTURE.md` — which repository and directory anything belongs in.
- `NAMING_STANDARDS.md` — the name of every artefact these procedures create.
- `CSHARP_STANDARDS.md` — namespaces, records, DI, EF Core, domain entities, DTOs, handlers.
- `DATABASE_STANDARDS.md` — §3 Id/Seq/Ref, §5 relationships, §9 migrations, §12 the entity checklist.
- `API_STANDARDS.md` — routes, versioning, DTOs, Problem Details, compatibility.
- `TYPESCRIPT_REACT_STANDARDS.md` — the frontend form of §6.
- `AI_DEVELOPMENT_STANDARDS.md` — the full agent, prompt, context and tool architecture.
- `PRODUCT_DEVELOPMENT_GUIDE.md` — when the thing you are adding is a whole product.
- `ASSURANCE_STANDARDS.md` — what a test must prove, and why not to create empty test projects.
- `DEVELOPMENT_WORKFLOW.md` — classification and parallel safety before any of this starts.
- `GIT_WORKFLOW.md` — worktrees, commits, push-at-every-stage-boundary.
