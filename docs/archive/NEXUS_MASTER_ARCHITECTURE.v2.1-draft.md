# Nexus — Master Architecture, Purpose and Execution Roadmap

**Status:** Draft for human review. No code changed, no migrations run, no entities deleted, no repositories restructured.
**Date:** 2026-08-21
**Supersedes:** prior Nexus architecture prompts where in conflict. Does **not** supersede ADR-014 (Azure SQL migration), which this report absorbs as Layer 02 foundation work.

---

## Part 0 — How to read this, and what it deliberately does not contain

The brief asks for 51 sections including full milestone drill-down, work items, tasks and subtasks for every milestone across eleven layers and three roadmaps. Applied literally — the mandatory 34-field milestone template against every milestone, plus the 12-field task template against every task — that is several hundred pages, and most of it would be invented.

The brief also contains the rule that resolves this, in §3: *"Do not create roadmap items with no architectural purpose."* Task-level decomposition of Layer 09 Operations today would be fiction dressed as a plan. There is no Operations code, no decided scope, and no runtime to observe. Writing forty tasks for it would produce a document that *looks* executable and isn't, which is worse than an honest gap.

So this report is deep where work is imminent and reserved where it is not:

| Depth | Applies to | Why |
|---|---|---|
| **Full** — purpose, responsibilities, data ownership, interfaces, V1 scope, milestones, work items, tasks, acceptance criteria | Layers 01, 02, 04, 07 (the foundation four), the Foundation Gate, Roadmap A | Work starts on approval. Detail is load-bearing. |
| **Structural** — purpose, responsibilities, owns / does not own, sub-layers, data ownership, interfaces, V1 boundary | Layers 03, 05, 06, 08, 09, 10, 11 | Enough to reserve the architectural space and stop the foundation being built wrong. Not enough to pretend the work is planned. |
| **Register only** — milestone ID, layer, purpose, dependencies, gate relevance, parallelisable | Roadmaps B and C | Sequence and dependency are decidable now. Task content is not, and will be decided *by Developer V1*, which is the point of building it. |

Drill-down for Roadmaps B and C is deferred until the Foundation Gate is within sight. That is a recommendation, not a limitation — writing it now would guarantee rework.

**Three things in this report contradict the brief.** They are in §3.4, §12.2 and §7.7, each with reasoning. They are the parts most worth your disagreement.

---

## Part 1 — Executive summary

**The realignment is sound and cheaper than it looks.** The eleven layers are not a rewrite of the current system; they are a finer-grained reading of the three-solution split that already exists. Nexus.AI is Layers 01/03/06, Nexus.Int is Layer 04, Nexus.Web is Layers 10/11. Nothing currently built lands in the wrong place. The V2.1 restructure completed last week did most of the structural work this brief asks for, before the brief existed.

**Nothing in flight is wasted.** The Azure SQL migration (ADR-014, Stages 1b–3) becomes Layer 02 foundation work and is a Foundation Gate blocker — it gets *more* important under this architecture, not less. The frontend F0–F4 work, completed and pushed, is Layer 10/11 and stands. Continue both.

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
| `C:\Personal\NexusAI` | `github.com/prtcare/NexusAI` | Platform — NuGet libraries only, no host | `Nexus.AI.slnx` |
| `C:\Personal\Nexus.Int` | `github.com/prtcare/Nexus-Int` | Intelligence — deployed at `/intelligence/v1` | `Nexus.Int.slnx` |
| `C:\Personal\Nexus.Web` | `github.com/prtcare/Nexus-web` | Chat product — `/api/v1` + React client | `Nexus.Web.slnx` |
| `C:\Personal\LocalNuGet` | — | Local package feed for Platform/Intelligence packages | n/a |

### 2.2 Project map — NexusAI (Platform)

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

### 2.3 Project map — Nexus.Int (Intelligence)

| Project | Contents | Substance |
|---|---|---|
| `Nexus.Intelligence.Contracts` | `Turns/` (17 types — IntelligenceTurnRequest/Response, ScopeRef, ActorRef, TurnConstraints, DecisionTrace, PlanStep, ProposedAction, UsageSummary…), `Context/` (ContextBundle, ContextItem, ContextItemKind, TrustLevel, Citation, PersistenceHint), `Results/` (ResultReport, ResultOutcome), `Client/IIntelligenceClient` | **Real and well-formed.** This is the strongest contract surface in the codebase. |
| `Nexus.Intelligence.Core` | `Turns/` — full pipeline: IntentClassifier, PolicyGate, ContextSelector, AgentSelector, ModelSelector, PromptStep, ModelStep, ToolLoop, ResponseComposer, TurnPipeline (6.9 KB), InMemoryTurnTraceStore; `Planning/Planner` (3.9 KB); `Execution/ExecutionEngine` | **Real.** A complete, working turn pipeline. |
| `Nexus.Intelligence.Context` | `Ranking/` KeywordContextRanker (2.6 KB), RankingOptions; `Prompting/` PromptAssembler (3.6 KB) | **Real but primitive.** Keyword ranking only — no embeddings, no vector retrieval. |
| `Nexus.Intelligence.Agents` | Abstractions (IAgent, IAgentRegistry, IAgentDispatcher, IAgentRuntime, AgentContext, AgentMetadata, AgentType), AgentRegistry, AgentDispatcher, `BuiltIn/DeveloperAgent.cs` (974 bytes) | **Skeleton.** Registry works; `DeveloperAgent` is a stub. |
| `Nexus.Intelligence.Memory` | IMemoryStore, InMemoryMemoryStore, MemoryRecord, MemoryQuery, MemoryKind | **Volatile.** In-memory only. |
| `Nexus.Intelligence.Api` | Endpoints: Turns, Plans, Results, Capabilities, Health; `Tooling/` EmptyToolCatalog + EmptyToolGateway; `ResultReports/InMemoryResultReportStore` | **Real, but tool surface is empty and results are volatile.** |

### 2.4 Project map — Nexus.Web (Chat product)

**Domain — eleven aggregates**, each with an aggregate root, strongly-typed ID, status enum and repository interface:

`Adr` · `Artifact` · `Branch` · `Conversation` · `ConversationMessage` · `Knowledge` · `Project` · `Session` · `Snapshot` · `WorkItem` · `Workspace`

**API — eleven endpoint groups** under `Endpoints/`: Artifacts, Branches, Chat, ConversationMessage, Conversations, Knowledge, Projects, Sessions, Snapshots, WorkItems, WorkSpaces, plus Health.

**Infrastructure — dual persistence, mid-migration:**
- `Sql/` — `NexusChatDbContext`, `Configurations/WorkspaceConfiguration`, `Conventions/StronglyTypedIdConverters`, `Repositories/SqlWorkspaceRepository`, and migration `20260820180802_InitialSqlSchema` (org schema, `Seq` IDENTITY, `Ref` computed-persisted, unique index)
- Dataverse implementations for the remaining ten aggregates
- Both live behind the `Nexus:Persistence` configuration key (ADR-014 strangler pattern)

**Frontend — React/TypeScript under `src\Nexus.Web.Client\src\`:**
- `api/` — ApiClient, ApiError (single HTTP path, post-F0)
- `features/chat/` — 15 files: ChatPanel, MessageThread, ConversationList, CreateConversationForm, CitationsPanel, ChatTelemetryContext, citationTargets, useCitationTarget, useConversation(s), useConversationMessages, useCreateConversation, useSendChat, chatApi, chat.types
- `features/projects/`, `features/workspaces/`, `features/system/`
- `pages/` — Dashboard, Chat, Insights, ProjectDetails, WorkItem, KnowledgeItem, Workspaces, CreateWorkspace, WorkspaceSettings, Settings, NotFound
- `components/RouteErrorBoundary`, `layouts/AppLayout`, `routes/AppRoutes`

### 2.5 Test coverage — the weakest area in the system

| Repository | Test projects | Actual test files |
|---|---|---|
| NexusAI | `Nexus.Platform.Tests`, `Nexus.Platform.Architecture.Tests` | `PlatformBoundaryTests.cs` only. **`Nexus.Platform.Tests` contains a `.csproj` and no `.cs` files at all — zero behaviour tests for Platform.** |
| Nexus.Int | `Nexus.Intelligence.Tests`, `Nexus.Intelligence.Architecture.Tests` | `BoundaryRuleTests.cs`, `KeywordContextRankerTests.cs` — 2 files |
| Nexus.Web | `Nexus.Products.Chat.Tests`, `Nexus.Products.Chat.Architecture.Tests` | `BoundaryTests.cs`, `ChatContextBundleMapperTests.cs` — 2 files |

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

No CI, no deployment, no infrastructure-as-code, and a test suite of four files. The system is built and run by hand on one machine. This is survivable for one developer and fatal for parallel streams, which is why §12.2 moves it before the gate.

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

### 4.1 The eleven layers, and where they physically live

The key judgment: **eleven logical layers, three repositories, and one new one.** Not eleven services, not eleven databases.

| # | Layer | Repository | Status today |
|---|---|---|---|
| 01 | Platform | `NexusAI` | Contracts real, implementations mostly stubs |
| 02 | Data & Knowledge | `NexusAI` (contracts) + per-product infra | Structured half in progress (ADR-014); document half is markdown files |
| 03 | Governance & Registry | `NexusAI` | Seed only — `IProductRegistry` interface exists |
| 04 | Intelligence | `Nexus.Int` | Strongest layer in the system |
| 05 | Automation & Workflow | `NexusAI` | Does not exist |
| 06 | Shared Product Foundation | `NexusAI` | Does not exist |
| 07 | Developer | **`Nexus.Dev` (new)** | Six aggregates exist, misplaced in Chat |
| 08 | Delivery & Infrastructure | Repo-level config + `NexusAI` contracts | Does not exist |
| 09 | Operations | `NexusAI` (contracts) + hosts | Does not exist |
| 10 | Experience / Interaction | `Nexus.Web` client + `NexusAI` contracts | Real for chat, nothing reusable extracted |
| 11 | Products | `Nexus.Web` (Chat), `Nexus.Dev` (Developer), future repos | One product |

### 4.2 Dependency direction

```
                    11 Products
                         │
        ┌────────────────┼────────────────┐
        ▼                ▼                ▼
  10 Experience   06 Shared Product   07 Developer
        │            Foundation            │
        └────────────────┼─────────────────┘
                         ▼
         ┌───────────────┼───────────────┐
         ▼               ▼               ▼
   04 Intelligence  05 Automation  03 Governance
         │               │               │
         └───────────────┼───────────────┘
                         ▼
              02 Data & Knowledge
                         │
                         ▼
                   01 Platform
                         │
         ┌───────────────┴───────────────┐
         ▼                               ▼
  08 Delivery & Infra              09 Operations
        (cross-cutting, depend on nothing above)
