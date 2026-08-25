# Nexus — Master Architecture, Purpose and Execution Roadmap

**Version:** 2.2 — two gates. Development Ready brought forward so business systems start earlier.
**Status:** Architecture accepted. No code changed, no migrations run, no entities deleted, no repositories restructured.
**Date:** 2026-08-21
**Supersedes:** prior Nexus architecture prompts where in conflict. Absorbs ADR-014 (Azure SQL migration) as Layer 02 foundation work.
**Companion:** `nexus-roadmap.yaml` v2.2 — 614 nodes across twelve layers and six phases, mechanically validated against thirteen checks. It is the import source for DEVELOPER V1a (`M-07-1.1`).
**Documentation:** `docs/DOCUMENTATION_INDEX.md` indexes 43 standards, guides and per-layer architecture documents that together let a developer — human or agent — build Nexus without prior conversation history.

---

## Part 0 — How to read this

This report is the **architecture**. Its companion `nexus-roadmap.yaml` is the **work breakdown** — 90 features, 151 milestones, 108 work items, 140 tasks and 113 subtasks, every node tagged with a phase, machine-verified for unique IDs, resolvable dependencies and phase-order correctness.

### The depth rule

The brief asked for full milestone and task decomposition across all eleven layers. Applied literally that is several hundred pages, and most of it would be invented — task-level detail for Layer 10 OPERATIONS today would be fiction, because there is no Operations code, no deployed runtime and no decided scope.

The brief itself contains the rule that resolves this, in §3: *"Do not create roadmap items with no architectural purpose."* So:

> **Decomposition depth is a function of phase, not of importance.**

| Phase | Depth | Rationale |
|---|---|---|
| **P0, P1** | feature → milestone → work item → task → subtask | Work starts on approval. Detail is load-bearing. |
| **P2** | feature → milestone → work item | Shape is decidable; task content depends on what P1 actually produces. |
| **P3** | feature → milestone | Outcome and dependency are decidable. Task detail would be guesswork. |
| **P4, P5** | feature | Enough to reserve the architectural space and prove the dependency chain closes. |

Every layer is defined completely at every level *the architecture* requires — purpose, responsibilities, what it owns, what it explicitly does not own, sub-layers, data, interfaces, dependencies, V1 boundary. Only *execution* detail is phase-gated.

**Developer V1 deepens the rest as each phase approaches. That is precisely its job**, and it is why the roadmap is machine-readable rather than prose: milestone `M-07-1.1` imports this file into the work graph, and from that point the roadmap maintains itself in structure rather than in markdown.

### The six phases

| Phase | Name | Intent | Estimate |
|---|---|---|---|
| **P0** | Groundwork | Make the current system safe, verifiable and single-stack. Nothing new is designed. | 4–5 wk |
| **P1** | Development Ready (GATE A) | Real identity, minimum assurance, DEVELOPER V1a proving three simultaneous isolated work items. Not EXPERIENCE, not durable AI memory. | 8–10 wk |
| **P2** | Autonomy and Delivery | Developer dispatches its own workers. Code reaches a running environment automatically and its health is visible. | 10–14 wk |
| **P3** | Platform Maturity | The layers that make a second and third product cheap: governance, automation, product foundation, retrieval, the experience system. | 16–20 wk |
| **P4** | Product Expansion | Consumer products on the matured platform. Business systems pulled in on demand. | continuous |
| **P5** | Nexus Builds Nexus | Evaluation-driven self-improvement, advanced autonomy, safety-critical domains. | open-ended |

Phases are sequential for the **foundation**; from P2 onward two streams run in parallel, and from P4 three.

## Part 1 — Executive summary

**The realignment is sound and cheaper than it looks.** The eleven layers are not a rewrite of the current system; they are a finer-grained reading of the three-solution split that already exists. Nexus.Platform is Layers 01/03/06, Nexus.Intelligence is Layer 04, Nexus.Experience is Layers 10/11. Nothing currently built lands in the wrong place. The V2.1 restructure completed last week did most of the structural work this brief asks for, before the brief existed.

**Nothing in flight is wasted.** The Azure SQL migration (ADR-014, Stages 1b–3) becomes Layer 02 foundation work and is a GATE A blocker — it gets *more* important under this architecture, not less. The frontend F0–F4 work, completed and pushed, is Layer 10/11 and stands. Continue both.

**The plan contradicts itself in one place, and it matters.** §6 puts Delivery & Infrastructure in Stream B, after the gate. §33 requires Developer V1 to demonstrate independent build, independent test, result capture and controlled integration before the gate. You cannot demonstrate any of those without CI. Evidence: `.github/workflows/` is empty, `Nexus.Platform.Tests` is a project with no test files, and three repositories lost their git object database to what looks like antivirus quarantine on 2026-08-20. **A minimal slice of Layer 08 is mandatory before the gate.** This is the single most consequential correction in this report.

**Developer V1 as specified is roughly six months of work and delays everything else.** §7's capability chain has fifteen links; §27 says business development must start early. Both cannot be true. The resolution is to split Developer V1 into **V1a (advisory)** and **V1b (executing)**, and close the gate at V1a. V1a is the system of record plus dependency analysis plus worker *coordination* — humans and coding agents still execute. V1b lets Developer dispatch workers itself. V1a is roughly six to eight weeks and satisfies §33's acceptance test in full, because the test is about isolation, simultaneity and controlled integration, not about who types the command.

**Identity has moved from debt to blocker.** `Nexus.Platform.Identity` is a single 240-byte file. ADR-014 Stage 3 removes Dataverse, and Dataverse's row-level security leaves with it. Every ownership, attribution, approval and review concept in Layers 03, 06 and 07 needs a real subject. Identity is now on the critical path for the gate.

**"Nexus builds Nexus" has a bootstrapping trap.** If Developer V1 is the only route to building everything else, a Developer V1 defect halts both streams simultaneously. V1a/V1b sequencing is the mitigation: in advisory mode a Developer outage costs you a dashboard, not a development capability.

**Recommended immediate action, before any of this is approved:** commit and push the uncommitted Stage 1b work sitting in `Nexus.Web`. It is correct, its acceptance proof is in `api_run.log` (§2.7), and this repository lost its entire history to an unexplained cause forty-eight hours ago.

---

## Part 2 — Current state

Every claim below was read from disk on 2026-08-21 via the device bridge. Nothing is inferred from documentation.

### 2.1 Repository map

| Repository | Remote | Role today | Solution |
|---|---|---|---|
| `C:\Personal\Nexus.Platform` | `github.com/prtcare/Nexus.Platform` | Platform — NuGet libraries only, no host | `Nexus.Platform.slnx` |
| `C:\Personal\Nexus.Intelligence` | `github.com/prtcare/Nexus.Intelligence` | Intelligence — deployed at `/intelligence/v1` | `Nexus.Intelligence.slnx` |
| `C:\Personal\Nexus.Experience` | `github.com/prtcare/Nexus.Experience` | Chat product — `/api/v1` + React client | `Nexus.Experience.slnx` |
| `C:\Personal\LocalNuGet` | — | Local package feed for Platform/Intelligence packages | n/a |

### 2.2 Project map — Nexus.Platform

Live modules under `src\`:

| Project | Contents | Substance |
|---|---|---|
| `Nexus.Platform.Contracts` | `Governance/` (AuditEntry, IAuditLog, IQuotaPolicy, IUsageMeter, QuotaVerdict, UsageRecord), `Identity/` (IIdentityService, IProductRegistry, ITenantResolver, ResolvedIdentity), `Models/` (13 types — IModelCatalog, IModelGateway, ModelDescriptor, ModelInvocation, ModelUsage…), `Secrets/ISecretResolver`, `Tools/` (IToolCatalog, IToolGateway, ToolDescriptor, ToolInvocation, ToolResult, SideEffectClass) | **Real.** The contract surface is genuinely designed. |
| `Nexus.Platform.Core` | `Governance/` ConsoleAuditLog, InMemoryUsageMeter, PermissiveQuotaPolicy; `Models/` AggregatingModelCatalog, RoutingModelGateway, IModelCatalogSource, INamedModelGateway | **Real but volatile.** Every governance implementation is in-memory or console. Nothing survives a restart. |
| `Nexus.Platform.Providers.OpenAI` | OpenAIModelGateway (5.2 KB), OpenAIModelCatalogSource, OpenAIOptions, DI extensions | **Real.** The only working provider. |
| `Nexus.Platform.Providers.Anthropic` | `AnthropicModelGateway.cs` — 306 bytes | **Stub.** |
| `Nexus.Platform.Identity` | `IdentityProvider.cs` — 240 bytes | **Stub.** |
| `Nexus.Platform.Persistence` | `PlatformStore.cs` — 308 bytes | **Stub.** |
| `Nexus.Platform.Tools` | `ToolProvider.cs` — 231 bytes | **Stub.** |

Legacy shells still present as directories but empty or near-empty: `NexusAI.Agents`, `NexusAI.Api`, `NexusAI.Core`, `NexusAI.Domain`, `NexusAI.Foundation`, `NexusAI.Host`, `NexusAI.Infrastructure`. `NexusAI.Application` retains `Agents/`, `Orchestration/Commands/`, `WorkItem/Queries/` folder skeletons. These are gitignored husks from the V2.1 restructure.

### 2.3 Project map — Nexus.Intelligence

| Project | Contents | Substance |
|---|---|---|
| `Nexus.Intelligence.Contracts` | `Turns/` (17 types — IntelligenceTurnRequest/Response, ScopeRef, ActorRef, TurnConstraints, DecisionTrace, PlanStep, ProposedAction, UsageSummary…), `Context/` (ContextBundle, ContextItem, ContextItemKind, TrustLevel, Citation, PersistenceHint), `Results/` (ResultReport, ResultOutcome), `Client/IIntelligenceClient` | **Real and well-formed.** This is the strongest contract surface in the codebase. |
| `Nexus.Intelligence.Core` | `Turns/` — full pipeline: IntentClassifier, PolicyGate, ContextSelector, AgentSelector, ModelSelector, PromptStep, ModelStep, ToolLoop, ResponseComposer, TurnPipeline (6.9 KB), InMemoryTurnTraceStore; `Planning/Planner` (3.9 KB); `Execution/ExecutionEngine` | **Real.** A complete, working turn pipeline. |
| `Nexus.Intelligence.Context` | `Ranking/` KeywordContextRanker (2.6 KB), RankingOptions; `Prompting/` PromptAssembler (3.6 KB) | **Real but primitive.** Keyword ranking only — no embeddings, no vector retrieval. |
| `Nexus.Intelligence.Agents` | Abstractions (IAgent, IAgentRegistry, IAgentDispatcher, IAgentRuntime, AgentContext, AgentMetadata, AgentType), AgentRegistry, AgentDispatcher, `BuiltIn/DeveloperAgent.cs` (974 bytes) | **Skeleton.** Registry works; `DeveloperAgent` is a stub. |
| `Nexus.Intelligence.Memory` | IMemoryStore, InMemoryMemoryStore, MemoryRecord, MemoryQuery, MemoryKind | **Volatile.** In-memory only. |
| `Nexus.Intelligence.Api` | Endpoints: Turns, Plans, Results, Capabilities, Health; `Tooling/` EmptyToolCatalog + EmptyToolGateway; `ResultReports/InMemoryResultReportStore` | **Real, but tool surface is empty and results are volatile.** |

### 2.4 Project map — Nexus.Experience

**Domain — eleven aggregates**, each with an aggregate root, strongly-typed ID, status enum and repository interface:

`Adr` · `Artifact` · `Branch` · `Conversation` · `ConversationMessage` · `Knowledge` · `Project` · `Session` · `Snapshot` · `WorkItem` · `Workspace`

**API — eleven endpoint groups** under `Endpoints/`: Artifacts, Branches, Chat, ConversationMessage, Conversations, Knowledge, Projects, Sessions, Snapshots, WorkItems, WorkSpaces, plus Health.

**Infrastructure — dual persistence, mid-migration:**
- `Sql/` — `NexusChatDbContext`, `Configurations/WorkspaceConfiguration`, `Conventions/StronglyTypedIdConverters`, `Repositories/SqlWorkspaceRepository`, and migration `20260820180802_InitialSqlSchema` (org schema, `Seq` IDENTITY, `Ref` computed-persisted, unique index)
- Dataverse implementations for the remaining ten aggregates
- Both live behind the `Nexus:Persistence` configuration key (ADR-014 strangler pattern)

**Frontend — React/TypeScript under `src\Nexus.Experience.Client\src\`:**
- `api/` — ApiClient, ApiError (single HTTP path, post-F0)
- `features/chat/` — 15 files: ChatPanel, MessageThread, ConversationList, CreateConversationForm, CitationsPanel, ChatTelemetryContext, citationTargets, useCitationTarget, useConversation(s), useConversationMessages, useCreateConversation, useSendChat, chatApi, chat.types
- `features/projects/`, `features/workspaces/`, `features/system/`
- `pages/` — Dashboard, Chat, Insights, ProjectDetails, WorkItem, KnowledgeItem, Workspaces, CreateWorkspace, WorkspaceSettings, Settings, NotFound
- `components/RouteErrorBoundary`, `layouts/AppLayout`, `routes/AppRoutes`

### 2.5 Test coverage — the weakest area in the system

| Repository | Test projects | Actual test files |
|---|---|---|
| Nexus.Platform | `Nexus.Platform.Tests`, `Nexus.Platform.Architecture.Tests` | `PlatformBoundaryTests.cs` only. **`Nexus.Platform.Tests` contains a `.csproj` and no `.cs` files at all — zero behaviour tests for Platform.** |
| Nexus.Intelligence | `Nexus.Intelligence.Tests`, `Nexus.Intelligence.Architecture.Tests` | `BoundaryRuleTests.cs`, `KeywordContextRankerTests.cs` — 2 files |
| Nexus.Experience | `Nexus.Products.Chat.Tests`, `Nexus.Products.Chat.Architecture.Tests` | `BoundaryTests.cs`, `ChatContextBundleMapperTests.cs` — 2 files |

**Five test files across three repositories, three of which are architecture-boundary tests rather than behaviour tests.** That leaves exactly two behaviour tests in the entire system: `KeywordContextRankerTests` and `ChatContextBundleMapperTests`. There is no test for the turn pipeline, the planner, the tool loop, the model gateway, any repository, any endpoint, or any React component.

### 2.6 Build, CI and deployment

- **CI: none, in any repository.** `C:\Personal\NexusAI\.github\workflows\` exists and is **empty**. `Nexus.Web` and `Nexus.Int` have no `.github` directory at all.
- **Build:** three separate `.slnx` solutions, .NET 10, `Directory.Build.props` per repo, `global.json` pinning SDK.
- **Package flow:** `pack-local.ps1` in NexusAI and Nexus.Int publishes to `C:\Personal\LocalNuGet`, consumed via `nuget.config`. Platform → Intelligence → Web is a local file-feed chain.
- **Deployment:** no infrastructure-as-code, no environment definitions, no deployment pipeline found in any repository.

### 2.7 Git state

All three repositories were recovered on 2026-08-20 after `.git\objects` disappeared from **all three simultaneously** — consistent with antivirus quarantine of extensionless zlib blobs. Recovery was by fresh clone and in-place `.git` swap. `.git-broken\` directories remain in **all three** repositories.

`Nexus.Web` is on `feat/azure-sql` at `29ac2f4`. **SQL Stage 1b is complete, proven and uncommitted** — domain, EF configuration, migration and API DTOs all modified between 18:04 and 18:08 UTC on 2026-08-20, with a successful build afterwards, and no commit since 17:54 UTC.

Its acceptance proof is on disk in `api_run.log` (18:09 UTC) and is worth quoting, because it is the evidence that the `Id`/`Seq`/`Ref` pattern works as designed — the database allocates the reference, not C#:

```sql
INSERT INTO [org].[Workspace] ([Id], [CreatedAt], [Description], [Name], [Owner], [Status])
OUTPUT INSERTED.[Ref], INSERTED.[Seq]
VALUES (@p0, @p1, @p2, @p3, @p4, @p5);
```

Two successive inserts, both returning server-generated `Ref` and `Seq`, followed by a list query. ADR-014 Rule 4 is confirmed working against a live database.

### 2.8 Documentation

Nineteen files in `C:\Personal\NexusAI\docs\`: the numbered canonical set `00`–`12`, `README.md`, `ADR-014`, `ADR-015`, `DATAVERSE_SCHEMA_REFERENCE.md`, `NEXUS_ARCHITECTURE_V2.md`, `NEXUS_MIGRATION_RUNBOOK.md`. Loose prompt files at repo root: `SQL_PROMPTS_STAGE_1B_2A.md`, `SQL_PROMPTS_STAGE_2B_2C.md`, `FRONTEND_PROMPTS_F0_F4.md`, `DOCS_CONSOLIDATION_PROMPT.md`.

Documentation is in good shape and recently consolidated. It is also **entirely unstructured** — markdown files with no machine-readable representation, which is precisely the problem §39 identifies.

---

## Part 3 — Architecture problems

### 3.1 Everything stateful is in memory

`InMemoryUsageMeter`, `PermissiveQuotaPolicy`, `ConsoleAuditLog`, `InMemoryMemoryStore`, `InMemoryTurnTraceStore`, `InMemoryResultReportStore`. Cost is not enforceable, audit is not reviewable, memory does not survive a restart, and the Result Loop — advice linked to its outcome — cannot exist. This is the largest gap between what the architecture claims and what the system does.

### 3.2 There is no delivery capability at all

No CI, no deployment, no infrastructure-as-code, and a test suite of five files. The system is built and run by hand on one machine. This is survivable for one developer and fatal for parallel streams, which is why §12.2 moves it before the gate.

### 3.3 Identity is a placeholder holding up four layers

`ChatTurnIdentity` returns a hardcoded tenant and placeholder permissions. `Nexus.Platform.Identity` is 240 bytes. Layers 03 (ownership), 06 (product profiles), 07 (worker assignment, review, approval) and 09 (who did what) all need a real subject. Dataverse's row-level security is the only authorization in the system today and ADR-014 Stage 3 deletes it.

### 3.4 Development state lives in conversation — and this report is part of the problem

§8 is correct. Roadmap, decisions, progress and rationale live in markdown files and chat transcripts. This document is a 20,000-word markdown file about the need to stop putting things in 20,000-word markdown files. That is not a reason to skip it, but it is a reason to treat §39's transition strategy as urgent rather than eventual, and to design Developer V1's schema so this document can be *ingested* rather than retyped. Part 13.4 does that.

### 3.5 Product and development concepts are tangled in the Chat domain

`WorkItem`, `Adr`, `Branch`, `Snapshot`, `Session` and `Artifact` sit in `Nexus.Products.Chat.Domain` alongside `Conversation` and `Workspace`. Six of the eleven aggregates in the Chat product are not about chat — they are about *software development*, and they are the seed of Layer 07. Part 16 resolves this.

### 3.6 One ambiguous status per aggregate

Every aggregate has exactly one `Status` enum. §10 is right that this collapses lifecycle, development stage, deployment state and health into one field. No aggregate currently distinguishes them.

### 3.7 Conversation is coupled to Chat-product structure

`Conversation` carries `ConversationType` and `ConversationVisibility` and lives in the Chat product's domain. §23's principle — *conversation is universal, structure is contextual* — is not yet honoured. Part 8 addresses it.

---

## Part 4 — Target architecture

### 4.1 Twelve layers, thirty-seven projects, five repositories

Every layer gets its own project folder and its own .NET projects — that is what separation for maintainability means in .NET. Repository count is a different question, answered by release cadence rather than layer count.

Standard per-layer project shape:

```
Nexus.<Layer>.Contracts        interfaces, DTOs, events — depends on nothing
Nexus.<Layer>.Core             domain and application logic
Nexus.<Layer>.Infrastructure   persistence and external adapters
Nexus.<Layer>.Api              HTTP surface, only where called remotely
```

| # | Short name | Long description | Repository | Schema | State today |
|---|---|---|---|---|---|
| 01 | **CORE** | universal technical foundation | `Nexus.Platform` | `core` | Contracts real; identity, persistence, tools are stubs |
| 02 | **DATA** | information, documents, knowledge, retrieval | `Nexus.Platform` | `data` | Migration in progress (ADR-014) |
| 03 | **GOVERNANCE** | registries of products, technology, brand, compliance | `Nexus.Platform` | `governance` | `IProductRegistry` interface only |
| 04 | **AI** | reasoning, agents, context, models | `Nexus.Intelligence` | `ai` | Strongest layer in the system |
| 05 | **AUTOMATION** | workflow and process execution | `Nexus.Platform` | `automation` | Does not exist |
| 06 | **PRODUCT CORE** | reusable product capability and scope primitives | `Nexus.Platform` | `product_core` | Does not exist |
| 07 | **DEVELOPER** | define, plan and build software | `Nexus.Developer` | `developer` | Six aggregates exist, misplaced in Chat |
| 08 | **DELIVERY** | source, build, environments, deployment, infrastructure | `Nexus.Platform` + per-repo pipelines | `delivery` | Does not exist |
| 09 | **ASSURANCE** | prove that requirements have been satisfied | `Nexus.Platform` | `assurance` | **New in v2.1.** Does not exist |
| 10 | **OPERATIONS** | run and observe deployed systems | `Nexus.Platform` | `operations` | Does not exist |
| 11 | **EXPERIENCE** | reusable human and system interaction | `Nexus.Experience` | `experience` | Real, but trapped inside the Chat product |
| 12 | **PRODUCTS** | real business and customer problems | `Nexus.Products.<Name>` | own database each | Zero — Chat is being dissolved into EXPERIENCE |

**Short names are primary.** They appear in documentation, roadmaps, diagrams, schema names, project names and DEVELOPER data. Long descriptions appear in parentheses only where genuinely useful. The test is that every developer can immediately place a thing:

> Identity belongs to CORE. Documents belong to DATA. Trademark belongs to GOVERNANCE. Agents belong to AI. Workflow belongs to AUTOMATION. Subscriptions belong to PRODUCT CORE. Milestones belong to DEVELOPER. Git and deployment belong to DELIVERY. Acceptance belongs to ASSURANCE. Runtime health belongs to OPERATIONS. Conversation belongs to EXPERIENCE. ERP belongs to PRODUCTS.

**Why five repositories and not twelve.** Repositories are versioning and release units, not organisational ones. Twelve means twelve CI pipelines, cross-repository pull requests for a single feature, and diamond version dependencies on every contract change. These five each have a genuinely distinct release cadence:

- `Nexus.Platform` — eight product-neutral layers (01, 02, 03, 05, 06, 08, 09, 10), versioned and published together as NuGet packages, because they share a contract surface and change together.
- `Nexus.Intelligence` — already separate, already deployed independently at `/intelligence/v1`. **The assemblies keep their existing names.** The layer's short name is AI; the technical namespaces stay `Nexus.Intelligence.*`. Short architecture names are for human comprehension; stable technical names are not changed without technical value, and a rename here would buy nothing but churn across every consuming project.
- `Nexus.Experience` — consumed by every product; must ship without redeploying Platform.
- `Nexus.Developer` — its own product with its own cadence, and the one thing that must keep working while everything else changes.
- `Nexus.Products.<Name>` — one per product, because a product must be removable.

Separation is delivered by project boundaries and enforced by NetArchTest. Splitting a layer to its own repository later is a folder move, because nothing depends on the folder.

### 4.1.1 Database strategy

**One platform database, schema per layer; one database per product.**

```
NexusPlatform  ──  core · data · governance · ai · automation · product_core
                   developer · delivery · assurance · operations · experience

NexusBusinessOS ──  ERP product data
NexusVault      ──  Vault product data
NexusTrips      ──  Trips product data
```

Schema names equal the lowercased layer short name. That single rule removes an entire class of "which schema was that again" and makes a later split cheap: moving a layer to its own database becomes a connection-string change, not a rename, because nothing outside the layer references its schema by name.

Layers 01–11 share one database because they are one system with one lifecycle, one backup, one restore and one migration story, and they occasionally need a transaction spanning two of them. Products are different: product data has its own retention, its own residency obligations and its own lifecycle, and a product must be removable without a surgical delete across shared tables.

Two splits are predicted but **not decided**: `operations` (time-series, different access pattern) and `developer` (highest write volume once autonomous runs begin). `M-02-1.5` establishes the convention so both stay cheap.

**CURRENT vs TARGET.** The Stage 1b migration created `[org].[Workspace]`. `org` is not a layer schema. `M-02-1.5` moves it to `[product_core].[Workspace]` as part of establishing the convention. Until then, `org` is CURRENT and `product_core` is TARGET.


### 4.2 Dependency direction

```
                        12 PRODUCTS
                             │
        ┌────────────────────┼────────────────────┐
        ▼                    ▼                    ▼
  11 EXPERIENCE       06 PRODUCT CORE       07 DEVELOPER
        │                    │                    │
        └────────────────────┼────────────────────┘
                             ▼
         ┌───────────────────┼───────────────────┐
         ▼                   ▼                   ▼
      04 AI           05 AUTOMATION       03 GOVERNANCE
         │                   │                   │
         └───────────────────┼───────────────────┘
                             ▼
                          02 DATA
                             │
                             ▼
                          01 CORE
                             │
        ┌────────────────────┼────────────────────┐
        ▼                    ▼                    ▼
   08 DELIVERY         09 ASSURANCE        10 OPERATIONS
              (cross-cutting: everything may emit to them;
               they depend on nothing above CORE)
```

**Rules, enforced by NetArchTest:**

1. A layer may depend only on layers below it.
2. **08, 09 and 10 are cross-cutting.** Everything may emit to them; they may depend on nothing above CORE. This is what lets DEVELOPER interpret a DELIVERY build and satisfy an ASSURANCE gate without either of them knowing what DEVELOPER is.
3. **No shared kernel.** `Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never reference product types. Currently true — keep it true.
4. Products never reference each other. ERP cannot see Vault's types.
5. AI never sees product structure. It receives `ContextBundle`; `ScopeRef` is opaque to it.
6. No `if (Product == X)` anywhere. Capability packs are declared, not coded (`M-12-1.2`).