```

**Rules, enforceable by NetArchTest and already partially enforced:**

1. A layer may depend only on layers below it.
2. Layers 08 and 09 are cross-cutting: everything may emit to them; they may depend on nothing above Platform.
3. **No shared kernel.** `Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never reference product types. Currently true — keep it true.
4. Products never reference each other. Chat cannot see Developer's types and vice versa.
5. Intelligence never sees product structure. It receives `ContextBundle`; `ScopeRef` is opaque to it. Currently true.

### 4.3 The governing sentence, extended

The existing rule — *Intelligence decides, Platform executes, products own the data and the experience* — still holds and is extended:

> **Intelligence reasons. Automation executes. Governance records what is true. Developer builds. Delivery ships. Operations runs. Data & Knowledge remembers. Products own their domain and their users.**

---

## Part 5 — Responsibility matrix

`O` = owns · `U` = uses · `—` = no relationship

| Capability | 01 | 02 | 03 | 04 | 05 | 06 | 07 | 08 | 09 | 10 | 11 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Identity / authentication | **O** | — | U | U | U | U | U | U | U | U | U |
| Tenancy / organisation | **O** | U | U | — | — | U | U | — | — | — | U |
| Authorization / policy | **O** | U | U | U | U | U | U | — | — | — | U |
| Audit trail | **O** | U | U | U | U | U | U | U | U | — | U |
| Secrets | **O** | — | U | U | U | — | — | U | U | — | — |
| Structured data access | U | **O** | U | — | — | — | U | — | — | — | U |
| Documents & versions | — | **O** | U | U | — | — | U | U | U | U | U |
| Search / embeddings / RAG | — | **O** | — | U | — | — | — | — | — | U | U |
| Product registry | U | — | **O** | — | — | U | U | U | U | — | U |
| Compliance / licence / domain registry | — | U | **O** | — | — | — | — | U | U | — | U |
| Model gateway & routing | **O** | — | U | U | — | — | — | — | U | — | — |
| Prompt / context assembly | — | U | — | **O** | — | — | U | — | — | U | U |
| Agents & tool runtime | U | — | U | **O** | U | — | U | — | U | — | U |
| Memory | — | U | — | **O** | — | — | — | — | — | — | U |
| Usage metering & cost | **O** | — | U | U | — | U | U | — | U | — | U |
| Workflow / jobs / schedules | — | — | — | U | **O** | U | U | U | U | U | U |
| Approvals / human-in-the-loop | U | — | U | — | **O** | U | U | U | — | U | U |
| Subscriptions / entitlements / quotas | U | — | U | — | — | **O** | — | — | — | — | U |
| Product membership & profiles | U | — | U | — | — | **O** | — | — | — | — | U |
| Development work graph | — | U | U | U | U | — | **O** | U | — | — | U |
| Worker orchestration & isolation | — | — | — | U | U | — | **O** | U | — | — | — |
| Build / test / integration records | — | U | — | — | — | — | **O** | U | U | — | — |
| Repositories / git / branches | — | — | U | — | — | — | U | **O** | — | — | — |
| CI/CD / artifacts / environments | — | — | U | — | — | — | U | **O** | U | — | — |
| Deployment & release promotion | — | — | U | — | U | — | U | **O** | U | — | — |
| Observability / logs / metrics / traces | U | — | — | U | U | — | U | U | **O** | — | U |
| Incidents / alerts / health | — | — | U | — | U | — | U | U | **O** | — | U |
| Conversation engine | — | U | — | U | — | — | U | — | — | **O** | U |
| Chat / voice / realtime UX | — | — | — | U | — | U | U | — | — | **O** | U |
| Forms / commands / search UX | — | U | — | — | U | U | U | — | — | **O** | U |
| Domain business logic | — | U | U | U | U | U | U | — | U | U | **O** |

---

## Part 6 — Data ownership matrix

The rule from §9, stated precisely:

> **The domain owns the structured fact. Data & Knowledge owns its document representation. Never both, and never neither.**

Worked example — a milestone:

| Thing | Owner | Form |
|---|---|---|
| That milestone `M-A-P3` exists, is blocked by `M-A-D2`, has 7 of 10 work items complete | **07 Developer** | Structured rows |
| The written specification describing what `M-A-P3` is for | **02 Data & Knowledge** | Document, versioned, linked to `M-A-P3` by ID |
| That the *product* this milestone belongs to is registered, owned by Durai, classified internal | **03 Governance** | Structured rows |
| The branch the work happened on, the artifact it produced, the environment it deployed to | **08 Delivery** | Structured rows |
| That the deployed thing is healthy and served 4,000 requests | **09 Operations** | Time-series |

### 6.1 Ownership by layer

| Layer | Owns (structured) | Explicitly does **not** own |
|---|---|---|
| **01 Platform** | User, Credential, Session, Organisation, Tenant, Role, Permission, Policy, AuditEntry, SecretRef, UsageRecord, ModelDescriptor | Any product concept. Any development concept. Documents. |
| **02 Data & Knowledge** | Document, DocumentVersion, DocumentMetadata, KnowledgeItem, Source, Reference, Provenance, Embedding, Index, Classification, RetentionPolicy, Lineage | Structured domain facts. It stores the *document about* the fact, never the fact. |
| **03 Governance** | Product, ProductOwnership, ProductLifecycleState, TechnologyRegistryEntry, Brand, Trademark, Domain, Certificate, ComplianceProfile, Licence, ExternalService, ConfigurationRegistryEntry | What is being built (07). How it ships (08). How it runs (09). |
| **04 Intelligence** | Agent, AgentCapability, Prompt, PromptVersion, ModelRoute, MemoryRecord, TurnTrace, Evaluation, Guardrail | Product data. Conversation storage (10/11 own that). Documents (02). |
| **05 Automation** | WorkflowDefinition, WorkflowInstance, Rule, Trigger, Schedule, Job, Queue, Approval, Escalation, WorkflowResult | The business meaning of what it runs. |
| **06 Shared Product Foundation** | ProductProfile, ProductMembership, Plan, Subscription, Entitlement, FeatureFlag, Quota, ProductSetting, Preference, OnboardingState | Identity (01). Domain data (11). |
| **07 Developer** | ProductDevelopment, Module, Feature, Requirement, Release, Milestone, WorkItem, Task, Subtask, Dependency, Worker, WorkerAssignment, DevelopmentRun, BuildRecord, TestRun, Review, IntegrationRun, DevelopmentResult, ProgressState | Product *identity* (03). Repository and CI *mechanics* (08). Runtime health (09). Specification documents (02). |
| **08 Delivery** | Repository, Branch, Tag, Commit, Artifact, Pipeline, PipelineRun, Environment, Deployment, InfrastructureResource, BackupRecord | The *meaning* of a build (07 interprets it). Runtime health (09). |
| **09 Operations** | LogStream, Metric, Trace, HealthCheck, Incident, Alert, PerformanceRecord, CapacityRecord, CostRecord | Anything durable about what was built or why. |
| **10 Experience** | ConversationView state, UIPreference, NotificationDelivery, CommandDefinition | Conversation content (11 owns the store; 10 renders it). |
| **11 Products** | Everything domain-specific: Workspace, Project, Conversation, ConversationMessage, Knowledge, and every future business entity | Anything a lower layer owns. |

---

## Part 7 — The eleven layers

### 7.1 Layer 01 — Platform

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

**Minimum V1 (Foundation Gate scope):**
- Real identity: user, credential, session, sign-in, token issuance and validation
- Organisation and tenant with enforced isolation
- Role and permission with a working `IAuthorizationService`
- Durable `IAuditLog` (replacing `ConsoleAuditLog`)
- Durable `IUsageMeter` (replacing `InMemoryUsageMeter`)
- `ISecretResolver` backed by real configuration/key vault
- `Nexus.Platform.Persistence` made real enough to host the above

**Future (post-gate):** federated / SSO identity, fine-grained ABAC policies, notification transport, event bus, multi-region tenancy, Anthropic and further providers, tool gateway implementation.

---

### 7.2 Layer 02 — Data & Knowledge

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

**Minimum V1 (Foundation Gate scope) — this is the ADR-014 work:**
- Azure SQL as the single persistence backend; Dataverse removed entirely (ADR-014 Stages 1b, 2a, 2b, 2c, 3)
- EF Core code-first discipline: domain class → `IEntityTypeConfiguration` → migration → DDL
- The `Id`/`Seq`/`Ref` pattern (ADR-014 Rule 4) and SQL schemas replacing prefixes (Rule 6)
- Document entity with versioning, sufficient to hold this report and the numbered doc set
- Documents linkable by ID to structured records in other layers

**Explicitly NOT in V1:** embeddings, vector retrieval, RAG, lineage, retention policies, classification. Keyword ranking (which already exists in Intelligence) is sufficient until there is enough content for retrieval quality to be measurable.

**Future:** embeddings and vector search, semantic retrieval, document sync from external sources, full lineage and retention governance.

---

### 7.3 Layer 03 — Governance & Registry

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

### 7.4 Layer 04 — Intelligence

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

**Minimum V1 (Foundation Gate scope):**
- Durable `ITurnTraceStore` and `IResultReportStore` — currently in-memory, and without them the Result Loop cannot exist and Developer cannot explain why it made a call
- Durable `IMemoryStore`
- Citations verified end to end through the frontend (F3 built the UI; it has never been proven against a live model because of the OpenAI credit block)
- `DeveloperAgent` made real enough to reason about the work graph

**Explicitly NOT in V1:** multi-provider routing beyond OpenAI, embeddings, RAG, evaluation harness, guardrail framework, tool runtime.

**Future:** Anthropic / Google / OpenRouter / DeepSeek / Z.ai / local models; model routing by cost and latency class; prompt versioning and A/B evaluation; a real tool registry and runtime; evaluation-driven improvement.

---

### 7.5 Layer 05 — Automation & Workflow

**Purpose.** Execute reliable, repeatable processes — with or without AI involvement.

**Why it exists.** *Intelligence reasons; Automation executes.* Without this separation, every scheduled job and every approval chain gets built into whatever product needed it first, and reliability becomes a property of individual features rather than of the system. It also gives AI-proposed actions somewhere deterministic to land.

**Owns:** workflow definitions and instances, rules, triggers, schedules, conditions, state machines, queues, jobs, retries, approvals, human-in-the-loop gates, escalations, event handlers, process orchestration, workflow results.

**Does NOT own:** the business meaning of what it runs. A workflow that approves a purchase order does not know what a purchase order is.

**Sub-layers:** Definition · Execution engine · Scheduling & triggers · Queue & job runtime · Approvals & human-in-the-loop · Results.

**May depend on:** 01, 02, 03, 04.

**Minimum V1 — post-gate.** Not a gate blocker. Developer V1a coordinates work through explicit state transitions, not a workflow engine. When Developer V1b begins dispatching workers autonomously, Automation becomes the right home for retry, escalation and approval — that is the natural trigger for building it.

**Future:** full state-machine definitions, durable queues, compensation, saga patterns.

---

### 7.6 Layer 06 — Shared Product Foundation

**Purpose.** Provide reusable product-level capability so no product rebuilds membership, subscriptions, entitlements, quotas, settings or onboarding.

**Why it exists.** The distinction from Platform is subtle and important. Platform owns *who you are* — one identity across all of Nexus. Shared Product Foundation owns *who you are within a product* — your Vault profile, your Trips traveller profile, your Business OS user context. One identity, many product contexts.

**Owns:** product membership, product profile framework, subscription framework, plans, entitlements, feature access, usage metering at product level, quotas and limits, product settings and preferences, onboarding framework, product notifications, product lifecycle hooks, product audit context.

**Does NOT own:** identity (01), domain data (11), or product identity in the registry sense (03).

**Sub-layers:** Membership & profiles · Subscription & entitlement · Quota & metering · Settings & preferences · Onboarding · Product notifications.

**May depend on:** 01, 02, 03.

**Minimum V1 — post-gate.** Not a gate blocker: there is one product with one user. It becomes urgent the moment a second product or a second user exists, which is early in Stream A.

**Future:** billing integration, plan migration, entitlement inheritance across product bundles.

---

### 7.7 Layer 07 — Developer

**Purpose.** Define, plan, build, test, review and coordinate software development — and become the structured system of record for development state, replacing chat transcripts and markdown.

**Why it exists.** Two reasons. Immediately: development state currently lives in conversation and is lost between sessions. Strategically: Nexus cannot build Nexus, and cannot coordinate simultaneous workers, without a machine-readable model of what is being built, what depends on what, and what is safe to run in parallel.

#### A recommended departure from the brief

The brief lists Developer as architectural layer 07. I think it is **both a layer capability and a product**, and that separating them matters:

- **Developer Runtime** — dependency analysis, parallel-safe scheduling, worker isolation, run orchestration. This is reusable machinery. Business Systems in Stream A will want it. It belongs in `NexusAI` as a Platform-adjacent capability.
- **Developer Product** — the work graph as a system of record, the boards, the dashboards, the developer chat, the approval UI. This is a product built on Nexus, exactly like Chat. It belongs in a new `Nexus.Dev` repository as Layer 11.

This is consistent with the brief's own §28: *"Nexus Continuation uses Nexus Developer"* — you use products, you compose layers. And it avoids the trap where Developer, as a "layer", accumulates UI and domain logic that no other layer is allowed to have.

**Decision required from you.** If you prefer Developer to remain a single layer, the roadmap below still works — the milestones do not change, only where the code lands.

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

### 7.8 Layer 08 — Delivery & Infrastructure

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

### 7.9 Layer 09 — Operations

**Purpose.** Keep running systems healthy, secure, observable and recoverable.

**Why it exists.** Nothing is deployed yet, so nothing is operated yet. The layer exists in this architecture to reserve the space, and to stop observability being retrofitted into eleven places once something is finally running.

**Owns:** observability, logs, metrics, tracing, health, performance, diagnostics; incidents, alerts; cost monitoring, capacity; deployment health, feature flags; backup monitoring, recovery, disaster recovery; security monitoring; operational results.

**Does NOT own:** anything durable about what was built or why (07), or how it was shipped (08).

**Sub-layers:** Observability · Health & diagnostics · Incident & alerting · Cost & capacity · Security monitoring · Recovery.

**May depend on:** 01. Cross-cutting otherwise.

**Minimum V1 — post-gate, with one carve-out.** Structured logging with correlation IDs should be added during Platform V1 rather than retrofitted, because retrofitting correlation is disproportionately expensive. Everything else waits for something to actually be deployed.

**Future:** full observability stack, incident management, cost monitoring, feature flags, disaster recovery drills.

---

### 7.10 Layer 10 — Experience / Interaction

**Purpose.** Provide reusable human and system interaction capability, so products compose interaction rather than rebuilding it.

**Why it exists.** The chat UI in `Nexus.Web.Client` is good and it is entirely product-specific. When Developer needs a chat panel and Business OS needs a chat panel, either they are rebuilt twice or the reusable core is extracted. This layer is where it gets extracted.

**Owns:** conversation engine, chat, voice, realtime interaction, commands, search UX, forms, notification UX, human approvals UI, adaptive UI, contextual capability UI, shared experience components.

**Does NOT own:** conversation content storage — the product owns the store, Experience renders it.

**A standing constraint, from §22:** *chat must not become the architecture for Nexus.* Chat is one interaction mode over a system whose structure is defined elsewhere. When something can be modelled as structure or as conversation, model it as structure and let conversation reference it.

**Sub-layers:** Conversation engine · Chat components · Command & search UX · Forms & approvals · Notification UX · Shared component library.

**May depend on:** 01, 02, 04, 06.

**Minimum V1 — post-gate.** Extraction is premature with one consumer. The right trigger is Developer V1a needing a work-graph UI: at that point there are two consumers, and the shared parts become visible rather than guessed at.