#### 4.2.1 The one apparent upward dependency, and why it is not one

DEVELOPER (07) has a conversation surface, and EXPERIENCE (11) sits above it. That looks like a violation. It is not, and the distinction is worth stating precisely because it is the pattern every future consumer will copy:

> **DEVELOPER depends on `Nexus.Experience.Contracts` only — never on `Nexus.Experience.Core`.**

Contracts assemblies depend on nothing, so depending on one creates no upward coupling. The flow inverts at runtime:

- EXPERIENCE defines `IScopeResolver` in its Contracts assembly.
- DEVELOPER *implements* that interface and registers it against its own scope kinds.
- EXPERIENCE discovers resolvers through dependency injection and calls **down** into DEVELOPER.
- EXPERIENCE never references a DEVELOPER type; DEVELOPER never references EXPERIENCE's implementation.

The same pattern serves every other consumer — a plain conversation, a machine-domain application, any future product. It is the reason the conversation engine can be one implementation rather than one per consumer, and `M-11-1.2` enforces it with an architecture test that fails if the conversation core references any layer 06, 07 or 12 assembly.

**Consequence for the roadmap:** DEVELOPER's declared `depends_on` includes 11, and that entry means *Contracts only*. Any dependency on `Nexus.Experience.Core` from DEVELOPER is a defect, not a design choice.


## Part 5 — Responsibility matrix

`O` = owns · `U` = uses · `—` = no relationship

| Capability | 01 CORE | 02 DATA | 03 GOV | 04 AI | 05 AUTO | 06 PC | 07 DEV | 08 DEL | 09 ASR | 10 OPS | 11 EXP | 12 PRD |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Identity / authentication | **O** | — | U | U | U | U | U | U | U | U | U | U |
| Tenancy / organisation | **O** | U | U | — | — | U | U | — | U | — | — | U |
| Authorization / policy | **O** | U | U | U | U | U | U | — | U | — | — | U |
| Audit trail | **O** | U | U | U | U | U | U | U | U | U | — | U |
| Secrets | **O** | — | U | U | U | — | — | U | — | U | — | — |
| Structured data access | U | **O** | U | — | — | U | U | — | U | — | — | U |
| Documents & versions | — | **O** | U | U | — | — | U | U | U | U | U | U |
| Search / embeddings / RAG | — | **O** | — | U | — | — | — | — | — | — | U | U |
| Product registry | U | — | **O** | — | — | U | U | U | U | U | — | U |
| Compliance / licence / domain | — | U | **O** | — | — | — | — | U | U | U | — | U |
| Model gateway & routing | **O** | — | U | U | — | — | — | — | — | U | — | — |
| Prompt / context assembly | — | U | — | **O** | — | — | U | — | — | — | U | U |
| Agents & tool runtime | U | — | U | **O** | U | — | U | — | U | U | — | U |
| Memory | — | U | — | **O** | — | — | — | — | — | — | — | U |
| Usage metering & cost | **O** | — | U | U | — | U | U | — | — | U | — | U |
| Workflow / jobs / schedules | — | — | — | U | **O** | U | U | U | U | U | U | U |
| Approvals / human-in-the-loop | U | — | U | — | **O** | U | U | U | U | — | U | U |
| Scope primitives (Workspace/Project) | — | — | — | — | — | **O** | U | — | — | — | U | U |
| Subscriptions / entitlements / quotas | U | — | U | — | — | **O** | — | — | — | — | — | U |
| Product membership & profiles | U | — | U | — | — | **O** | — | — | — | — | — | U |
| Development work graph | — | U | U | U | U | U | **O** | U | U | — | U | U |
| Worker orchestration & isolation | — | — | — | U | U | — | **O** | U | — | — | — | — |
| Build / test execution | — | — | — | — | — | — | U | **O** | U | U | — | — |
| Repositories / git / branches | — | — | U | — | — | — | U | **O** | — | — | — | — |
| CI/CD / artifacts / environments | — | — | U | — | — | — | U | **O** | U | U | — | — |
| Deployment & release promotion | — | — | U | — | U | — | U | **O** | U | U | — | — |
| Acceptance criteria & traceability | — | U | U | — | — | — | U | — | **O** | — | — | U |
| Verification / validation / inspection | — | U | U | U | — | — | U | U | **O** | U | — | U |
| Evidence & qualification | — | U | U | — | — | — | U | U | **O** | U | — | U |
| Quality gates | — | — | U | — | U | — | U | U | **O** | — | — | U |
| Defects & non-conformance | — | U | — | — | — | — | U | — | **O** | U | — | U |
| Observability / logs / metrics / traces | U | — | — | U | U | — | U | U | U | **O** | — | U |
| Incidents / alerts / health | — | — | U | — | U | — | U | U | U | **O** | — | U |
| Conversation engine | — | U | — | U | — | U | U | — | — | — | **O** | U |
| Chat / voice / realtime UX | — | — | — | U | — | U | U | — | — | — | **O** | U |
| Forms / commands / search UX | — | U | — | — | U | U | U | — | U | — | **O** | U |
| Domain business logic | — | U | U | U | U | U | U | — | U | U | U | **O** |

## Part 6 — Data ownership matrix

The rule, stated precisely:

> **The domain owns the structured fact. DATA owns its document representation. Never both, and never neither.**

Worked examples:

| Thing | Owner | Form |
|---|---|---|
| That milestone `M-07-1.1` exists, is blocked by `M-02-2.1`, has 7 of 10 work items complete | **DEVELOPER** | Structured rows |
| The written specification describing what `M-07-1.1` is for | **DATA** | Document, versioned, linked by ID |
| That acceptance criterion `ACC-00000042` passed, and what proved it | **ASSURANCE** | Structured rows |
| The formal test report PDF | **DATA** | Document |
| That the trademark is registered and current | **GOVERNANCE** | Structured rows |
| The trademark certificate PDF | **DATA** | Document |
| The branch the work happened on, the artifact it produced, the environment it deployed to | **DELIVERY** | Structured rows |
| That the deployed thing is healthy and served 4,000 requests | **OPERATIONS** | Time-series |

### 6.1 Ownership by layer

| Layer | Owns (structured) | Explicitly does **not** own |
|---|---|---|
| **01 CORE** | User, Credential, Session, Organisation, Tenant, Role, Permission, Policy, AuditEntry, SecretRef, UsageRecord, ModelDescriptor, ToolDescriptor | Any product concept. Any development concept. Documents. |
| **02 DATA** | Document, DocumentVersion, DocumentMetadata, KnowledgeItem, Source, Reference, Provenance, Embedding, IndexEntry, Classification, RetentionPolicy, Lineage | Structured domain facts. It stores the *document about* the fact, never the fact. |
| **03 GOVERNANCE** | Product, ProductOwnership, ProductLifecycleState, TechnologyRegistryEntry, Brand, Trademark, Domain, Certificate, ComplianceProfile, Licence, ExternalService, ConfigurationRegistryEntry | What is being built (07). How it ships (08). Whether it passed (09). How it runs (10). |
| **04 AI** | Agent, AgentCapability, Prompt, PromptVersion, ModelRoute, MemoryRecord, TurnTrace, Evaluation, Guardrail, ResultReport | Product data. Conversation storage (11). Documents (02). Provider credentials (01). |
| **05 AUTOMATION** | WorkflowDefinition, WorkflowInstance, Rule, Trigger, Schedule, Job, Queue, Approval, Escalation, WorkflowResult | The business meaning of what it runs. |
| **06 PRODUCT CORE** | **Workspace, Project, Subproject**, ProductProfile, ProductMembership, Plan, Subscription, Entitlement, FeatureFlag, Quota, ProductSetting, Preference, OnboardingState | Identity (01). Product identity in the registry sense (03). Domain data (12). Structure below Subproject (07 and other consumers). |
| **07 DEVELOPER** | ProductDevelopment, Module, Requirement, Feature, Release, Milestone, WorkItem, Task, Subtask, Dependency, ScopeDeclaration, Worker, WorkerAssignment, DevelopmentRun, BuildRecord, TestReference, Review, IntegrationRun, DevelopmentResult, ProgressState, StatusHistory | Product identity (03). Scope trunk (06). Repository and CI mechanics (08). Whether a requirement was satisfied (09). Runtime health (10). Conversation storage (11). Specification documents (02). |
| **08 DELIVERY** | Repository, GitBranch, Tag, Commit, BuildArtifact, Pipeline, PipelineRun, Environment, Deployment, InfrastructureResource, BackupRecord, RestorePoint | The *meaning* of a build (07 interprets it). Whether it satisfied a requirement (09). Runtime health (10). |
| **09 ASSURANCE** | QualityPlan, VerificationPlan, ValidationPlan, TestPlan, TestCase, InspectionPlan, InspectionCharacteristic, AcceptanceCriterion, VerificationMethod, ValidationMethod, VerificationRun, ValidationRun, InspectionRun, Evidence, Defect, Deviation, NonConformance, CorrectiveAction, QualityGate, QualificationResult, TraceabilityLink | What needs testing (07). Executing pipelines (08). Runtime health (10). The test report *document* (02). The Requirement itself (07). |
| **10 OPERATIONS** | LogStream, Metric, Trace, HealthCheck, Incident, Alert, PerformanceRecord, CapacityRecord, CostRecord, FeatureFlagState | Anything durable about what was built, why, or whether it was verified. |
| **11 EXPERIENCE** | Conversation, Message, Participant, Attachment, ConversationSession, MemoryReference, KnowledgeReference, ToolUsage, ResultReference, ScopeKindBinding, UIPreference, CommandDefinition, NotificationDelivery | **Contextual structure of any kind.** Workspace/Project (06). Milestone/WorkItem (07). Product domain data (12). Documents (02). Model access (01/04). |
| **12 PRODUCTS** | Everything domain-specific to a product | Anything a lower layer owns. Another product's types. |


## Part 7 — The twelve layers

### 7.1 Layer 01 — CORE

**Purpose.** Provide the universal technical foundation used by Nexus itself and by every product, so that no product ever re-implements identity, tenancy, authorization, audit, secrets or model access.

**Why it exists.** Without it, every product invents its own user model and its own permission check, and there is no way to answer "who did this" across the system. It is the layer that makes "one Nexus identity, many product contexts" possible.

**Owns:** identity, authentication, sessions, organisations, tenancy, roles, permissions, policy evaluation, audit, secrets resolution, notification transport, the API foundation, the event foundation, model gateway and routing, tool gateway, usage metering.

**Does NOT own:** any product concept, any development concept, any document, any workflow definition, any domain rule. Platform must remain product-neutral — if a Platform type mentions `Workspace` or `Milestone`, the boundary has been broken.

**Sub-layers:**

| Sub-layer | Project | State |
|---|---|---|
| Contracts | `Nexus.Platform.Contracts` | Real |
| Identity & tenancy | `Nexus.Platform.Identity` | **Stub — 240 bytes** |
| Authorization & policy | *(does not exist)* | Missing |
| Governance primitives (audit, quota, usage) | `Nexus.Platform.Core/Governance` | In-memory only |
| Model access & routing | `Nexus.Platform.Core/Models` + `Providers.*` | Real for OpenAI, stub for Anthropic |
| Tools | `Nexus.Platform.Tools` | **Stub — 231 bytes** |
| Persistence primitives | `Nexus.Platform.Persistence` | **Stub — 308 bytes** |
| Secrets | `Contracts/Secrets/ISecretResolver` | Interface only |

**Inputs:** configuration, secrets from the host environment, provider credentials.
**Outputs:** `ResolvedIdentity`, `QuotaVerdict`, `ModelInvocationResult`, `ToolResult`, `AuditEntry`.
**Events produced:** `IdentityResolved`, `AuthorizationDenied`, `QuotaExceeded`, `ModelInvoked`, `AuditRecorded`.
**Events consumed:** none. Platform is the bottom.
**May be depended on by:** everything.
**May depend on:** nothing above it.

**Security responsibilities:** it *is* the security layer. Credential storage, token issuance and validation, permission evaluation, tenant isolation enforcement, secret resolution without secret exposure. Provider credentials never leave Platform — Intelligence asks Platform to invoke a model and never sees a key.

**Minimum V1 (GATE A scope):**
- Real identity: user, credential, session, sign-in, token issuance and validation
- Organisation and tenant with enforced isolation
- Role and permission with a working `IAuthorizationService`
- Durable `IAuditLog` (replacing `ConsoleAuditLog`)
- Durable `IUsageMeter` (replacing `InMemoryUsageMeter`)
- `ISecretResolver` backed by real configuration/key vault
- `Nexus.Platform.Persistence` made real enough to host the above

**Future (post-gate):** federated / SSO identity, fine-grained ABAC policies, notification transport, event bus, multi-region tenancy, Anthropic and further providers, tool gateway implementation.

---

### 7.2 Layer 02 — DATA

**Purpose.** Govern information, documents and reusable knowledge across Nexus, and provide the structured data access foundation products build on.

**Why it exists.** Two distinct problems, deliberately in one layer. First, every product needs disciplined persistence — schema, migration, transaction, query. Second, Nexus's central claim is that it *remembers* — architecture documents, specifications, ADRs, manuals, test reports, release notes — and can retrieve the right piece at the right moment. Separating these would mean two teams owning "where does information live."

**Owns:** structured data access patterns and migration discipline; Document, DocumentVersion, metadata; KnowledgeItem, Source, Reference, Provenance; search, indexing, embeddings, vector retrieval; import/export and synchronisation; classification, retention, lineage.

**Does NOT own:** the structured facts themselves. A milestone's completion percentage is Developer's. A product's owner is Governance's. Data & Knowledge owns the *document about* them and the ability to retrieve it.

**All documentation belongs here** — architecture documents, standards, specifications, ADRs, manuals, roadmaps-as-documents, test reports, release notes, compliance evidence, operational documentation, developer documentation. Including this report.

**Sub-layers:** Persistence foundation · Document store · Knowledge store · Retrieval (index, embeddings, vector) · Governance (classification, retention, lineage) · Import / export / sync.

**Inputs:** documents from any layer; structured records to index; retrieval queries from Intelligence.
**Outputs:** `Document`, `DocumentVersion`, `KnowledgeItem`, ranked retrieval results with provenance.
**Events produced:** `DocumentCreated`, `DocumentVersioned`, `KnowledgeApproved`, `IndexUpdated`.
**Events consumed:** any layer's "this produced a document" event.
**May depend on:** 01 only.

**Minimum V1 (GATE A scope) — this is the ADR-014 work:**
- Azure SQL as the single persistence backend; Dataverse removed entirely (ADR-014 Stages 1b, 2a, 2b, 2c, 3)
- EF Core code-first discipline: domain class → `IEntityTypeConfiguration` → migration → DDL
- The `Id`/`Seq`/`Ref` pattern (ADR-014 Rule 4) and SQL schemas replacing prefixes (Rule 6)
- Document entity with versioning, sufficient to hold this report and the numbered doc set
- Documents linkable by ID to structured records in other layers

**Explicitly NOT in V1:** embeddings, vector retrieval, RAG, lineage, retention policies, classification. Keyword ranking (which already exists in Intelligence) is sufficient until there is enough content for retrieval quality to be measurable.

**Future:** embeddings and vector search, semantic retrieval, document sync from external sources, full lineage and retention governance.

---

### 7.3 Layer 03 — GOVERNANCE

**Purpose.** Govern Nexus and every product through structured registries of ownership, identity, compliance and lifecycle — so that "what products exist, who owns them, what state are they in, what obligations do they carry" has one authoritative answer.

**Why it exists.** Once there is more than one product, the questions "which domains do we own", "which licences apply", "who is accountable for Vault", "what is registered and what is shadow IT" have no home. Governance is that home. It is deliberately separate from Developer: Governance says a product *exists and is ours*; Developer says what is *being built in it*.

**Owns:** Product registry, product identity, ownership, classification, lifecycle registration; Technology registry; Brand, trademark, domain and DNS references, certificate lifecycle; Compliance registry, privacy requirements, data residency; Licence registry; External service registry; Configuration registry; standards governance.

**Does NOT own:** what is being built (07), how it ships (08), how it runs (09), or any document (02 owns the document; Governance owns the fact it is compliant).

**Sub-layers:** Product registry · Technology registry · Brand & domain registry · Compliance registry · Licence & external service registry · Configuration registry.

**Existing seed:** `Nexus.Platform.Contracts/Identity/IProductRegistry.cs` already exists — the interface is in the right conceptual place but currently sits in Platform. It should move to Governance when Governance is built.

**May depend on:** 01, 02.

**Minimum V1 — post-gate, with one exception.** Governance is not a gate blocker *except* for `Product` identity: Developer's `ProductDevelopment` needs a `ProductId` to hang off. The gate needs a minimal `Product` record — id, name, owner, classification, lifecycle state — and nothing else. Full Governance follows in Stream B.

**Future:** trademark and domain lifecycle tracking, certificate expiry automation, compliance evidence linkage, drift detection against the technology registry.

---

### 7.4 Layer 04 — AI

**Purpose.** Provide reusable AI reasoning, model access, agents and intelligent capability to every product, without any product's structure leaking into it.

**Why it exists.** So that adding AI to a new product is composition, not reimplementation — and so that reasoning quality can be improved once, centrally, and measured.

**Owns:** AI gateway usage, provider routing decisions, model registry and router, prompt management, context engine, memory engine, agent registry and runtime, tool registry and runtime, RAG orchestration, planning and reasoning, evaluations, guardrails, result validation, AI observability, per-turn usage and cost attribution.

**Does NOT own:** product data, conversation storage, documents, provider credentials (Platform holds those), or any knowledge of what a `Workspace` is.

**The seam, which already works and must not be broken:** products flatten their entities into `ContextItem { Id, Kind, Body, Trust, OccurredAt, Author, RelevanceHint }` and hand over a `ContextBundle`. `ScopeRef` is opaque to Intelligence. Intelligence returns an `IntelligenceTurnResponse` with citations and a `DecisionTrace`. This is the single best-designed boundary in the system.

**Sub-layers:** Contracts · Turn pipeline (intent → policy → context → agent → model → prompt → tool loop → compose) · Context (ranking, prompt assembly) · Memory · Agents · Evaluation & guardrails.

**Inputs:** `IntelligenceTurnRequest` with `ContextBundle`, `TurnConstraints`, `ActorRef`, `ScopeRef`.
**Outputs:** `IntelligenceTurnResponse` — reply, citations, plan, proposed actions, decision trace, usage summary.
**Events produced:** `TurnCompleted`, `PlanProposed`, `ActionProposed`, `GuardrailTriggered`, `EvaluationRecorded`.
**May depend on:** 01, 02.

**Minimum V1 (GATE A scope):**
- Durable `ITurnTraceStore` and `IResultReportStore` — currently in-memory, and without them the Result Loop cannot exist and Developer cannot explain why it made a call
- Durable `IMemoryStore`
- Citations verified end to end through the frontend (F3 built the UI; it has never been proven against a live model because of the OpenAI credit block)
- `DeveloperAgent` made real enough to reason about the work graph

**Explicitly NOT in V1:** multi-provider routing beyond OpenAI, embeddings, RAG, evaluation harness, guardrail framework, tool runtime.

**Future:** Anthropic / Google / OpenRouter / DeepSeek / Z.ai / local models; model routing by cost and latency class; prompt versioning and A/B evaluation; a real tool registry and runtime; evaluation-driven improvement.

---

### 7.5 Layer 05 — AUTOMATION

**Purpose.** Execute reliable, repeatable processes — with or without AI involvement.

**Why it exists.** *Intelligence reasons; Automation executes.* Without this separation, every scheduled job and every approval chain gets built into whatever product needed it first, and reliability becomes a property of individual features rather than of the system. It also gives AI-proposed actions somewhere deterministic to land.

**Owns:** workflow definitions and instances, rules, triggers, schedules, conditions, state machines, queues, jobs, retries, approvals, human-in-the-loop gates, escalations, event handlers, process orchestration, workflow results.

**Does NOT own:** the business meaning of what it runs. A workflow that approves a purchase order does not know what a purchase order is.

**Sub-layers:** Definition · Execution engine · Scheduling & triggers · Queue & job runtime · Approvals & human-in-the-loop · Results.

**May depend on:** 01, 02, 03, 04.

**Minimum V1 — post-gate.** Not a gate blocker. Developer V1a coordinates work through explicit state transitions, not a workflow engine. When Developer V1b begins dispatching workers autonomously, Automation becomes the right home for retry, escalation and approval — that is the natural trigger for building it.

**Future:** full state-machine definitions, durable queues, compensation, saga patterns.

---

### 7.6 Layer 06 — PRODUCT CORE

**Purpose.** Provide reusable product-level capability — scope, membership, subscriptions, entitlements, quotas, settings, onboarding — so no product rebuilds them.

**The distinction from Platform is the point.** Platform owns *who you are* — one Nexus identity. This layer owns *who you are within a product* — your Vault profile, your Developer profile, your Trips traveller profile.

**Decision 6 consequence — this layer now owns the scope trunk.** With chat becoming a layer, `Workspace` and `Project` can no longer live in a product. They recur across every consumer: Developer needs Workspace → Project → Subproject → Milestone → Feature → WorkItem → Task; a plain conversation needs Workspace → Project; machine work needs a different hierarchy entirely. So Layer 06 owns the shared trunk and consumers extend it.

```
Layer 06 owns:     Workspace → Project → Subproject
Layer 07 extends:                        → Milestone → Feature → WorkItem → Task → Subtask
A plain chat uses:  Workspace → Project        (and stops)
Machine work:       registers Machine → Assembly → Operation  (its own trunk entirely)
```

The mechanism is `ScopeKindRegistration` (`M-06-1.2`): a consumer declares its own scope kinds without modifying Layer 06, and an architecture test forbids any branch on product identity inside this layer.

**Owns:** Workspace, Project, Subproject, ProductProfile, ProductMembership, Plan, Subscription, Entitlement, FeatureFlag, Quota, ProductSetting, Preference, OnboardingState.

**Does NOT own:** identity (01), product identity in the registry sense (03), domain data (11), or development structure above Project (07).

**Minimum V1 — scope primitives are P1, everything else is P3.** The scope trunk must exist before Developer's work graph and before the conversation layer can resolve anything, so `F-06-1` is gate-critical. Membership, subscriptions, entitlements, quotas, settings and onboarding wait for a second product and a second user.


### 7.7 Layer 07 — DEVELOPER

**Purpose.** Define, plan, build, test, review and coordinate software development — and become the structured system of record for development state, replacing chat transcripts and markdown.

**Why it exists.** Two reasons. Immediately: development state currently lives in conversation and is lost between sessions. Strategically: Nexus cannot build Nexus, and cannot coordinate simultaneous workers, without a machine-readable model of what is being built, what depends on what, and what is safe to run in parallel.

#### Decision 2 applied — Developer is one full layer

Developer is a single layer, not a split between runtime and product. It is the layer used to develop the products *and* the other layers, including itself. It carries product history, features, current upgrades and development state, and it has its own conversation surface so that everything needed to develop a product is reachable from one place.

Two constraints keep that from becoming a dumping ground:

1. **Developer consumes layers; it does not absorb them.** It does not implement chat — it registers its scope hierarchy with Layer 06 and implements the Layer 10 scope resolver. It does not run CI — Layer 08 produces a build and Developer interprets whether it satisfies a work item. It does not own documents — a Milestone links to a Layer 02 `Document` by id.
2. **Developer references product identity; it does not own it.** Layer 03 says a product exists and who owns it. Developer says what is being built in it.

Its own repository, `Nexus.Developer`, seven projects, the `dev` schema — and the first candidate to split to its own database at P3, because once autonomous runs begin it will carry the highest write volume of any layer.


**Owns:** ProductDevelopment, Module, Feature, Requirement, Release, Milestone, WorkItem, Task, Subtask, Dependency, Worker, WorkerAssignment, DevelopmentRun, BuildRecord, TestRun, Review, IntegrationRun, DevelopmentResult, ProgressState.