**Future:** voice, realtime, adaptive UI, full component library.

---

### 7.11 Layer 11 — Products

**Purpose.** Solve actual user, business and domain problems by composing Nexus capability.

**Generic product architecture:**

```
Product
├── Product Core          — domain-specific identity, context, settings, state
├── Domain Modules        — the actual business capability
└── Capability Integrations — how this product consumes layers 01-10
```

**Product Core** holds what is irreducibly this product's. For Vault: Vault Profile, Household Context, Vault Settings, Vault Preferences, Vault State. Common capability — authentication, generic subscriptions, generic usage, entitlements, onboarding — comes from Layer 06 and is **never duplicated per product**.

**Capability composition, not conditionals.** §26 is right and worth enforcing as an architecture test: no `if (Product == Vault)`. Products declare capability packs:

```
Vault         = Web + Mobile + Desktop + Documents + AI + Security + Offline Sync
Trips         = Web + Mobile + Marketplace + Booking + Maps + Payments + AI
Game          = Game Engine + Gameplay + Assets + Audio + Save + Multiplayer
Machine System = Hardware I/O + Motion + Measurement + Control + Safety + Industrial UI
```

**Current and planned products:** Chat (exists), Developer (Part 13), then Business OS / ERP, Vault, Trips, Career, Education, Truck, Machine Systems, Games.

**Milestone template for any new product:** register in Governance → define Product Core → declare capability packs → model domain modules → integrate Layer 06 for membership and entitlement → integrate Layer 10 for interaction → integrate Layer 04 for AI → define the `ContextBundle` mapper → Delivery pipeline → Operations instrumentation.

---

## Part 8 — Conversation architecture

**Permanent principle:** *Conversation is universal. Structure is contextual.*

The universal core stays lightweight:

```
Conversation · Message · Session · Participant · Attachment
MemoryReference · KnowledgeReference · ToolUsage · ResultReference
```

**Explicitly NOT in the universal core:** Project, Milestone, WorkItem, ADR, Build, Release, Repository, Worker. These belong to contextual systems.

**How context attaches without coupling.** A conversation carries an opaque `ScopeRef` — already the case in `Nexus.Intelligence.Contracts/Turns/ScopeRef.cs`. The product resolves that scope into a `ContextBundle` of flattened `ContextItem`s. Intelligence never learns what a `Milestone` is; it receives a `ContextItem` with `Kind = Objective` and a `Body`.

**Current state and required change.** `Conversation` lives in `Nexus.Products.Chat.Domain` with `ConversationType` and `ConversationVisibility`. That was correct when there was one product. When Developer needs conversations, the universal core must be extracted to Layer 10 and the Chat-specific enums must stay in Chat. This is not gate-blocking and should happen when Developer V1a needs its first conversation.

---

## Part 9 — State, progress, release and environment models

### 9.1 Product state is not one field

Replacing the single `Status` enum (§3.6, §10):

| Dimension | Owner | Example values | Derived? |
|---|---|---|---|
| `ProductLifecycleState` | 03 Governance | Proposed, Active, Sunsetting, Retired | Manual |
| `DevelopmentStage` | 07 Developer | Not Started, In Design, In Development, Stabilising, Complete | **Derived** from milestone states |
| `CurrentRelease` | 07 Developer | Release ref | Manual |
| `CurrentProductionRelease` | 08 Delivery | Release ref actually deployed to Production | **Derived** from deployment records |
| `DevelopmentHealth` | 07 Developer | Healthy, At Risk, Blocked | **Derived** from blocked work items and failing builds |
| `DeploymentState` | 08 Delivery | Not Deployed, Deploying, Deployed, Failed, Rolled Back | **Derived** |
| `OperationalHealth` | 09 Operations | Healthy, Degraded, Down | **Derived** from health checks |
| `ComplianceState` | 03 Governance | Compliant, Exception, Non-Compliant | Manual with evidence links |

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

This is what the Foundation Gate exists to deliver, so it deserves precision.

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

**Phase 2 — machine-readable roadmap.** Alongside this document, a `roadmap.yaml` expressing milestones, work items, dependencies and file scopes in the exact shape of Developer's schema. Hand-maintained, but *parseable*. This is the bridge and it should be produced as part of Developer V1a milestone `A-D1`, not after it — because it doubles as the schema's first real test case.

**Phase 3 — Developer as system of record.** `roadmap.yaml` is imported into Developer's tables. Developer becomes authoritative for structured development state. Documents move to Layer 02 and link to Developer records by ID. This document becomes a `Document` in Data & Knowledge, versioned, referenced by the milestones it describes.

**The rule that keeps them honest:** after Phase 3, a structured fact has exactly one home. If completion percentage is in Developer, it is not also in markdown. Documents describe and explain; they do not duplicate state.

---

## Part 12 — The Foundation Gate

### 12.1 Definition

> **The Nexus Foundation Gate is closed when three independent work items can be planned, isolated, built, tested, reviewed and integrated simultaneously, with every step recorded in structured Developer data, against a system with real identity, a single persistence backend, and automated build and test verification.**

Past this point, Stream A (Business Systems) and Stream B (Nexus Continuation) run in parallel.

### 12.2 The correction: Delivery is mandatory before the gate

§6 places Delivery & Infrastructure in Stream B, after the gate. §33 requires Developer V1 to demonstrate independent build, independent test, result capture and controlled integration.

These cannot both stand. "Independent build and test" without CI means a human building three branches by hand on one machine and typing results into Developer — which is not isolation, does not scale past one machine, and produces no evidence trail. The current evidence makes this worse, not better: `.github/workflows/` is empty, `Nexus.Platform.Tests` has no tests, and the repositories lost their history two days ago.

**Recommendation: a minimal Layer 08 slice moves before the gate.** Scope is small — a GitHub Actions workflow per repository, branch protection, architecture tests as a hard gate, and results in an ingestible form. Roughly one week. Without it, the gate's own acceptance test cannot be run honestly.

### 12.3 Capability classification

**MANDATORY BEFORE FOUNDATION GATE**

| Layer | Capability | Why mandatory |
|---|---|---|
| 01 | Real identity: user, credential, session, token | Every ownership, assignment, review and approval needs a subject |
| 01 | Organisation / tenant with enforced isolation | Replaces the row-level security ADR-014 Stage 3 removes |
| 01 | Roles, permissions, working authorization service | Same |
| 01 | Durable audit log | Parallel work without an audit trail is unreviewable |
| 01 | Durable usage meter | Cost of parallel AI workers is otherwise unbounded and invisible |
| 01 | Real secret resolver | Prerequisite for CI |
| 02 | Azure SQL migration complete, Dataverse removed (ADR-014 1b→3) | Two persistence stacks double the surface of every parallel change |
| 02 | EF migration discipline + `Id`/`Seq`/`Ref` + schema convention | Parallel migrations collide without a convention |
| 02 | Document entity with versioning | Milestones must link to specifications |
| 04 | Durable turn trace + result report store | The Result Loop and Developer's reasoning evidence |
| 04 | Durable memory store | Context does not survive a restart otherwise |
| 04 | Citations verified against a live model | F3 built the UI; it has never been proven |
| **08** | **CI per repository: restore, build, test, results** | **§33's acceptance test is unrunnable without it** |
| **08** | **Branch protection + architecture tests as a hard gate** | **The only thing stopping parallel workers breaking boundaries** |
| **08** | **Antivirus exclusion verified + documented backup** | **Three repositories lost history 48 hours ago** |
| 03 | Minimal `Product` record only | Developer's work graph needs something to hang off |
| 07 | Developer V1a in full (Part 13) | The gate *is* this |
| 09 | Structured logging with correlation IDs | Disproportionately expensive to retrofit |

**CAN BE BUILT AFTER FOUNDATION GATE** — Governance registries beyond `Product`; Automation & Workflow entirely; Shared Product Foundation entirely; Experience extraction; Delivery beyond CI (artifacts, environments, deployment, IaC); Operations beyond structured logging; Developer V1b (autonomous dispatch); Requirements and Releases; multi-provider model routing; embeddings, vector retrieval and RAG.

**FUTURE** — Evaluation harness and guardrail framework; capability packs and technology profiles; product designer and schema designer; developer chat and dashboards; voice and realtime; disaster recovery automation; drift detection; Machine Systems safety architecture.

**OPTIONAL** — Anthropic and additional providers (OpenAI suffices to prove the routing abstraction); local models; multi-region; advanced self-healing.

### 12.4 What the gate deliberately excludes

No workflow engine, no subscription framework, no observability stack, no deployment pipeline, no agents beyond `DeveloperAgent`, no RAG, no second AI provider, no reusable component library, no product designer. Each is genuinely useful and none is required to prove that three workers can work at once without corrupting each other.

---

## Part 13 — Roadmap A: Initial Nexus Foundation

Fourteen milestones. The critical path is `A-X1 → A-D2 → A-P1 → A-D3 → A-V1`, and the gate closes at `A-V1`.

### 13.1 Milestone register — Roadmap A

| ID | Layer | Milestone | Depends on | Parallel with | Est. |
|---|---|---|---|---|---|
| `A-X1` | 08 | CI, branch protection, backup safety | — | `A-D1`, `A-K1` | 1 wk |
| `A-D1` | 02 | Complete Azure SQL migration (ADR-014 1b→2c) | — | `A-X1`, `A-K1` | 2 wk |
| `A-D2` | 02 | Delete Dataverse (ADR-014 Stage 3) | `A-D1` | `A-K1` | 3 d |
| `A-K1` | 04 | Durable Intelligence stores | — | `A-X1`, `A-D1` | 1 wk |
| `A-K2` | 04 | Verify citations against a live model | `A-K1` | `A-P1` | 2 d |
| `A-P1` | 01 | Real identity, tenancy, authorization | `A-D2` | `A-K2` | 3 wk |
| `A-P2` | 01 | Durable audit, usage, secrets | `A-P1` | `A-G1` | 1 wk |
| `A-P3` | 09 | Structured logging with correlation IDs | `A-P1` | `A-G1`, `A-D3` | 3 d |
| `A-G1` | 03 | Minimal Product record | `A-P1` | `A-P2`, `A-D3` | 2 d |
| `A-D3` | 02 | Document entity with versioning | `A-D2` | `A-P2`, `A-G1` | 1 wk |
| `A-V1` | 07 | **Developer V1a — work graph & system of record** | `A-G1`, `A-D3` | — | 2 wk |
| `A-V2` | 07 | Developer V1a — dependency & parallel-safe analysis | `A-V1` | `A-V3` | 1.5 wk |
| `A-V3` | 07 | Developer V1a — worker, run, build, test capture | `A-V1`, `A-X1` | `A-V2` | 1.5 wk |
| `A-V4` | 07 | Developer V1a — review, integration, derived progress | `A-V2`, `A-V3` | — | 1 wk |
| **GATE** | — | **§33 acceptance test passes** | `A-V4` | — | 3 d |

Rough elapsed time with sensible parallelism: **11–13 weeks.**

### 13.2 Full drill-down — the gate-critical milestones

---

#### `A-X1` — CI, branch protection and backup safety