**Does NOT own:** product identity (03 — Developer references a `ProductId`), repository and CI mechanics (08 — Developer *interprets* a build result, Delivery *produces* it), runtime health (09), specification documents (02 — Developer's `Milestone` links to a Document by ID).

**Sub-layers:**

| Sub-layer | V1a | V1b | Post-gate |
|---|---|---|---|
| Work graph (Product → Module → Milestone → WorkItem → Task) | ● | | |
| Dependency graph & parallel-safe analysis | ● | | |
| Worker manager & assignment | ● | | |
| Branch / worktree coordination | ● | | |
| Build & test result capture | ● | | |
| Review & controlled integration | ● | | |
| Derived progress & state | ● | | |
| Development orchestrator (autonomous dispatch) | | ● | |
| Model assignment & per-run cost | | ● | |
| Requirements & releases | | | ● |
| Product designer / schema designer / API & UI definition | | | ● |
| Capability packs & technology profiles | | | ● |
| Developer chat & dashboards | | | ● |

**Inputs:** work definitions (initially imported from this document), git state from Delivery, build and test results from CI, human review decisions.
**Outputs:** parallel-safe execution plans, worker assignments, run records, progress state, integration decisions.
**Events produced:** `WorkItemReady`, `WorkerAssigned`, `RunStarted`, `RunCompleted`, `BuildRecorded`, `ReviewRequested`, `IntegrationCompleted`.
**Events consumed:** `PipelineCompleted` (08), `TurnCompleted` (04).
**May depend on:** 01, 02, 03, 04, 05, 08.

Full V1 scope, milestones and tasks are in Part 13.

---

### 7.8 Layer 08 — DELIVERY

**Purpose.** Safely move source into reproducible running systems, and preserve everything required to reconstruct them.

**Why it exists.** Right now the answer to "how does code become a running system" is "Durai builds it on his laptop." That does not survive one parallel worker, let alone three. And the answer to "what happens if the machine is lost" was tested involuntarily on 2026-08-20 and the answer was *nearly everything*.

**Owns:** git providers, repositories, branch policies, tags, release branches; build infrastructure, CI/CD, artifact registry; environment management, infrastructure provisioning, cloud, servers, containers, databases, storage, networking; domains and DNS deployment; deployment and release promotion; backup, restore, disaster recovery; infrastructure-as-code; deployment credentials.

**Does NOT own:** the *meaning* of a build — Developer interprets whether a green build satisfies a work item. Runtime health after deployment (09).

**Sub-layers:** Source control & branch policy · Build & CI · Artifact registry · Environment management · Deployment & promotion · Infrastructure-as-code · Backup & disaster recovery.

**May depend on:** 01, 03. Cross-cutting otherwise.

**Minimum V1 — MANDATORY BEFORE THE GATE, contrary to §6.** See §12.2. Scope is deliberately tiny:
- GitHub Actions workflow per repository: restore, build, test, publish results
- Branch protection on `main` requiring a green build
- Architecture tests (NetArchTest) wired into CI as a hard gate
- Build and test results emitted in a form Developer can ingest
- **Antivirus exclusion for `C:\Personal\` verified** and a documented backup of all three repositories

**Explicitly NOT in V1:** artifact registry, environments, deployment pipelines, infrastructure-as-code, cloud provisioning, disaster recovery automation.

**Future:** all of the above, plus release promotion and drift detection.

---

### 7.9 Layer 09 — ASSURANCE

**New in v2.1.** The layer that makes "done" an evidenced claim rather than an opinion.

**Purpose.** Verify and validate that what Nexus designs, builds, deploys and operates satisfies its requirements, quality standards, safety constraints and acceptance criteria.

**Why it exists.** A green build proves code compiles and some assertions held. It does not prove a requirement was met. Nothing in v2.0 closed that gap, so completion meant "someone believed it was complete." Given that DEVELOPER will eventually dispatch its own workers, an unevidenced definition of done is not a documentation weakness — it is the thing that lets an autonomous system convince itself it succeeded.

**Deliberately broader than software testing.** Nexus will build machines, manufacturing systems and business processes where inspection, measurement and validation matter more than unit tests. A boring machine is qualified by measured characteristics against tolerances; an ERP process by user validation; an AI answer by scored evaluation. One layer, one traceability model, different methods.

**The traceability model — every link is a row:**

```
Requirement            (DEVELOPER owns)
      ↓
AcceptanceCriterion    ─┐
      ↓                 │
Verification /          │
Validation Method       │  ASSURANCE owns
      ↓                 │
Test / Inspection /     │
Evaluation              │
      ↓                 │
Evidence                │
      ↓                 │
Pass / Fail            ─┘
      ↓
Release Qualification
```

A requirement with no acceptance criterion, or a criterion with no method, is **reportable as a traceability gap** rather than silently absent. That reporting is the whole point — it converts an unknown into a known.

**Owns:** QualityPlan, VerificationPlan, ValidationPlan, TestPlan, TestCase, InspectionPlan, InspectionCharacteristic, AcceptanceCriterion, VerificationMethod, ValidationMethod, VerificationRun, ValidationRun, InspectionRun, Evidence, Defect, Deviation, NonConformance, CorrectiveAction, QualityGate, QualificationResult, TraceabilityLink.

**Does NOT own:** what needs testing and which work item it belongs to (DEVELOPER); executing pipelines (DELIVERY); runtime health (OPERATIONS); the formal test report *document* (DATA owns the document, ASSURANCE owns the result); the Requirement itself (DEVELOPER owns Requirement, ASSURANCE owns its AcceptanceCriterion).

**Assurance profiles.** Different product types activate different mandatory methods — software, AI, ERP, machine, consumer. A profile decides which methods are *mandatory*, not which are *possible*, and selecting one is a declaration rather than a code change (`M-09-7.1`).

**Minimum V1 — P1, gate-critical, and deliberately small.** Acceptance criteria, verification methods, evidence, verdict, and one quality gate (`F-09-1`). That is enough to make Definition of Done enforceable. Plans, specifications, inspection characteristics, AI evaluation and profiles are P3.

**The safety carve-out.** A criterion marked safety-critical cannot be waived by the ordinary deviation path — only by a named human with recorded authority — and **no agent may create, modify or waive one** (`M-09-7.2`). This is the single hardest constraint in the architecture and it exists because Machine Automation is on the roadmap.


### 7.10 Layer 10 — OPERATIONS

**Purpose.** Keep running systems healthy, secure, observable and recoverable.

**Why it exists.** Nothing is deployed yet, so nothing is operated yet. The layer exists in this architecture to reserve the space, and to stop observability being retrofitted into eleven places once something is finally running.

**Owns:** observability, logs, metrics, tracing, health, performance, diagnostics; incidents, alerts; cost monitoring, capacity; deployment health, feature flags; backup monitoring, recovery, disaster recovery; security monitoring; operational results.

**Does NOT own:** anything durable about what was built or why (07), or how it was shipped (08).

**Sub-layers:** Observability · Health & diagnostics · Incident & alerting · Cost & capacity · Security monitoring · Recovery.

**May depend on:** 01. Cross-cutting otherwise.

**Minimum V1 — post-gate, with one carve-out.** Structured logging with correlation IDs should be added during Platform V1 rather than retrofitted, because retrofitting correlation is disproportionately expensive. Everything else waits for something to actually be deployed.

**Future:** full observability stack, incident management, cost monitoring, feature flags, disaster recovery drills.

---

### 7.11 Layer 11 — EXPERIENCE

**Decision 6 applied — this is the largest structural change in version 2.0. Chat is no longer a product. It is a layer.**

**Purpose.** Provide reusable human and system interaction capability — above all the conversation engine — so every layer and product gets chat without rebuilding it, and without conversation becoming the architecture.

**Why this is right.** The brief's own §23 states the principle: *conversation is universal, structure is contextual.* Version 1.0 honoured half of it — `Conversation` still sat in the Chat product carrying `ConversationType` and `ConversationVisibility`. Making chat a backend layer completes it. Developer, a plain conversation and machine work each need completely different structure around the same conversation mechanics, and there is no version of that which works if conversation belongs to one product.

**The universal core, and what is deliberately excluded:**

| In the core | Excluded — belongs to a consumer |
|---|---|
| Conversation, Message, Participant, Attachment, ConversationSession | Workspace, Project (Layer 06) |
| MemoryReference, KnowledgeReference | Milestone, Feature, WorkItem, Task (Layer 07) |
| ToolUsage, ResultReference | Adr, Build, Release, Repository, Worker |
| ScopeKindBinding | Anything product-specific |

Milestone `M-12-1.2` makes that table an architecture test rather than a discipline.

**The mechanism — `IScopeResolver`.** This is the whole design in one interface:

1. A conversation carries an opaque `ScopeRef` and nothing else about its context.
2. A consuming layer registers a scope kind and a resolver.
3. The engine calls the resolver, receives a `ContextBundle`, and passes it through **untouched**.
4. Intelligence receives flattened `ContextItem`s and never learns what a `Milestone` is.

Developer's resolver maps a milestone's outcome to `Kind = Objective`, its blocking dependencies to `Kind = Constraint`, and its development results to `Kind = Outcome`. The reference resolver for plain conversation covers Workspace and Project and doubles as the worked example. Two consumers with different hierarchies are served by one engine simultaneously — that is `M-11-2.1`'s acceptance criterion.

**Owns:** Conversation, Message, Participant, Attachment, ConversationSession, MemoryReference, KnowledgeReference, ToolUsage, ResultReference, ScopeKindBinding, UIPreference, CommandDefinition, NotificationDelivery.

**Does NOT own:** contextual structure of any kind, product domain data, documents, model access.

**What happens to `Nexus.Web` (renamed `Nexus.Experience`, 2026-08-24).** Its conversation implementation becomes this layer; `Workspace` and `Project` move to Layer 06; `WorkItem` moves to Layer 07; `Branch`, `Snapshot` and build `Artifact` move to Layer 08; `Adr` and `Knowledge` move to Layer 02. The React client becomes the shared component set. The F0–F4 work is not wasted — `CitationsPanel`, `ChatTelemetryContext` and the thread components survive intact and become reusable, which is what they should have been.

**Minimum V1 — P2, and deliberately NOT in GATE A.** Conversation core, scope resolution and the reusable chat surface. DEVELOPER V1a runs on an API and a work-graph view; it needs no conversation surface to coordinate three workers, and removing EXPERIENCE from GATE A takes roughly three weeks off the critical path. Commands, search, forms, approvals, notification centre and the component system are P3; voice and realtime are P4.

### 7.12 Layer 12 — PRODUCTS

**Purpose.** Solve actual user, business and domain problems by composing Nexus capability.

```
Product
├── Product Core            domain-specific identity, context, settings, state
├── Domain Modules          the actual business capability
└── Capability Integrations how this product consumes layers 01-10
```

**Decision 4 applied — two categories with different scheduling.**

| Category | Scheduling | Products |
|---|---|---|
| **Consumer public products** | **Planned.** P4, sequenced, with dependencies on platform capability. | Vault, Trips, Career, Education, Truck, Games (P5) |
| **Internal business systems** | **On demand.** Built when the business needs them. Their phase means *eligible from*, not *scheduled for*. | Business OS/ERP, CRM & Field Data, Engine Works, Retreads, Transport, Knowledge Systems, Internal Tools, Machine Development |
| **Machine systems** | **Gated.** Own safety architecture and human sign-off required before any milestone is written. | Machine Automation (P5) |

This is why version 1.0's "Roadmap B — Business Systems" is gone. Business systems are not a stream with a schedule; they are a pull queue. Business OS/ERP remains foundational *within* that queue — if any business system is pulled in, it comes first.

**Capability composition, not conditionals.** Products declare capability packs:

```
Vault          = Web + Mobile + Desktop + Documents + AI + Security + Offline Sync
Trips          = Web + Mobile + Marketplace + Booking + Maps + Payments + AI
Games          = Game Engine + Gameplay + Assets + Audio + Save + Multiplayer
Machine System = Hardware I/O + Motion + Measurement + Control + Safety + Industrial UI
```

`M-12-1.2` makes `if (Product == Vault)` a build failure across the whole solution rather than a code-review comment. Games is deliberately last among consumer products — its capability packs resemble nothing else, which makes it the hardest test of whether composition is genuinely general.

**One database per product**, because product data has its own retention, residency and lifecycle, and a product must be removable.

**Product state is eight dimensions, not one status field** (`M-12-1.3`): ProductLifecycleState, DevelopmentStage, CurrentRelease, CurrentProductionRelease, DevelopmentHealth, DeploymentState, OperationalHealth, ComplianceState — each marked derived or manual, and every derived one computed rather than entered.


## Part 8 — Conversation architecture

**Permanent principle:** *Conversation is universal. Structure is contextual.*

Chat is a layer (7.10). This part specifies how one engine serves consumers whose structure has nothing in common.

### 8.1 The three participants

| Party | Knows about | Deliberately ignorant of |
|---|---|---|
| **Layer 10 — the engine** | Conversation, Message, Participant, Attachment, ScopeRef | What any `ScopeRef` points at |
| **The consumer** (Developer, a product, machine work) | Its own hierarchy in full | How conversation is stored or rendered |
| **Layer 04 — AI** | `ContextItem` and `ContextBundle` | Both of the above |

Each of the three can change without the other two recompiling. That is the test of whether the seam is real.

### 8.2 The flow

```
   consumer                    Layer 10                    Layer 04
   ────────                    ────────                    ────────
   registers ScopeKind ──────► ScopeKindBinding
   + IScopeResolver

                               conversation opened
                               with opaque ScopeRef
                                      │
   resolve(ScopeRef) ◄────────────────┘
        │
        ▼
   flatten own entities
   to ContextItem
        │
        └── ContextBundle ────► passed through ──────────► ranks, assembles,
                                  untouched                 invokes, cites
                                                                  │
   citations resolve  ◄──────── IntelligenceTurnResponse ◄────────┘
   back through the
   consumer's own ids
```

The engine never inspects the bundle. Intelligence never inspects the scope. The consumer never touches storage or the model.

### 8.3 Worked example — three consumers, one engine

| Consumer | Registered hierarchy | A conversation scoped to… | …yields ContextItems |
|---|---|---|---|
| **Developer** | Workspace → Project → Subproject → Milestone → Feature → WorkItem → Task | a Milestone | outcome as `Objective`, blocking dependencies as `Constraint`, linked spec as `Fact`, results as `Outcome` |
| **Plain conversation** | Workspace → Project | a Project | project description as `Objective`, recent messages as `Fact` |
| **Machine development** | Machine → Assembly → Operation | an Operation | tolerance as `Constraint`, measurement history as `Fact`, safety limits as `Constraint` at `Authoritative` trust |

Three completely different structures. One conversation engine. Zero shared types between them.

### 8.4 The standing constraint

From the brief's §22: **chat must not become the architecture for Nexus.** When something can be modelled as structure or as conversation, model it as structure and let conversation reference it. A milestone's dependency is a `Dependency` row that a conversation can discuss — never a sentence in a transcript that a conversation has to re-derive.

This is enforced, not merely stated: `M-11-1.2` fails the build if the conversation core references any Layer 06, 07 or 11 assembly.


## Part 9 — State, progress, release and environment models

### 9.1 Product state is not one field

Replacing the single `Status` enum (§3.6, §10):

| Dimension | Owner | Example values | Derived? |
|---|---|---|---|
| `ProductLifecycleState` | 03 GOVERNANCE | Proposed, Active, Sunsetting, Retired | Manual |
| `DevelopmentStage` | 07 DEVELOPER | Not Started, In Design, In Development, Stabilising, Complete | **Derived** from milestone states |
| `CurrentRelease` | 07 DEVELOPER | Release ref | Manual |
| `CurrentProductionRelease` | 08 DELIVERY | Release ref actually deployed to Production | **Derived** from deployment records |
| `DevelopmentHealth` | 07 DEVELOPER | Healthy, At Risk, Blocked | **Derived** from blocked work items and failing builds |
| `DeploymentState` | 08 DELIVERY | Not Deployed, Deploying, Deployed, Failed, Rolled Back | **Derived** |
| `OperationalHealth` | 10 OPERATIONS | Healthy, Degraded, Down | **Derived** from health checks |
| `ComplianceState` | 03 GOVERNANCE | Compliant, Exception, Non-Compliant | Manual with evidence links |

### 9.2 Progress — derived, with an honesty rule

Derived from structured work, per §11:

```
Milestone
├── 10 Work Items
├── 7 Complete → 2 In Progress → 1 Blocked
└── Derived completion ≈ 70%
```

| Value | Derived or manual |
|---|---|
| Progress % | **Derived** — completed children ÷ total children, weighted by estimate where present |
| Status | **Derived** from children, overridable with a recorded reason |
| Blocked | **Derived** — any blocking dependency unmet |
| Risk | Manual |
| Dependencies | Manual (declared), validated automatically |
| Target date | Manual |
| Actual completion | **Derived** from the integration record |

**The honesty rule.** Derived progress on an *incomplete* work breakdown is worse than no progress at all, because it looks authoritative. A milestone with three declared work items out of an eventual twenty will report 33% and mean nothing. Therefore: **progress is derived only where the parent is explicitly marked `BreakdownComplete`.** Until then it reports "not estimable." This one flag is the difference between a progress model that earns trust and one that quietly lies.

### 9.3 Release maturity and environment are orthogonal

Two independent axes. Never use Dev/Test/Prod as maturity terminology.

**Release maturity:** Idea → Prototype → Pre-Alpha → Alpha → Beta *(Closed / Open)* → Release Candidate → General Availability → Maintenance → Deprecated → End of Life. *Early Access* may overlay Beta or RC.

**Environments:** Local / Sandbox → Development → Integration → Staging → Pre-Production → Production.

A Beta release can run in Production. A GA release still runs in Staging during promotion. Products may define their own Release Lifecycle Profile, Environment Profile and Deployment Profile.

---

## Part 10 — Simultaneous development architecture

This is what GATE A exists to deliver, so it deserves precision.

### 10.1 Worker model

A **Worker** is an isolated execution context capable of taking one assignment at a time: a human, a Claude Code session, or (in V1b) an autonomously dispatched agent. Workers have a capability profile — which repositories, which languages, which risk level of change they may make.

A **WorkerAssignment** binds one Worker to one WorkItem, one branch and one worktree, for one DevelopmentRun.

### 10.2 Parallel-safe analysis — the core algorithm

Two work items may run simultaneously if **all** hold:

1. **No dependency path** between them in the dependency graph (transitively).
2. **No file-scope overlap.** Each work item declares the projects and, where known, files it will touch. Overlap means sequential.
3. **No shared schema mutation.** Two work items adding EF migrations to the same `DbContext` conflict on the model snapshot even when they touch different tables. This is the single most common false-parallel case in this codebase and must be an explicit rule.
4. **No contract mutation on a shared boundary.** Two work items both changing `Nexus.Platform.Contracts` conflict semantically even if textually separable.
5. **Compatible risk levels.** Two high-risk architectural changes do not run together regardless of overlap.

Output classification, per §32: `Can run now` · `Can run together` · `Blocked` · `Waiting for dependency` · `High conflict risk` · `Must be sequential`.

### 10.3 Git and worktree strategy

```
main                    protected, green-build-required, no direct commits
└── integration/<ms>    per-milestone integration branch
    ├── work/<id>-a     worker A, own worktree
    ├── work/<id>-b     worker B, own worktree
    └── work/<id>-c     worker C, own worktree
```

- One worktree per worker: `git worktree add ../wt-<workitem-id> work/<id>`
- A worker never touches another worker's worktree, and never checks out `main`
- Every branch ends in a build and a test run recorded against the work item
- Integration is explicit: worker → review → integration branch → green build → `main`
- **Push at every stage boundary.** Not every milestone. `feat/azure-sql` once held two stages' work with exactly one copy on earth, and 2026-08-20 nearly proved the cost.

**Windows-specific caution learned from the 2026-08-20 recovery:** worktrees under a folder an agent has as its working directory cannot be renamed while that agent runs. Worktrees go in a sibling directory, never nested inside the repository.

### 10.4 The §33 acceptance test, made concrete

Developer V1 is not accepted until this runs and is recorded:

| Step | Evidence required |
|---|---|
| Three work items declared, dependency-analysed, classified `Can run together` | Analysis output stored against each work item |
| Feature A → Worker A → Worktree A, simultaneously with B and C | Three `WorkerAssignment` rows with overlapping timestamps and distinct worktree paths |
| Independent build per worker | Three `BuildRecord` rows, distinct branches |
| Independent test per worker | Three `TestRun` rows |
| Failure isolation | Deliberately fail worker B; A and C complete unaffected; B's failure recorded against B's work item only |
| Result capture | Three `DevelopmentResult` rows, each linked to its work item |
| Review | Three `Review` rows with a human decision |
| Controlled integration | Three `IntegrationRun` rows, sequential merges into the integration branch, each with a green build |
| Progress state | Parent milestone shows derived progress reflecting exactly the completed subset |

**Note that none of this requires Developer to *dispatch* the workers.** That is why V1a satisfies it.

---

## Part 11 — Documentation and roadmap source of truth (§39)

Three phases, and the transition is the interesting part:

**Phase 1 — now (this report).** Structured markdown in `NexusAI\docs\`, one canonical numbered set, one global ADR sequence. Already achieved.

**Phase 2 — machine-readable roadmap.** Alongside this document, `nexus-roadmap.yaml` expressing milestones, work items, dependencies and file scopes in the exact shape of Developer's schema. Hand-maintained, but *parseable* and mechanically verified. This is the bridge. It exists now, and it is produced as part of Developer V1a milestone `M-07-1.1`, not after it — because it doubles as the schema's first real test case.

**Phase 3 — Developer as system of record.** `roadmap.yaml` is imported into Developer's tables. Developer becomes authoritative for structured development state. Documents move to Layer 02 and link to Developer records by ID. This document becomes a `Document` in Data & Knowledge, versioned, referenced by the milestones it describes.

**The rule that keeps them honest:** after Phase 3, a structured fact has exactly one home. If completion percentage is in Developer, it is not also in markdown. Documents describe and explain; they do not duplicate state.

---

## Part 12 — The two gates

**Changed in v2.2.** One gate was too heavy to stand in front of real business value. There are now two, and only the first blocks business development.

```
Initial work
      │
      ▼
GATE A — DEVELOPMENT READY
      │
      ├─────────────────────────────┐
      ▼                             ▼
BUSINESS DEVELOPMENT          NEXUS CONTINUATION
ERP / Business OS             remaining foundation work
CRM / Field Data              AI durability
Engine Works                  EXPERIENCE
Transport                     PRODUCT CORE expansion
Retreads                      GOVERNANCE · AUTOMATION
Knowledge Systems             DELIVERY · ASSURANCE
Machine Development           OPERATIONS
Internal Tools                DEVELOPER improvements
                                    │
                                    ▼
                        GATE B — FOUNDATION READY
```

### 12.1 GATE A — Development Ready

> **The earliest safe point at which internal business systems can begin.**
>
> Closed when three independent work items are planned, isolated, built, tested, evidenced, reviewed and integrated simultaneously — against a system with real identity, a single persistence backend, automated build verification, an AI gateway callable from DEVELOPER, and a quality gate that blocks integration while a mandatory acceptance criterion is unverified.

Closes at `M-07-5.3`. Its nine acceptance criteria are unchanged from v2.1 except that EXPERIENCE scope resolution is no longer among them.

**The minimum, by layer:**

| Layer | In GATE A | Deferred to GATE B or later |
|---|---|---|
| **CORE** | Identity foundation · Authentication · Organisation/tenant · Basic authorization · Secrets · Minimum audit for development accountability | **Usage metering** · SSO · MFA · ABAC · notifications · event bus · tool gateway |
| **DATA** | Azure SQL foundation · EF Core migration convention · Schema ownership · Database standards · Minimum persistence DEVELOPER needs | Knowledge store · embeddings · retrieval · RAG · lineage · retention |
| **AI** | Working model gateway · AI callable from DEVELOPER · Minimum context handling for development assistance | **Durable memory** · multi-provider routing · prompt versioning · evaluations · guardrails |
| **DEVELOPER** | The full V1a chain: product/project record, requirement, release where required, milestone, feature, work item, task, subtask, dependencies, worker, worker assignment, branch/worktree coordination, **simultaneous development**, build/test capture, review, progress/state, controlled integration | V1b autonomous dispatch · designers · capability packs · dashboards · **conversation surface** |
| **DELIVERY** | Git integration · branch/worktree rules · CI build · automated test execution · branch protection · results available to DEVELOPER · source backup minimum | Artifacts · environments · deployment · promotion · IaC · DR |
| **ASSURANCE** | Acceptance Criterion · Verification Method · Evidence · Pass/Fail · Basic quality gate | Plans · specifications · inspection · AI evaluation · profiles · certification |
| **GOVERNANCE** | Product identity only — DEVELOPER's work graph needs a real `ProductId` | Every other registry |
| **PRODUCT CORE** | Workspace · Project · Subproject only — DEVELOPER's scope trunk | Membership · subscriptions · entitlements · quotas · settings · onboarding |
| **EXPERIENCE** | **Nothing** | The entire layer |
| **AUTOMATION** | **Nothing** | The entire layer |
| **OPERATIONS** | Structured logging with correlation only | Everything else |

**What changed from v2.1's single gate.** Six milestones moved out, each for a stated reason recorded in the roadmap:

| Milestone | Was | Now | Why |
|---|---|---|---|
| `M-01-4.2` usage metering | P1 | P2 | Not in the GATE A CORE minimum. Audit is; metering is not. |
| `M-04-1.2` durable AI memory | P0 | P2 | Must not block business development. |
| `M-11-1.1` conversation core | P1 | P2 | EXPERIENCE is not in the GATE A minimum. |
| `M-11-1.2` boundary enforcement | P1 | P2 | Follows the conversation core. |
| `M-11-2.1` scope resolution | P1 | P2 | Follows the conversation core. |
| `M-11-3.1` reusable chat surface | P1 | P2 | Follows scope resolution. |
| `M-07-6.1` DEVELOPER scope resolver | P1 | P2 | Depends on EXPERIENCE, which left GATE A. |

Two dependencies were corrected to make that possible: `M-04-3.1` (DeveloperAgent) now depends on durable **traces** rather than durable **memory**, which is what it actually needs; and `M-07-5.3` no longer requires `M-11-2.1`.

**DEVELOPER V1a has no conversation surface at GATE A.** It has an API and a work-graph view. That is enough to run and coordinate three workers, and it removes roughly three weeks from the critical path.

### 12.2 GATE B — Foundation Ready

> **Confirms the broader reusable Nexus foundation is established** — the capability that makes each *additional* product cheaper, rather than the capability needed to build the first one.

Closes at the end of P2, when all of these are complete:

| Milestone | Capability |
|---|---|
| `M-04-1.2` | Durable AI memory |
| `M-11-2.1` | EXPERIENCE scope resolution |
| `M-11-3.1` | Reusable chat surface |
| `M-07-3.2` | DEVELOPER autonomous dispatch (V1b) |
| `M-08-5.1` | Automated deployment |
| `M-10-2.2` | Metrics and distributed tracing |
| `M-09-5.1` | Release qualification |

**The rule that makes two gates worth having:**

> **GATE B work runs in parallel with business development and must never pause or block it.** A business system waiting on GATE B is a scheduling error, not a dependency.

The roadmap enforces this: check 12 fails the build if any business-system milestone declares a dependency on a GATE B closer.

### 12.3 Why DELIVERY and ASSURANCE are still before GATE A

Both were moved forward in v2.1 and both stay. The argument is unchanged and is the reason GATE A can be trusted at all.

**DELIVERY.** GATE A's acceptance test requires independent build, independent test and result capture across three simultaneous workers. Without CI that means a human building three branches by hand on one machine — not isolation, no evidence trail, and no scaling past one machine. `.github/workflows/` is empty in NexusAI; Nexus.Web and Nexus.Int have no `.github` at all.

**ASSURANCE.** Without it, "integrated" means "someone merged it." With it, integration blocks while a mandatory criterion is unverified. Five entities and one gate is a small price, and it is the only thing standing between a green build and a claim that a requirement was satisfied.

### 12.4 Recommended subjects for the acceptance test

Three genuinely independent **P2** work items rather than a synthetic exercise. A synthetic test proves the mechanism; a real one proves the mechanism *and* delivers three work items.


## Part 13 — The work breakdown

`nexus-roadmap.yaml` v2.1 is the authoritative work breakdown. This part summarises it; it does not duplicate it.

### 13.1 Shape

| Node type | Total | P0 | P1 | P2 | P3 | P4 | P5 |
|---|---|---|---|---|---|---|---|
| Layers | 12 | | | | | | |
| Features | 90 | 7 | 18 | 19 | 35 | 8 | 3 |
| Milestones | 151 | 13 | 26 | 31 | 72 | 7 | 2 |
| Work items | 108 | 21 | 57 | 30 | — | — | — |
| Tasks | 140 | 38 | 102 | — | — | — | — |
| Subtasks | 113 | 12 | 101 | — | — | — | — |
| **Total** | **614** | | | | | | |

The empty cells are the depth rule (Part 0), not gaps.

### 13.2 Validation results

Thirteen mechanical checks, all passing. Three are new in v2.2 and two of them exist specifically to keep the gates honest.

| # | Check | Result |
|---|---|---|
| 1 | All IDs unique | **PASS** — 614 nodes |
| 2 | All parent references valid | **PASS** |
| 3 | All dependencies resolvable | **PASS** — 203 dependencies |
| 4 | No impossible phase dependencies | **PASS** |
| 5 | No circular dependency graph | **PASS** — topological sort completes |
| 6 | Every milestone has acceptance criteria | **PASS** — 151/151 |
| 7 | Every gate has measurable evidence | **PASS** — `M-07-5.3`, `M-09-1.3` |
| 8 | Every GATE A minimum item has a dependency path | **PASS** — all nine contributing layers |
| 9 | Business systems eligible after GATE A | **PASS** — 8 systems; `F-12-16` exempt as safety-gated |
| 10 | Parallel-safe classifications valid | **PASS** — 99 milestones carry a schema conflict group |
| 11 | **GATE B closers exist and land by P2** | **PASS** — 7 closers |
| 12 | **No business system blocked by a GATE B closer** | **PASS** — this is what keeps Stream B from stalling Stream A |
| 13 | **No project-rename churn remains** | **PASS** — no milestone exists whose only purpose is a namespace rename |

### 13.3 Phases cut across layers — by construction

No layer is built to completion before the next begins. CORE alone spans four phases:

| Layer | P0 | P1 | P2 | P3 | P4 | P5 |
|---|---|---|---|---|---|---|
| CORE | secrets, model gateway | identity, tenancy, authz, audit, usage | event bus | ABAC, multi-provider, tools, notifications | | |
| DATA | SQL migration, Dataverse removal | layer schemas, documents | knowledge, search | embeddings, RAG, lineage, retention | | |
| AI | durable traces, memory | citations, developer agent | cost attribution | prompt versioning, routing, evaluation | | result loop |
| DEVELOPER | | work graph, analysis, workers, review, progress, **GATE** | autonomous dispatch, requirements, releases, conversation | designers, capability packs | | self-improvement |
| DELIVERY | CI, source safety | | artifacts, environments, deployment | IaC, drift, backup, DR | | |
| ASSURANCE | | criteria, evidence, quality gate | defects, release qualification | plans, inspection, AI evaluation, profiles | measurement traceability, safety profile | |
| EXPERIENCE | | conversation core, scope resolution, chat surface | | commands, search, approvals, components | voice, realtime | |
| PRODUCTS | | | **ERP core + business systems eligible** | product framework | consumer products | machine automation (gated) |

A layer having milestones in P0, P1, P3 and P5 with nothing in P2 is expected, not a defect.

### 13.4 The critical path

```
P0  M-08-1.1 package feed ─► M-08-1.2 pipelines ─► M-08-1.3 results ─► M-08-1.4 protection
    M-02-1.1 commit 1b ─► 2a ─► 2b/2c ─► M-02-1.4 Dataverse deleted
    M-01-5.1 secrets        M-04-1.1 durable traces ─► M-04-1.2 memory
    M-01-6.1 OpenAI live ─► M-04-2.1 citations proven
                             │
P1  M-02-1.5 layer schemas ──┼─► M-01-1.1 identity ─► M-01-1.2 auth ─► M-01-2.1 tenancy ─► M-01-3.1 authz
                             ├─► M-06-1.1 scope trunk ─► M-06-1.2 scope registration
                             └─► M-11-1.1 conversation core ─► M-11-2.1 scope resolver ─► M-11-3.1 chat surface
                                                                        │
                             M-03-1.1 product record ──┐                │
                             M-02-2.1 documents ───────┼─► M-07-1.1 work graph
                                                       │        │
                                                       │        ├─► M-07-2.1/2.2 dependencies & safety rules
                                                       │        ├─► M-07-3.1 workers & runs
                                                       │        ├─► M-07-4.1 build/test capture
                                                       │        ├─► M-09-1.1 criteria ─► M-09-1.2 evidence
                                                       │        └─► M-07-5.1 review ─► M-07-5.2 progress
                                                       │                       │
                                                       │              M-09-1.3 quality gate
                                                       │                       │
                                                       └───────────────► M-07-5.3 FOUNDATION GATE
                                                                                │
                              ┌─────────────────────────────────────────────────┴──────────────┐
P2                            ▼                                                                ▼
                    STREAM A — BUSINESS                                    STREAM B — NEXUS CONTINUATION
                    F-12-8 ERP core (first)                                M-07-3.2 autonomous dispatch
                    then pulled by need                                    M-08-3/4/5 delivery
                                                                           M-10-2 observability
                                                                           M-09-2/5 assurance
```

### 13.5 Business-system timing

| | v2.0 | v2.1 | v2.2 |
|---|---|---|---|
| Gate that unblocks them | Foundation Gate | Foundation Gate | **GATE A Development Ready** |
| ERP / Business OS | P4 | P2 | **P2, recommended first** |
| CRM · Engine Works · Retreads · Transport · Knowledge Systems · Internal Tools · Machine Development | P4 | P2 | **P2** |
| Machine Automation | P5, gated | P5, gated | P5, gated *(unchanged)* |
| Consumer products | P4 | P4 | P4 *(unchanged)* |

The phase number did not move between v2.1 and v2.2. What moved is **the weight of the gate in front of it** — six milestones lighter, so P2 arrives sooner.

**Eligible means pulled by business need, not scheduled.** Eight systems becoming eligible does not mean eight start. ERP core is generally first because the others reuse its organisation, party, product, document and transaction primitives — unless a current business requirement justifies a different immediate priority.

### 13.6 Phases still cut across layers

Unchanged principle, and v2.2's moves reinforce it. AI now has milestones in P0, P2, P3 and P5. EXPERIENCE has none before P2. CORE spans P0 through P3. A layer with a gap in the middle is expected, not a defect.

## Part 16 — Entity migration matrix

Every existing entity classified per §35. **`KEEP` dominates: nothing needs to be thrown away.**

### 16.1 Nexus.Experience — Chat domain aggregates

| Entity | Target layer | Action | Reasoning |
|---|---|---|---|
| `Workspace` | 06 PRODUCT CORE | **MOVE** | A reusable scope primitive, not a product's. DEVELOPER, a plain conversation and every future product consume the same trunk. Not CORE's `Organisation` — that is tenancy; this is a workspace. |
| `Project` | 06 PRODUCT CORE | **MOVE** | Same reasoning as `Workspace`. DEVELOPER extends below `Subproject`. |
| `Conversation` | 11 EXPERIENCE (core) + 11 (Chat specifics) | **SPLIT** | Universal core to Layer 10 per §23; `ConversationType`, `ConversationVisibility` stay in Chat. Do it when Developer needs conversations, not before. |
| `ConversationMessage` | 11 EXPERIENCE | **MOVE** | Part of the universal conversation core. |
| `Knowledge` | 02 DATA | **MOVE** | Knowledge is explicitly a Layer 02 concept. Currently a Chat aggregate because Chat was the only product. |
| `Adr` | 02 DATA | **MOVE + REFACTOR** | An ADR is a Document with a decision lifecycle. Becomes `Document` + `DecisionRecord` metadata rather than its own aggregate. |
| `WorkItem` | 07 DEVELOPER | **MOVE** | Already the right shape; wrong home. Has a `milestone` FK waiting for a `Milestone` that does not exist yet — `M-07-1.1` supplies it. |
| `Artifact` | Split: 07 DEVELOPER / 08 DELIVERY | **SPLIT** | A build output is Delivery's. A work product attached to a work item is Developer's. Currently conflated. |
| `Branch` | 08 DELIVERY | **MOVE** | Git branch state is Delivery's. Developer *references* it. |
| `Snapshot` | 08 DELIVERY | **MOVE** | Same reasoning. |
| `Session` | Split: 01 CORE / 07 DEVELOPER | **SPLIT** | A user session is Platform's. A development session is Developer's `DevelopmentRun`. Two different things sharing a name. |

**Sequencing note.** Every `MOVE` above is post-gate. Doing them before the gate means moving code while also building on it. `M-02-1.2` migrates all eleven aggregates to SQL *in place*; they move layers afterwards. Exception: `WorkItem` may move during `M-07-1.1` if it proves cheaper than referencing across the boundary.

### 16.2 Nexus.Platform — Platform

| Entity | Target | Action | Reasoning |
|---|---|---|---|
| `Nexus.Platform.Contracts/Models/*` (13 types) | 01 | **KEEP** | Well-designed. |
| `Nexus.Platform.Contracts/Tools/*` | 01 | **KEEP** | Contracts fine; implementation missing. |
| `Nexus.Platform.Contracts/Governance/*` | 01 | **KEEP + EXTEND** | `IAuditLog`, `IUsageMeter`, `IQuotaPolicy` correct. Need durable implementations (`M-01-4.1`). |
| `IProductRegistry` | 03 GOVERNANCE | **MOVE** | Product registry is Governance, not Platform identity. Move in `M-03-1.1`. |
| `IIdentityService`, `ITenantResolver`, `ResolvedIdentity` | 01 | **KEEP + EXTEND** | Correct shape; `M-01-1.1` makes them real. |
| `ConsoleAuditLog`, `InMemoryUsageMeter`, `PermissiveQuotaPolicy` | 01 | **REPLACE** | Keep as test doubles; replace in production wiring (`M-01-4.1`). |
| `RoutingModelGateway`, `AggregatingModelCatalog` | 01 | **KEEP** | Proves the routing abstraction with one provider. |
| `OpenAIModelGateway` + catalog + options | 01 | **KEEP** | |
| `AnthropicModelGateway` (306 B) | 01 | **KEEP as stub** | Not gate-relevant. `M-01-6.2`. |
| `IdentityProvider` (240 B) | 01 | **REPLACE** | `M-01-1.1` supersedes it entirely. |
| `PlatformStore` (308 B) | 01 | **REPLACE** | `M-01-1.1`/`M-01-4.1` supersede it. |
| `ToolProvider` (231 B) | 01 | **KEEP as stub** | Post-gate. |
| `NexusAI.Agents`, `.Api`, `.Core`, `.Domain`, `.Foundation`, `.Host`, `.Infrastructure` | — | **REMOVE** | Empty gitignored husks from V2.1. Deleting them removes ambiguity about where code goes. Low risk, do during `M-08-1.x`. |
| `NexusAI.Application/{Agents,Orchestration,WorkItem}` | — | **REMOVE** | Same. |

### 16.3 Nexus.Intelligence — Intelligence

| Entity | Target | Action | Reasoning |
|---|---|---|---|
| `Nexus.Intelligence.Contracts/Turns/*` (17 types) | 04 | **KEEP** | The best contract surface in the system. Do not touch. |
| `Nexus.Intelligence.Contracts/Context/*` | 04 | **KEEP** | `ContextBundle`/`ContextItem`/`TrustLevel` is the seam. Protect it. |
| `TurnPipeline` and its ten steps | 04 | **KEEP** | Working. |
| `InMemoryTurnTraceStore` | 04 | **REPLACE** | `M-04-1.1`. |
| `InMemoryResultReportStore` | 04 | **REPLACE** | `M-04-1.1`. |
| `InMemoryMemoryStore` | 04 | **REPLACE** | `M-04-1.1`. |
| `KeywordContextRanker` | 04 | **KEEP + EXTEND** | Sufficient until `C-9` adds embeddings. Keep as a fallback afterwards. |
| `PromptAssembler` | 04 | **KEEP** | |
| `AgentRegistry`, `AgentDispatcher`, agent abstractions | 04 | **KEEP** | Right shape. |
| `DeveloperAgent` (974 B) | 04 | **EXTEND** | Becomes real in `M-07-2.2`/`M-07-5.1` — the agent that reasons about the work graph. |
| `EmptyToolCatalog`, `EmptyToolGateway` | 04 | **KEEP as stub** | Honest placeholders. Post-gate. |
| `Planner`, `ExecutionEngine` | 04 | **KEEP** | |

### 16.4 Deprecated

| Item | Action | When |
|---|---|---|
| `Microsoft.PowerPlatform.Dataverse.Client` + all Dataverse repositories/mappers | **REMOVE** | `M-02-1.4` |
| `System.Security.Cryptography.Xml` version pin | **REMOVE** | `M-02-1.4` (only existed for Dataverse) |
| `Nexus:Persistence` configuration switch | **REMOVE** | `M-02-1.4` |
| `.git-broken\` in all three repositories | **REMOVE** | After `M-08-1.x` confirms backups |
| `ChatTurnIdentity` hardcoded tenant/permissions | **REPLACE** | `M-01-1.1` |

---

## Part 17 — Parallelisation matrix

Per the brief's §32. This is also `M-07-2.2`'s first real test case — the safety-rule engine must reproduce this table from declared `ScopeDeclaration` data alone, without it being hardcoded.

| Milestones | Classification | Reason |
|---|---|---|
| `M-08-1.1` package feed | **Can run now** | No source conflict; configuration only. First work in the roadmap. |
| `M-02-1.1` commit Stage 1b | **Can run now** | Independent of everything. |
| `M-01-5.1` secrets · `M-08-2.1` source safety | **Can run together** | Different concerns, no shared files. |
| `M-08-1.2` pipelines × 3 repos | **Can run together** after `M-08-1.1` | Three repositories, three workers. The natural first parallel proof, before Developer exists to coordinate it. |
| `M-02-1.2` → `M-02-1.3` → `M-02-1.4` | **Must be sequential** | Shared `DbContext` model snapshot. Rule 3, and the canonical example of it. |
| `M-04-1.1` traces · `M-02-1.2` SQL 2a | **Can run together** | Different repositories entirely. |
| `M-04-1.1` → `M-04-1.2` memory | **Must be sequential** | Both add migrations to the `intel` schema. |
| `M-01-6.1` OpenAI live → `M-04-2.1` citations | **Waiting for dependency** | Citations cannot be proven without a live model. |
| `M-02-1.5` layer schemas | **Blocked** until `M-02-1.4` | Cannot establish schema ownership while two persistence stacks exist. |
| `M-01-1.1` identity · `M-06-1.1` scope · `M-12-1.1` conversation core | **High conflict risk** | Three concurrent migrations creating three schemas in one database. Cap at 2 workers touching schema; sequence identity first. |
| `M-01-1.2` auth · `M-01-4.1` audit · `M-01-4.2` usage | **Can run together** after `M-01-1.1` | Same schema but distinct tables, and the schema already exists by then. |
| `M-06-1.2` scope registration · `M-11-2.1` scope resolver | **Can run together** | Contract work on both sides of one seam — coordinate the interface, then split. |
| `M-03-1.1` product record · `M-02-2.1` documents | **Can run together** | Different schemas, no shared contract. |
| `M-07-1.1` work graph | **Waiting for dependency** | Needs `M-03-1.1`, `M-06-1.1`, `M-02-2.1`. |
| `M-07-2.1`/`2.2` analysis · `M-07-3.1` workers · `M-07-4.1` capture | **Can run together** after `M-07-1.1` | Analysis is algorithm, workers is orchestration, capture is ingestion. Distinct scopes — and this is the batch that most resembles the gate test itself. |
| `M-07-5.1` review → `M-07-5.2` progress → `M-07-5.3` gate | **Must be sequential** | Each consumes the previous one's output. |
| P2 Stream A · P2 Stream B | **Can run together** | The whole point of the gate. |
| `M-08-3.1` → `M-08-4.1` → `M-08-4.2` → `M-08-5.1` | **Must be sequential** | Artifacts before environments before provisioning before deployment. |
| Business systems `F-12-8`…`F-12-15` | **Can run together** after Business OS | Distinct domains — but see Decision 4; they are pulled, not scheduled. |
| `F-12-16` Machine Automation | **Must be sequential**, gated | Safety-critical. No milestone may be created here by Developer or any agent. |

**Recommended worker count.** Three for the P0 pipeline batch. Two through the P1 schema-creation phase — the cap is the binding constraint, not the dependency graph. Three for the Developer analysis/workers/capture batch. Four to six across both streams from P2.


## Part 18 — Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | **Developer V1 scope creep** | **High** | **High** — the gate never closes | The V1a/V1b table in §7.7 is the contract. Anything not marked V1a waits, without exception. |
| 2 | Another environmental loss of git objects | Medium | Severe | `M-08-1.x` verifies AV exclusion and documents backups. Push at every stage boundary. |
| 3 | Tenant isolation subtly wrong in `M-01-1.1` | Medium | **Severe** — silent cross-tenant data exposure | Write the cross-tenant denial test before the implementation. Human review mandatory. |
| 4 | Bootstrapping trap — Developer defect halts both streams | Medium | High | V1a is advisory: humans and coding agents still execute. A Developer outage costs a dashboard, not a capability. |
| 5 | Parallel migration conflicts corrupt the model snapshot | **High** if unmanaged | Medium | §10.2 rule 3 is explicit and enforced by `M-07-2.2`. Cap schema-touching workers at 2. |
| 6 | Business systems never start because the foundation keeps growing | Medium | **High** — this is the failure mode that kills platform projects | The gate is defined, dated and small. Anything not in §12.3's MANDATORY list is not allowed to delay it. |
| 7 | Derived progress reports confident nonsense | High if unmanaged | Medium | The `BreakdownComplete` rule in §9.2. |
| 8 | Test debt compounds — 4 test files today | **High** | High | Every work item's Definition of Done includes a test. `M-08-1.x` makes the absence visible. |
| 9 | Local NuGet feed blocks CI | High | Low | Move Platform/Intelligence packages to GitHub Packages in `WI-X1.1`. |
| 10 | This document goes stale and diverges from reality | **High** | Medium | Part 11's Phase 2: `roadmap.yaml` produced during `M-07-1.1` and imported, so the structured facts have one home. |
| 11 | Machine Automation safety treated as a normal milestone | Low | **Catastrophic** | `B-9` requires its own safety architecture and human sign-off before any milestone is written. Deterministic controllers own motion and interlocks, always. |
| 12 | Eleven layers become eleven services prematurely | Medium | High | The brief already warns against it and this report follows: three repos plus one new one, strong module boundaries, enforced by architecture tests rather than network boundaries. |

---

## Part 19 — What not to build yet

Per §41. Each of these is genuinely useful and none is justified by current evidence:

- **Microservices.** Three repositories with enforced module boundaries. Split when a boundary genuinely needs independent scaling or deployment — not before.
- **Cloud worker farms.** Prove three simultaneous workers on one machine first. That is what the gate tests.
- **Full autonomous development.** V1a is advisory. Autonomy after the model is proven, not before.
- **Every future product.** Chat exists, Developer is next, then one business system end to end. Nine products at once teaches nothing.
- **Every capability pack.** Define capability packs when a second product actually needs the same capability. Two data points beat one guess.
- **Multi-region.** No region yet.
- **Advanced self-healing.** Nothing is deployed.
- **Complex event infrastructure.** In-process events until a real cross-service need appears.
- **A second AI provider.** `RoutingModelGateway` already proves the abstraction with one provider.
- **Embeddings and RAG.** Keyword ranking is sufficient until there is enough content for retrieval quality to be measurable — and until `M-04-2.1` proves citations work at all.
- **Workflow engine.** Explicit state transitions until V1b needs retry and escalation.
- **Subscription and billing.** One user, one product.

---

## Part 20 — Immediate next milestone

**Before anything in this report is acted on: `M-02-1.1` — commit and push SQL Stage 1b.**

It is complete, its acceptance test has run and passed (§2.7), and it has sat uncommitted in `Nexus.Web` since 2026-08-20 18:08 UTC — in a repository that lost its entire object database two days earlier to a cause that has never been confirmed fixed. This does not wait for a roadmap decision.

**Then `M-08-1.1` — make the package feed reachable from CI.**

It is first in the roadmap proper for three reasons: it has no dependencies at all; it is small; and every pipeline that follows is blocked on it, because Platform and Intelligence packages currently resolve only from `C:\Personal\LocalNuGet`, which no build agent can see.

**Then three workers in parallel** — `M-08-1.2` (pipelines across three repositories), `M-02-1.2` (SQL Stage 2a), `M-04-1.1` (durable Intelligence stores). Three repositories, three distinct scopes, no shared migration. This is the first genuine parallel batch, and it runs *before* Developer exists to coordinate it — which is worth doing deliberately, because it is the last time the coordination will be manual and it is a useful reference point for what `M-07-5.3` later has to beat.

**P0 exit criteria, all four:** CI green on every repository with branch protection enforced; Azure SQL the only persistence backend with Dataverse gone; Intelligence state surviving a restart; citations proven against a live model.


## Part 21 — Decisions taken

All seven are answered. Recorded here because the roadmap's shape follows from them, and because a later reader needs to know what was chosen deliberately.

| # | Question | Decision | Where it lands |
|---|---|---|---|
| 1 | Does Delivery move before the gate? | **Yes — proceed and move.** | Layer 08 `F-08-1` and `F-08-2` are P0. `M-08-1.1` is the first work in the entire roadmap. |
| 2 | Is Developer a layer, a product, or both? | **A single full layer** — used to develop the products and the other layers, including itself. Carries product history, features, upgrades, its own conversation surface, and simultaneous development. | §7.7. `Nexus.Developer`, 7 projects, `dev` schema, 9 features, 24 milestones. |
| 3 | Is the V1a / V1b split acceptable? | **Accepted.** | V1a is P1 and closes the gate; V1b is `M-07-3.2` in P2. |
| 4 | Business Systems priority order? | **On demand — they are systems for the business, not consumer products.** Consumer products are the planned stream. | §7.11. `F-12-8`…`F-12-15` marked `on_demand: true`. Roadmap B dissolved. |
| 5 | Where does Layer 02's document half live? | **One platform database, schema per layer; one database per product.** | §4.1.1. `M-02-1.5` establishes the convention. |
| 6 | When do the Chat aggregates move layers? | **Chat becomes the backend conversation layer.** Every product and layer uses it with its own scope. | §7.10, Part 8. The largest change in v2.0. |
| 7 | Confirm the gate scope | **Reworked in v2.2 into two gates.** | §12. GATE A is six milestones lighter than v2.1; GATE B carries the rest and never blocks business work. |

### 21.1 What decision 6 cost, and why it is still right

Making chat a layer added roughly three weeks to P1: extracting the conversation core (`M-12-1.1`), building scope resolution (`M-11-2.1`), moving `Workspace`/`Project` to Layer 06 (`M-06-1.1`), and Developer's own resolver (`M-07-6.1`).

It is worth it because the alternative is worse in a way that compounds. Developer needs conversations. If chat had stayed a product, Developer would have built its own — and then machine development would have built a third. Three conversation implementations, three citation panels, three sets of Intelligence wiring, diverging from the day they are written. Paying three weeks once, before the second consumer exists, is the cheapest this change will ever be.

It also happens to be the moment the brief's §23 becomes true rather than aspirational.

### 21.2 Suggestions you asked for

Five things I would raise unprompted.

**1. Commit Stage 1b before anything else.** Still uncommitted, still proven, still in a repository that lost its entire object database on 20 August. This has now been the top of two consecutive reports. `M-02-1.1`.

**2. Write two tests before their implementations, not after.** The cross-tenant denial test in `M-01-2.1`, and parallel-safety rule 3 in `M-07-2.2`. Both are cases where the bug is silent and the damage is discovered late — one leaks data across tenants, the other corrupts two workers' migrations into an unmergeable state. Test-first is not a general prescription here; it is specific to these two.

**3. Treat `ScopeDeclaration` as gate-critical, not as analysis plumbing.** It is built in `M-07-1.1` with the work graph rather than in `M-07-2.1` with the analysis that uses it, deliberately. Add it later and every existing work item needs backfilling, by hand, with information nobody remembers.

**4. Consider running the gate acceptance test on P2 work rather than a toy.** `M-07-5.3` selects three genuinely independent P2 work items as its subjects. A synthetic test proves the mechanism; a real one proves the mechanism *and* delivers three work items. There is no reason to spend the run on a rehearsal.

**5. The one thing I would still push back on.** Developer V1a has nine features and 24 milestones, and it is the most interesting thing in the roadmap to build. Scope creep here is the single highest risk in the whole plan — it is what turns an 8–10 week gate into a six-month one, and it will not feel like creep at the time, because every addition will be genuinely useful. The V1a/V1b/P3 split in §7.7 is the contract. `F-07-8` (product designer, schema designer, capability packs) is P3 for a reason: it is the most seductive part of Developer and the least necessary to prove three workers can work at once.

### 21.3 Still open

Not decisions, but things that need an answer before the phase they belong to:

| Question | Needed by | Note |
|---|---|---|
| Which consumer product is first? | P4 planning | Vault is listed first on capability fit — documents, AI, security — not on business judgement. That judgement is yours. |
| Cloud provider and hosting shape? | `M-08-4.2` provisioning, P2 | Nothing is deployed anywhere today, so this is genuinely open. |
| Does `dev` split to its own database? | P3 | Predicted, not decided. Revisit when autonomous run volume is real rather than estimated. |
| Who reviews security-critical merges? | `M-01-2.1`, P1 | Tenant isolation needs a second pair of eyes, and there is currently one pair. |

---

*Report ends. Version 2.2 — two gates, business development brought forward. No code was modified, no migrations run, no entities deleted, no repositories restructured.*