| Field | Value |
|---|---|
| **Owning layer** | 08 Delivery & Infrastructure |
| **Purpose** | Make build and test results automatic, trustworthy and machine-readable, so parallel work can be verified without a human on one laptop. |
| **Responsibility addressed** | 08: build infrastructure, CI, branch policies, backup. |
| **Capabilities delivered** | Automated verification; boundary enforcement; recoverability. |
| **Outcome** | §33's "independent build / independent test / result capture" becomes runnable. |
| **Scope** | GitHub Actions per repo (restore, build, test, publish results); branch protection on `main`; NetArchTest wired as a hard gate; results as JSON artifacts; AV exclusion verified; documented backup. |
| **Out of scope** | Artifact registry, environments, deployment, IaC, cloud provisioning. |
| **Blocking dependencies** | None. Start immediately. |
| **Parallel with** | `A-D1`, `A-K1` |
| **Work items** | `WI-X1.1` CI workflow — NexusAI · `WI-X1.2` CI workflow — Nexus.Int · `WI-X1.3` CI workflow — Nexus.Web (incl. frontend build) · `WI-X1.4` Branch protection, all three · `WI-X1.5` Architecture tests as hard gate · `WI-X1.6` Machine-readable result artifact · `WI-X1.7` AV exclusion + backup verification |
| **Sample tasks** | `T-X1.1.1` Add `.github/workflows/build.yml` with .NET 10 setup, restore, build `Nexus.AI.slnx` · `T-X1.1.2` Add test step with `--logger trx` and result upload · `T-X1.3.1` Create `.github/` in `Nexus.Web` and `Nexus.Int` — **neither repository has one** · `T-X1.5.1` Fail the pipeline on any boundary violation · `T-X1.6.1` Delete `Nexus.Platform.Tests` or give it a real test — an empty test project passing in CI is a false green · `T-X1.7.1` Verify `C:\Personal` exclusion in Windows Security and record the result · `T-X1.7.2` Remove `.git-broken\` from all three repositories once backups are confirmed |
| **Data introduced** | None (results are files until `A-V3`). |
| **Infrastructure impact** | GitHub Actions minutes; branch protection rules. |
| **Security impact** | CI needs a secret for the local NuGet feed or the feed must move to GitHub Packages. **Provider API keys must never enter CI** — no test requires a live model. |
| **Tests** | The pipeline itself. Prove it by pushing a deliberate boundary violation and confirming a red build. |
| **Acceptance criteria** | 1. Push to any of three repos triggers restore, build, test. 2. `main` cannot be pushed to directly. 3. A PR with a boundary violation cannot merge. 4. Results downloadable as structured artifacts. 5. AV exclusion confirmed with a screenshot in the runbook. 6. `Nexus.Platform.Tests` contains at least one real test, or is deleted. 7. `.git-broken\` removed from all three repositories. |
| **Definition of done** | All six criteria met; runbook updated; a deliberately-failing PR demonstrated and closed. |
| **Rollback** | Delete workflow files; remove branch protection. Zero risk to source. |
| **Risks** | Local NuGet feed is not reachable from CI — likely, and the reason `WI-X1.1` is first. Mitigation: publish Platform and Intelligence packages to GitHub Packages. |
| **Parallelisation** | 3 workers (one per repo) after `WI-X1.1` establishes the pattern. |
| **Human approval** | Branch protection settings only. |
| **Evidence required** | Green pipeline URLs for all three repos; one demonstrated red build. |

---

#### `A-D1` — Complete the Azure SQL migration

| Field | Value |
|---|---|
| **Owning layer** | 02 Data & Knowledge |
| **Purpose** | One persistence backend, one migration discipline, one naming convention — so parallel schema work is possible at all. |
| **Responsibility addressed** | 02: structured data access, migration discipline. |
| **Outcome** | Ten remaining aggregates on Azure SQL under the `Id`/`Seq`/`Ref` and schema conventions. |
| **Scope** | ADR-014 Stages 1b (commit — **already built, uncommitted**), 2a (Project, Conversation, ConversationMessage), 2b (Knowledge, Adr, WorkItem, Artifact), 2c (Session, Branch, Snapshot). |
| **Out of scope** | Dataverse deletion (`A-D2`); new tables (Roadmap C); documents (`A-D3`). |
| **Blocking dependencies** | None. Prompts already written: `SQL_PROMPTS_STAGE_1B_2A.md`, `SQL_PROMPTS_STAGE_2B_2C.md`. |
| **Work items** | `WI-D1.0` **Commit and push Stage 1b — built and proven, do this today** · `WI-D1.1` Stage 2a · `WI-D1.2` Stage 2b · `WI-D1.3` Stage 2c |
| **Data impact** | Ten tables across `org`, `project`, `conversation`, `session`, `knowledge`, `work` schemas. |
| **Migration impact** | Dataverse data is disposable — schema rewrite, not data migration. |
| **Known hazard** | SQL Server error 1785, multiple cascade paths. Only the owning parent cascades; reference FKs `Restrict`; self-references `NoAction`. `A-D1`'s 2b and 2c stages both hit this; the prompts already carry the warning. |
| **Tests** | Per stage: build, architecture tests, and a round-trip create/read/update through the API. Stage 1b's proof — two successive workspace POSTs returning `WKS-00000001` then `WKS-00000002` — is the pattern for every aggregate with a `Ref`. |
| **Acceptance criteria** | 1. All eleven aggregates resolve to SQL with `Nexus:Persistence=Sql`. 2. Every aggregate round-trips through its endpoints. 3. `Ref` allocation proven sequential per aggregate. 4. One clean migration chain, no orphaned migrations. 5. Architecture tests green. |
| **Parallelisation** | **Sequential — `Must be sequential`.** Every stage adds a migration to the same `DbContext`; the model snapshot conflicts even when tables differ. This is rule 3 in §10.2, and this milestone is the canonical example. |
| **Evidence required** | Commit SHA and green CI per stage. |

---

#### `A-P1` — Real identity, tenancy and authorization

| Field | Value |
|---|---|
| **Owning layer** | 01 Platform |
| **Purpose** | Give the system a real subject, so ownership, assignment, review, approval and audit mean something — and replace the authorization that leaves with Dataverse. |
| **Responsibility addressed** | 01: identity, authentication, sessions, organisations, tenancy, roles, permissions, policy. |
| **Outcome** | Every action attributable to a real user in a real tenant, with enforced isolation. |
| **Scope** | User, Credential, Session; sign-in, token issue and validate; Organisation, Tenant with enforced isolation; Role, Permission; a working `IAuthorizationService`; replace `ChatTurnIdentity`'s hardcoded tenant and placeholder permissions. |
| **Out of scope** | SSO/federation, ABAC, MFA, self-service registration, password reset flows. |
| **Blocking dependencies** | `A-D2` — identity tables land in SQL, and it is wasteful to build them twice. |
| **Work items** | `WI-P1.1` Identity domain + schema · `WI-P1.2` Authentication (sign-in, token issue/validate) · `WI-P1.3` Organisation & tenant with isolation · `WI-P1.4` Roles, permissions, authorization service · `WI-P1.5` Replace `ChatTurnIdentity` · `WI-P1.6` Wire authorization into all Chat endpoints |
| **Data introduced** | `identity` schema: User, Credential, Session, Organisation, Tenant, Role, Permission, RoleAssignment. |
| **Security impact** | **This is the highest-risk milestone in Roadmap A.** Credential storage, token signing, tenant isolation. Requires the most careful review of anything in the roadmap. |
| **Frontend impact** | Sign-in page; token handling in `ApiClient`; 401/403 handling in `ApiError`; auth-aware routing. |
| **Tests** | Unit: password hashing, token validation, permission evaluation. Integration: cross-tenant access must be denied — **this is the single most important test in the foundation.** |
| **Acceptance criteria** | 1. A real user signs in and receives a valid token. 2. Every `/api/v1` endpoint rejects unauthenticated requests. 3. A user in tenant A cannot read tenant B's data, proven by test. 4. Permissions are evaluated, not stubbed. 5. `ChatTurnIdentity` returns real values. 6. Audit entries carry a real user ID. |
| **Rollback** | Feature-flag authorization enforcement so it can be disabled without reverting the schema. |
| **Risks** | Scope creep into SSO and MFA — both are explicitly out. Getting tenant isolation subtly wrong is the severe risk; mitigate with the cross-tenant denial test written *first*. |
| **Parallelisation** | `WI-P1.1` sequential (schema); `WI-P1.2`/`WI-P1.3` parallel after it; `WI-P1.5`/`WI-P1.6` parallel after `WI-P1.4`. Peak 2 workers. |
| **Human approval** | **Required** — security-critical. Review token handling and isolation before merge. |

---

#### `A-V1` – `A-V4` — Developer V1a

The four Developer milestones together constitute V1a. Combined drill-down, since they share scope:

| Field | Value |
|---|---|
| **Owning layer** | 07 Developer |
| **Purpose** | Make development state structured, make dependencies explicit, make parallel safety computable, and make every run, build, test, review and integration a record rather than a memory. |
| **Responsibility addressed** | 07: work graph, dependency graph, worker manager, run/build/test capture, review, integration, progress. |
| **Outcome** | The §33 acceptance test can be run and passed. |
| **`A-V1` scope** | `ProductDevelopment`, `Module`, `Milestone`, `WorkItem`, `Task`, `Subtask`, `Dependency` with full CRUD, plus **the `roadmap.yaml` importer that ingests this document's Roadmap A.** |
| **`A-V2` scope** | Dependency graph construction; transitive blocking; file/project scope declaration per work item; the five parallel-safety rules from §10.2; the six-way classification from §32. |
| **`A-V3` scope** | `Worker`, `WorkerAssignment`, `DevelopmentRun`, `BuildRecord`, `TestRun`; worktree path allocation and collision prevention; CI result ingestion from `A-X1`'s artifacts. |
| **`A-V4` scope** | `Review`, `IntegrationRun`, `DevelopmentResult`, `ProgressState`; derived progress with the `BreakdownComplete` honesty rule from §9.2; the minimal UI — work graph view, run view, approve/integrate. |
| **Out of scope (V1b or later)** | Autonomous dispatch, model assignment, per-run cost, Requirements, Releases, product/schema/API/UI designers, capability packs, developer chat, dashboards. |
| **Data introduced** | `dev` schema, 18 tables (Part 13.4). |
| **Frontend impact** | New `Nexus.Dev` client, or a `developer` feature in the existing client if the single-repo option is chosen (Part 21, decision 2). |
| **Tests** | Unit: the parallel-safety algorithm, exhaustively — including the shared-migration case, which is the one most likely to be got wrong. Unit: derived progress including `BreakdownComplete`. Integration: full §33 run. |
| **Acceptance criteria** | The nine rows of §10.4's table, each with stored evidence. |
| **Parallelisation** | `A-V2` and `A-V3` run in parallel after `A-V1` — and should, because that is the first real use of the capability being built. |
| **Human approval** | Required at the gate. |
| **Risks** | **Scope creep is the primary risk in the entire roadmap.** Developer is the most interesting thing to build and the easiest to gold-plate. The V1a/V1b table in §7.7 is the contract; anything not marked V1a waits. |

### 13.3 Compressed drill-down — remaining Roadmap A milestones

| ID | Scope | Acceptance |
|---|---|---|
| `A-D2` | Remove `Microsoft.PowerPlatform.Dataverse.Client`, the `System.Security.Cryptography.Xml` pin, all Dataverse repositories, mappers and the `Nexus:Persistence` switch. | No Dataverse assembly in any build output (−7.2 MB); all endpoints work; architecture tests green. |
| `A-K1` | Replace `InMemoryTurnTraceStore`, `InMemoryResultReportStore`, `InMemoryMemoryStore` with SQL-backed implementations in the `intel` schema. | A turn trace survives a restart; a result report is retrievable by ID; memory persists across sessions. |
| `A-K2` | Add OpenAI credit; run a real chat turn; verify citations populate, usage meters, assistant message persists. | The three smoke tests blocked since the V2.1 migration are closed. Citations visible in the browser. |
| `A-P2` | SQL-backed `IAuditLog` and `IUsageMeter`; `ISecretResolver` over real configuration. | Audit entries queryable by user and date; usage aggregatable by tenant; no secret in `appsettings`. |
| `A-P3` | Serilog or equivalent with correlation ID flowing request → turn → model invocation. | One request traceable end to end by a single correlation ID across Web and Intelligence. |
| `A-G1` | `Product` table: id, name, owner, classification, `ProductLifecycleState`. Move `IProductRegistry` from Platform.Contracts to Governance. | Chat and Developer both registered as products; Developer's work graph references a real `ProductId`. |
| `A-D3` | `Document`, `DocumentVersion`, `DocumentMetadata` in a `knowledge` schema; link table to arbitrary entity IDs. | This report stored as a Document; a Milestone links to it; version history retrievable. |

### 13.4 Developer data model (§38)

Recommended for `A-V1`. Designed so `roadmap.yaml` — and therefore this document — imports directly.

```
Program                 (optional grouping above Phase)
Phase                   → Program
ProductDevelopment      → ProductId (Governance), Module[]
Module                  → ProductDevelopment
Requirement             → Module                          [V1b]
Release                 → ProductDevelopment               [V1b]
Milestone               → ProductDevelopment, Phase, Release?, DocumentRef?
Feature                 → Milestone
WorkItem                → Milestone | Feature, ScopeDeclaration
Task                    → WorkItem
Subtask                 → Task
Dependency              → (FromId, ToId, DependencyKind: Blocking | Parallel | Informational)
ScopeDeclaration        → WorkItem, Projects[], Files[], SchemaContexts[], Contracts[]
Worker                  → CapabilityProfile
WorkerAssignment        → Worker, WorkItem, Branch, WorktreePath, DevelopmentRun
DevelopmentRun          → WorkItem, Worker, StartedAt, EndedAt, RunState
BuildRecord             → DevelopmentRun, PipelineRunRef (Delivery), Outcome
TestRun                 → DevelopmentRun, PipelineRunRef, Passed, Failed, Skipped
Review                  → DevelopmentRun, Reviewer (Platform User), Decision
IntegrationRun          → WorkItem[], TargetBranch, Outcome
DevelopmentResult       → WorkItem, Outcome, EvidenceRefs[]
ProgressState           → any node; Derived, BreakdownComplete flag
StatusHistory           → any node, FromState, ToState, At, By, Reason
```

`ScopeDeclaration` is the table that makes §10.2 computable, and it is the one most likely to be omitted as an afterthought. It should be created in `A-V1`, not `A-V2`.

---

## Part 14 — Roadmap B: Business Systems (register only)

Begins at the Foundation Gate, in parallel with Roadmap C. Drill-down deferred — these milestones will be planned *in Developer*, which is the point.

Sequence is driven by which system has the clearest current business requirement. On present repository and documentation evidence, none of these has a written requirement set, so **ordering below is a proposal requiring your input** (Part 21, decision 4).

| ID | System | Purpose | Depends on | Parallel? | Note |
|---|---|---|---|---|---|
| `B-1` | Business OS / ERP core | Organisation, party, product, document, transaction primitives shared by all business systems | Gate, `A-G1` | No — foundational for B | The other B systems compose this |
| `B-2` | CRM / Field Data | Customer, contact, activity, field capture | `B-1` | Yes | Highest standalone value; least dependent |
| `B-3` | Engine Works | Job, work order, parts, labour, costing | `B-1` | Yes | |
| `B-4` | Retreads | Process, batch, quality, traceability | `B-1` | Yes | Shares much with `B-3`; evaluate merging cores |
| `B-5` | Transport | Vehicle, trip, driver, fuel, maintenance | `B-1` | Yes | |
| `B-6` | Knowledge Systems | Business-facing capture over Layer 02 | `B-1`, Layer 02 post-gate | Yes | Thin — mostly Layer 02 with a business UI |
| `B-7` | Internal Tools | Small operational apps | `B-1` | Yes | Good first test of capability composition |
| `B-8` | Machine Development | Design, spec, build records for machine projects | `B-1` | Yes | |
| `B-9` | Machine Automation | Controlled industrial automation, boring-machine retrofit | `B-8`, Layer 09 | **No** | **Safety-critical.** Deterministic PLC/LinuxCNC owns motion, interlocks and E-stop. AI may plan, diagnose, document, propose parameters — and must never bypass hard limits, emergency stops, operator approval or validated control logic. Requires its own safety architecture before any milestone is written. |

**Recommendation:** start Stream A with `B-1` then `B-2` only. One business system delivered end to end teaches more about the platform than five started.

---

## Part 15 — Roadmap C: Nexus Continuation (register only)

Begins at the Foundation Gate, in parallel with Roadmap B, coordinated by Developer V1a. The `Parallel` column is what Developer will compute; this is the expected answer.

| ID | Layer | Milestone | Depends on | Parallel with | Priority |
|---|---|---|---|---|---|
| `C-1` | 07 | **Developer V1b — autonomous dispatch, model assignment, run cost** | Gate | `C-2`, `C-3` | **Highest** — compounds everything after it |
| `C-2` | 08 | Artifact registry & environment management | Gate | `C-1`, `C-3` | High |
| `C-3` | 08 | Deployment pipeline & release promotion | `C-2` | `C-1`, `C-4` | High — nothing is deployed today |
| `C-4` | 09 | Observability: metrics, tracing, health | `C-3` | `C-1`, `C-5` | High |
| `C-5` | 03 | Governance registries: technology, brand, domain, compliance, licence | Gate | `C-1`, `C-4` | Medium |
| `C-6` | 05 | Automation & Workflow V1: definitions, instances, jobs, approvals | Gate | `C-5`, `C-7` | Medium — trigger is `C-1` |
| `C-7` | 06 | Shared Product Foundation V1: membership, profiles, entitlements | `B-1` starting | `C-5`, `C-6` | Medium — urgent at second product |
| `C-8` | 10 | Experience extraction: conversation core, shared components | Developer UI exists | `C-5`, `C-9` | Medium |
| `C-9` | 02 | Embeddings, vector retrieval, RAG | Gate, `A-D3` | `C-8`, `C-10` | Medium |
| `C-10` | 04 | Multi-provider routing: Anthropic, Google, OpenRouter, DeepSeek | Gate | `C-9`, `C-11` | Low — OpenAI proves the abstraction |
| `C-11` | 04 | Evaluation harness & guardrails | `C-9` | `C-10` | Medium — gates AI quality claims |
| `C-12` | 07 | Developer V2: requirements, releases, designers, capability packs | `C-1` | `C-5`+ | Low |
| `C-13` | 09 | Incidents, alerts, cost monitoring, feature flags | `C-4` | most | Low until deployed |
| `C-14` | 08 | Infrastructure-as-code & disaster recovery | `C-3` | most | Medium — the 2026-08-20 lesson |

**Recommended Stream B opening:** `C-1` alone. Developer V1b makes every subsequent milestone in both streams cheaper, and it is the only milestone with that property.

---

## Part 16 — Entity migration matrix

Every existing entity classified per §35. **`KEEP` dominates: nothing needs to be thrown away.**

### 16.1 Nexus.Web — Chat domain aggregates

| Entity | Target layer | Action | Reasoning |
|---|---|---|---|
| `Workspace` | 11 Products (Chat) | **KEEP** | Chat's own organising concept. Not Platform's `Organisation` — that is tenancy; this is a workspace. |
| `Project` | 11 Products (Chat) | **KEEP** | |
| `Conversation` | 10 Experience (core) + 11 (Chat specifics) | **SPLIT** | Universal core to Layer 10 per §23; `ConversationType`, `ConversationVisibility` stay in Chat. Do it when Developer needs conversations, not before. |
| `ConversationMessage` | 10 Experience | **MOVE** | Part of the universal conversation core. |
| `Knowledge` | 02 Data & Knowledge | **MOVE** | Knowledge is explicitly a Layer 02 concept. Currently a Chat aggregate because Chat was the only product. |
| `Adr` | 02 Data & Knowledge | **MOVE + REFACTOR** | An ADR is a Document with a decision lifecycle. Becomes `Document` + `DecisionRecord` metadata rather than its own aggregate. |
| `WorkItem` | 07 Developer | **MOVE** | Already the right shape; wrong home. Has a `milestone` FK waiting for a `Milestone` that does not exist yet — `A-V1` supplies it. |
| `Artifact` | Split: 07 Developer / 08 Delivery | **SPLIT** | A build output is Delivery's. A work product attached to a work item is Developer's. Currently conflated. |
| `Branch` | 08 Delivery | **MOVE** | Git branch state is Delivery's. Developer *references* it. |
| `Snapshot` | 08 Delivery | **MOVE** | Same reasoning. |
| `Session` | Split: 01 Platform / 07 Developer | **SPLIT** | A user session is Platform's. A development session is Developer's `DevelopmentRun`. Two different things sharing a name. |

**Sequencing note.** Every `MOVE` above is post-gate. Doing them before the gate means moving code while also building on it. `A-D1` migrates all eleven aggregates to SQL *in place*; they move layers afterwards. Exception: `WorkItem` may move during `A-V1` if it proves cheaper than referencing across the boundary.

### 16.2 NexusAI — Platform

| Entity | Target | Action | Reasoning |
|---|---|---|---|
| `Nexus.Platform.Contracts/Models/*` (13 types) | 01 | **KEEP** | Well-designed. |
| `Nexus.Platform.Contracts/Tools/*` | 01 | **KEEP** | Contracts fine; implementation missing. |
| `Nexus.Platform.Contracts/Governance/*` | 01 | **KEEP + EXTEND** | `IAuditLog`, `IUsageMeter`, `IQuotaPolicy` correct. Need durable implementations (`A-P2`). |
| `IProductRegistry` | 03 Governance | **MOVE** | Product registry is Governance, not Platform identity. Move in `A-G1`. |
| `IIdentityService`, `ITenantResolver`, `ResolvedIdentity` | 01 | **KEEP + EXTEND** | Correct shape; `A-P1` makes them real. |
| `ConsoleAuditLog`, `InMemoryUsageMeter`, `PermissiveQuotaPolicy` | 01 | **REPLACE** | Keep as test doubles; replace in production wiring (`A-P2`). |
| `RoutingModelGateway`, `AggregatingModelCatalog` | 01 | **KEEP** | Proves the routing abstraction with one provider. |
| `OpenAIModelGateway` + catalog + options | 01 | **KEEP** | |
| `AnthropicModelGateway` (306 B) | 01 | **KEEP as stub** | Not gate-relevant. `C-10`. |
| `IdentityProvider` (240 B) | 01 | **REPLACE** | `A-P1` supersedes it entirely. |
| `PlatformStore` (308 B) | 01 | **REPLACE** | `A-P1`/`A-P2` supersede it. |
| `ToolProvider` (231 B) | 01 | **KEEP as stub** | Post-gate. |
| `NexusAI.Agents`, `.Api`, `.Core`, `.Domain`, `.Foundation`, `.Host`, `.Infrastructure` | — | **REMOVE** | Empty gitignored husks from V2.1. Deleting them removes ambiguity about where code goes. Low risk, do during `A-X1`. |
| `NexusAI.Application/{Agents,Orchestration,WorkItem}` | — | **REMOVE** | Same. |

### 16.3 Nexus.Int — Intelligence

| Entity | Target | Action | Reasoning |
|---|---|---|---|
| `Nexus.Intelligence.Contracts/Turns/*` (17 types) | 04 | **KEEP** | The best contract surface in the system. Do not touch. |
| `Nexus.Intelligence.Contracts/Context/*` | 04 | **KEEP** | `ContextBundle`/`ContextItem`/`TrustLevel` is the seam. Protect it. |
| `TurnPipeline` and its ten steps | 04 | **KEEP** | Working. |
| `InMemoryTurnTraceStore` | 04 | **REPLACE** | `A-K1`. |
| `InMemoryResultReportStore` | 04 | **REPLACE** | `A-K1`. |
| `InMemoryMemoryStore` | 04 | **REPLACE** | `A-K1`. |
| `KeywordContextRanker` | 04 | **KEEP + EXTEND** | Sufficient until `C-9` adds embeddings. Keep as a fallback afterwards. |
| `PromptAssembler` | 04 | **KEEP** | |
| `AgentRegistry`, `AgentDispatcher`, agent abstractions | 04 | **KEEP** | Right shape. |
| `DeveloperAgent` (974 B) | 04 | **EXTEND** | Becomes real in `A-V2`/`A-V4` — the agent that reasons about the work graph. |
| `EmptyToolCatalog`, `EmptyToolGateway` | 04 | **KEEP as stub** | Honest placeholders. Post-gate. |
| `Planner`, `ExecutionEngine` | 04 | **KEEP** | |

### 16.4 Deprecated

| Item | Action | When |
|---|---|---|
| `Microsoft.PowerPlatform.Dataverse.Client` + all Dataverse repositories/mappers | **REMOVE** | `A-D2` |
| `System.Security.Cryptography.Xml` version pin | **REMOVE** | `A-D2` (only existed for Dataverse) |
| `Nexus:Persistence` configuration switch | **REMOVE** | `A-D2` |
| `.git-broken\` in all three repositories | **REMOVE** | After `A-X1` confirms backups |
| `ChatTurnIdentity` hardcoded tenant/permissions | **REPLACE** | `A-P1` |

---

## Part 17 — Parallelisation matrix

Per §32. This is also `A-V2`'s first real test case — it should reproduce this table from the declared dependencies.

| Milestone | Classification | Conflicts with | Reason |
|---|---|---|---|
| `A-X1` | **Can run now** | — | No source conflict; config only |
| `A-D1` | **Can run now** | — | |
| `A-K1` | **Can run now** | — | Different repository from `A-D1` |
| `A-X1` + `A-D1` + `A-K1` | **Can run together** | — | Three repos, three workers. The natural first parallel proof. |
| `A-D1` stages 1b→2c internally | **Must be sequential** | each other | Shared `DbContext` model snapshot — §10.2 rule 3 |
| `A-D2` | **Waiting for dependency** | — | Needs `A-D1` |
| `A-K2` | **Waiting for dependency** | — | Needs `A-K1` + OpenAI credit |
| `A-P1` | **Waiting for dependency** | — | Needs `A-D2` |
| `A-P1` + `A-K2` | **Can run together** | — | Different repositories |
| `A-P2` + `A-G1` | **Can run together** | — | Different schemas, no shared migration |
| `A-P3` | **Can run together** with `A-P2`, `A-G1` | — | Cross-cutting config, no schema |
| `A-D3` | **Can run together** with `A-P2`, `A-G1` | — | Different schema |
| `A-P2` + `A-D3` + `A-G1` + `A-P3` | **High conflict risk** if all four at once | each other | Four concurrent migrations across two contexts. Cap at 2 workers touching schema. |
| `A-V1` | **Waiting for dependency** | — | Needs `A-G1`, `A-D3` |
| `A-V2` + `A-V3` | **Can run together** | — | `A-V2` is algorithm, `A-V3` is persistence + CI ingestion. Distinct scopes. |
| `A-V4` | **Waiting for dependency** | — | Needs both |
| `B-1` + `C-1` | **Can run together** | — | The whole point of the gate |
| `B-2`…`B-8` | **Can run together** after `B-1` | — | Distinct domains |
| `B-9` | **Must be sequential**, gated | — | Safety-critical |
| `C-2` → `C-3` → `C-4` | **Must be sequential** | — | Artifacts before deployment before observing deployments |

**Recommended worker count:** 3 for `A-X1`/`A-D1`/`A-K1`. Then 2 through the identity phase (schema conflict risk). Then 2 for `A-V2`/`A-V3`. Post-gate, 4–6 across both streams.

---

## Part 18 — Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | **Developer V1 scope creep** | **High** | **High** — the gate never closes | The V1a/V1b table in §7.7 is the contract. Anything not marked V1a waits, without exception. |
| 2 | Another environmental loss of git objects | Medium | Severe | `A-X1` verifies AV exclusion and documents backups. Push at every stage boundary. |
| 3 | Tenant isolation subtly wrong in `A-P1` | Medium | **Severe** — silent cross-tenant data exposure | Write the cross-tenant denial test before the implementation. Human review mandatory. |
| 4 | Bootstrapping trap — Developer defect halts both streams | Medium | High | V1a is advisory: humans and coding agents still execute. A Developer outage costs a dashboard, not a capability. |
| 5 | Parallel migration conflicts corrupt the model snapshot | **High** if unmanaged | Medium | §10.2 rule 3 is explicit and enforced by `A-V2`. Cap schema-touching workers at 2. |
| 6 | Business systems never start because the foundation keeps growing | Medium | **High** — this is the failure mode that kills platform projects | The gate is defined, dated and small. Anything not in §12.3's MANDATORY list is not allowed to delay it. |
| 7 | Derived progress reports confident nonsense | High if unmanaged | Medium | The `BreakdownComplete` rule in §9.2. |
| 8 | Test debt compounds — 4 test files today | **High** | High | Every work item's Definition of Done includes a test. `A-X1` makes the absence visible. |
| 9 | Local NuGet feed blocks CI | High | Low | Move Platform/Intelligence packages to GitHub Packages in `WI-X1.1`. |
| 10 | This document goes stale and diverges from reality | **High** | Medium | Part 11's Phase 2: `roadmap.yaml` produced during `A-V1` and imported, so the structured facts have one home. |
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
- **Embeddings and RAG.** Keyword ranking is sufficient until there is enough content for retrieval quality to be measurable — and until `A-K2` proves citations work at all.
- **Workflow engine.** Explicit state transitions until V1b needs retry and escalation.
- **Subscription and billing.** One user, one product.

---

## Part 20 — Immediate next milestone

**Before anything in this report is approved:**

**Commit and push SQL Stage 1b.** It is complete, its acceptance test has run and passed (§2.7), and it has sat uncommitted in `Nexus.Web` since 2026-08-20 18:08 UTC — in a repository that lost its entire object database forty-eight hours ago. This is `WI-D1.0` and it should not wait for a roadmap decision.

**On approval, the first milestone is `A-X1` — CI, branch protection and backup safety.**

It is first for three reasons: it has no dependencies and can start immediately; it is the smallest milestone in Roadmap A at roughly one week; and every subsequent milestone's Definition of Done depends on a green pipeline existing. It also closes the recovery loop from 2026-08-20 by verifying the antivirus exclusion, which has been recommended but never confirmed.

`A-D1` and `A-K1` start in parallel with it — three repositories, three workers, and the first real demonstration that parallel work is possible before Developer exists to coordinate it.

---

## Part 21 — Human decisions required

These block or reshape the roadmap and are yours, not mine.

**1. Does Delivery move before the gate?** (§12.2)
Recommended: **yes**, minimal slice, ~1 week. If no, §33's acceptance test must be rewritten to something a single machine can honestly demonstrate — and I would want to understand what that looks like, because I do not currently see a version that means anything.

**2. Is Developer a layer, a product, or both?** (§7.7)
Recommended: **both** — Developer Runtime as a Platform-adjacent capability in `NexusAI`, Developer Product as Layer 11 in a new `Nexus.Dev` repository. Alternative: keep it one layer in one place. The milestones do not change either way, only where code lands. If you want fewer repositories, say so — a `developer` feature inside `Nexus.Web` is defensible for V1a and can be extracted later.

**3. Is the V1a / V1b split acceptable?** (§7.7, §12.2)
Recommended: **yes.** It closes the gate roughly three months earlier and removes the bootstrapping trap. The cost is that Developer does not dispatch workers itself until `C-1`. If you want autonomous dispatch inside the gate, the gate moves out by roughly two months and risk 4 returns.

**4. What is the Business Systems priority order?** (§27, Part 14)
No repository or documentation evidence exists for business requirements, so Part 14's ordering is a proposal. Which system has a real, current business need? Recommended: `B-1` then `B-2` only — one system end to end before starting a second.

**5. Where does Layer 02's document half live?**
`A-D3` puts `Document` in the Chat product's database because that is the only database. That is wrong architecturally and cheap now. Options: (a) accept it and extract post-gate, (b) create a shared Nexus database at `A-D3`, (c) new `Nexus.Data` repository. Recommended: **(a)** — do not create a repository before there are two consumers.

**6. When do the Chat aggregates move layers?** (Part 16.1)
Recommended: **post-gate**, all at once, coordinated by Developer — an ideal first real parallel workload. Alternative: move `WorkItem` during `A-V1` if referencing across the boundary proves awkward.

**7. Confirm the Foundation Gate scope.** (§12.3)
The MANDATORY list is nineteen items. Every addition moves the gate and delays business systems. Every removal risks a foundation that needs rework. If anything on that list looks wrong to you, that is the most valuable disagreement you can have with this document.

---

*Report ends. No code was modified, no migrations run, no entities deleted, no repositories restructured.*
