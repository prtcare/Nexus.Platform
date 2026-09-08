# Layer Model

**Status:** Active — the numbered Platform is settled at TEN layers (v2.2, approved 2026-09-05).
Products and the Development Plane (Nexus Forge) sit OUTSIDE numbered Platform. Most layers have
no code yet and every block below says so
**Owner:** Durai; each layer's own owner maintains its block
**Last updated:** 2026-08-21
**Layer:** cross-cutting
**Authoritative for:** the ten numbered Platform layers plus Products and the Development Plane
(outside numbering) — number (where numbered), short name, long name, purpose, what each owns and
explicitly does not own, its repository, its schema, its projects, its minimum scope before the
Foundation Gate, and the mapping from the older names still present in code and documents.

Not authoritative for: which layer may reference which (`DEPENDENCY_RULES.md`), which layer owns a
named entity (`DATA_OWNERSHIP.md`), or the physical database layout (`DATABASE_ARCHITECTURE.md`).

---

## 1. The memorability test

A layer model that has to be looked up has failed. This is the test:

> **Identity belongs to CORE. Documents belong to DATA. Trademark belongs to GOVERNANCE. Agents
> belong to AI. Workflow belongs to AUTOMATION. Subscriptions belong to SHARED PLATFORM. Git and
> deployment belong to DELIVERY. Acceptance belongs to ASSURANCE. Runtime health belongs to
> OPERATIONS. Conversation belongs to EXPERIENCE. Milestones belong to Nexus Forge. ERP and every
> other product belong to Products.**

Ten numbered Platform nouns, plus two that sit outside the numbering on purpose: Nexus Forge (the
permanent development-plane infrastructure that plans, coordinates and builds software — including
Nexus itself) and Products (real business and customer problems, of which the Nexus Developer
product — the human-facing console over Forge — is one). If a new concept does not obviously slot
next to one of those twelve nouns, it is either misnamed or doing two jobs. That is worth stopping
for, not working around.

**Why Forge and Products are outside the numbering, not layers 07 and 11.** A numbered Platform
layer is infrastructure every product depends on through a stable contract. Forge is that for
*building* Nexus, not for any deployed product's runtime — a product ships without Forge inside it.
Products are the opposite: consumers of the numbered Platform, never a dependency of it. Numbering
either would misstate what actually depends on what.

---

## 2. Short names are primary

Short names are used in code, schemas, documents, milestone IDs and conversation. Long names are
descriptions, not identifiers, and appear only where genuinely clarifying.

| # | Short name (primary) | Long name (descriptive) |
|---|---|---|
| 01 | **CORE** | universal technical foundation |
| 02 | **DATA** | information, documents, knowledge and retrieval |
| 03 | **GOVERNANCE** | registries of products, technology, brand, compliance |
| 04 | **AI** | reasoning, agents, context, models |
| 05 | **AUTOMATION** | workflow and process execution |
| 06 | **SHARED PLATFORM** | reusable product-level capability and scope primitives |
| 07 | **DELIVERY** | source, build, environments, deployment, infrastructure |
| 08 | **ASSURANCE** | prove that requirements have actually been satisfied |
| 09 | **OPERATIONS** | run and observe deployed systems |
| 10 | **EXPERIENCE** | reusable human and system interaction |

**Outside numbered Platform** (not part of the ten above — see §1):

| Name | Long name (descriptive) |
|---|---|
| **Nexus Forge** | the permanent development plane — define, plan, build, test, review and coordinate all Nexus software, including itself |
| **Products** | real business and customer problems, each its own repository/database; the Nexus Developer product (the console over Forge) is one of them |

### 2.1 The old names — TRANSITION

The twelve-layer model with short names is v2.1. Version 2.0 had eleven layers with long names and
different schema names, and **code, assemblies and older documents still carry them.** Reading an
old reference without this table produces the wrong layer.

| Now | Old name | Old number | Old schema | What still uses the old form |
|---|---|---|---|---|
| 01 CORE | Platform | 01 | `identity` | Every assembly: `Nexus.Platform.Contracts`, `.Core`, `.Identity`, `.Persistence`, `.Tools`, `.Providers.*`. **The assembly prefix is not changing** — `Nexus.Platform` is the repository, CORE is the layer |
| 02 DATA | Data & Knowledge | 02 | `knowledge` | `Nexus.Platform.Persistence` (a stub) |
| 03 GOVERNANCE | Governance & Registry | 03 | `governance` | — |
| 04 AI | Intelligence | 04 | `intel` | Every assembly: `Nexus.Intelligence.*`; the repository `Nexus.Intelligence`; the route `/intelligence/v1`. **TARGET** is `Nexus.AI.*` — a rename that is its own work item, never bundled with a behaviour change |
| 05 AUTOMATION | Automation & Workflow | 05 | `automation` | — |
| 06 SHARED PLATFORM | Shared Product Foundation, then PRODUCT CORE (Rebaseline R02 renamed PRODUCT CORE to SHARED PLATFORM; physical `Nexus.ProductCore.*` projects unchanged) | 06 | `product` | — |
| 07 DEVELOPER | Developer | 07 | `dev` | — |
| 08 DELIVERY | Delivery & Infrastructure | 08 | `delivery` | — |
| 09 ASSURANCE | *did not exist* | — | — | Nothing. **New in v2.1**; inserting it renumbered the three layers above |
| 10 OPERATIONS | Operations | **09** | `ops` | Older milestone and matrix references |
| 11 EXPERIENCE | Experience | **10** | `experience` | Older milestone references — conversation-core milestones appear as `M-10-*` in v2.0 text and are `M-11-*` now |
| 12 PRODUCTS | Products | **11** | per-product database | Older feature references — business systems appear as `F-11-8`…`F-11-16` in v2.0 text and are `F-12-8`…`F-12-16` now |

**Rule when the two disagree:** `nexus-roadmap.yaml` and `_FACTS.md` carry the current numbering and
win. A milestone or feature ID quoted anywhere else that maps to layer 09, 10 or 11 under the old
scheme should be checked against the roadmap before it is acted on.

### 2.2 The v2.2 renumbering — TRANSITION

v2.2 (approved 2026-09-05) removes 07 DEVELOPER as a numbered Platform layer and renumbers the three
layers above it. This is a **naming and numbering correction**, not a new capability: every
responsibility DEVELOPER held under v2.1 is retained in full, just relocated to where it actually
belongs given what depends on it and what it depends on.

| Now (v2.2) | v2.1 name | v2.1 number | What changed |
|---|---|---|---|
| **Nexus Forge** (outside numbered Platform) | 07 DEVELOPER | 07 | The development-coordination engine — work graph, dependency graph, worker/worktree model, review and integration, derived progress — is Forge's, in full. See §4a. Forge is **permanent** development-plane infrastructure; it is never described as temporary or retired |
| **Nexus Developer** (a Product, outside numbered Platform) | *(new — did not exist as a separate name under v2.1)* | — | The human-facing console/UX that consumes Forge is its own product, versioned and contracted like any other Forge consumer — it does not reach Forge's data directly |
| 07 DELIVERY | 08 DELIVERY | 08 | Number only. No content change |
| 08 ASSURANCE | 09 ASSURANCE | 09 | Number only. No content change |
| 09 OPERATIONS | 10 OPERATIONS | 10 | Number only. No content change |
| 10 EXPERIENCE | 11 EXPERIENCE | 11 | Number only. No content change |
| **Products** (outside numbered Platform) | 12 PRODUCTS | 12 | Moved outside the numbering rather than renumbered — see §1 for why. Nexus Developer (above) is one of the products living here |

**Rule when the two disagree:** as with §2.1, `nexus-roadmap.yaml` and `_FACTS.md` carry the current
numbering and win. An `M-07-*` milestone ID predating 2026-09-05 refers to the old numbered
DEVELOPER layer and is Forge work under v2.2 — the ID itself is not renumbered, since it is a
historical record of when the milestone was created, not a live layer reference.

---

## 3. How to read each block

`Owns` is stated at **capability** level here. The entity-by-entity ownership table — which layer
owns `Trademark`, `BuildRecord`, `AcceptanceCriterion` — is `DATA_OWNERSHIP.md`, and it is the
authority when the two are read together.

`Projects (TARGET)` is the project set the layer will have. `Today` is what is on disk on
2026-08-21. **Most of the ten numbered layers have no project at all** (Forge and Products,
outside the numbering, are covered in §4a).

`Minimum before the gate` is the slice required by the Foundation Gate — see
`ARCHITECTURE_OVERVIEW.md` §7 for what the gate is, and `nexus-roadmap.yaml` for the milestones.

---

## 4. The ten numbered Platform layers

### 01 — CORE
*universal technical foundation*

**Purpose.** Provide the technical foundation used by Nexus itself and by every product, so no
product ever re-implements identity, tenancy, authorization, audit, secrets or model access.

| | |
|---|---|
| **Repository** | `Nexus.Platform` (renamed from `NexusAI`, 2026-08-24) |
| **Schema** | `core` — **TARGET, `M-02-1.5`** |
| **Projects (TARGET)** | `Nexus.Platform.Contracts`, `.Core`, `.Identity`, `.Authorization`, `.Persistence`, `.Providers.OpenAI`, `.Providers.Anthropic`, `.Tools` |
| **Today** | Contracts and Core are **real**. `Identity` (240 B), `Persistence` (308 B), `Tools` (231 B) and `Providers.Anthropic` (306 B) are **stubs**. `Authorization` does not exist. Governance primitives are in-memory or console only |
| **Owns** | Identity, authentication, sessions, organisations, tenancy, roles, permissions, policy evaluation, audit, secrets resolution, usage metering, model gateway and routing, tool gateway, notification transport, the API and event foundations |
| **Does NOT own** | Any product concept · any development concept · documents · workflow definitions · domain rules · conversation content. **If a CORE type mentions `Workspace` or `Milestone`, the boundary is broken** |
| **Minimum before the gate** | Real identity (user, credential, session, sign-in, token issue and validate) · organisation and tenant with **enforced** isolation · roles, permissions and a working authorization service · durable `IAuditLog` replacing `ConsoleAuditLog` · durable `IUsageMeter` replacing `InMemoryUsageMeter` · `ISecretResolver` backed by real configuration · `Nexus.Platform.Persistence` real enough to host all of it |
| **Deferred** | SSO and federation, MFA, attribute-based policy, notification transport, event bus, multi-region tenancy, the tool gateway implementation, any second model provider |

**The security position.** CORE *is* the security layer. Provider credentials never leave it — AI
asks CORE to invoke a model and never sees a key. `SECURITY_STANDARDS.md` owns the detail.

---

### 02 — DATA
*information, documents, knowledge and retrieval*

**Purpose.** Govern information, documents and reusable knowledge across Nexus, and provide the
structured data-access discipline every other layer builds on.

| | |
|---|---|
| **Repository** | `Nexus.Platform` |
| **Schema** | `data` — **TARGET, `M-02-1.5`**. CURRENT: the only migration on disk created `[org].[Workspace]` |
| **Projects (TARGET)** | `Nexus.Data.Contracts`, `.Core`, `.Persistence`, `.Knowledge`, `.Retrieval` |
| **Today** | Only `Nexus.Platform.Persistence`, a 308-byte stub. The working persistence code is `NexusChatDbContext` inside the Chat product, not in this layer |
| **Owns** | Structured data access patterns and migration discipline · documents and versioning · knowledge items, sources, references, provenance · search, indexing, embeddings, vector retrieval · classification, retention, lineage · import, export and synchronisation. **All documentation belongs here** — architecture documents, standards, specifications, ADRs, manuals, test reports, release notes, compliance evidence, including this document set |
| **Does NOT own** | The structured facts themselves. A milestone's completion percentage is Nexus Forge's; a product's owner is GOVERNANCE's. DATA owns the *document about* the fact and the ability to retrieve it |
| **Minimum before the gate** | This is the ADR-014 work: Azure SQL as the single backend with Dataverse removed entirely · EF Core code-first discipline · the `Id`/`Seq`/`Ref` pattern · schema-per-layer established · a `Document` entity with versioning, linkable by ID to structured records in other layers |
| **Deferred** | Embeddings, vector retrieval, RAG, knowledge approval workflow, lineage, retention policy, classification, external synchronisation |

**Why two halves in one layer.** Disciplined persistence and durable memory are the same question —
*where does information live* — and splitting them puts two owners on one answer. `M-02-1.5` is the
milestone that makes schema ownership real for every layer, not just this one.

---

### 03 — GOVERNANCE
*registries of products, technology, brand, compliance*

**Purpose.** Give "what exists, who owns it, what state it is in, what obligations it carries" one
authoritative structured answer.

| | |
|---|---|
| **Repository** | `Nexus.Platform` |
| **Schema** | `governance` |
| **Projects (TARGET)** | `Nexus.Governance.Contracts`, `.Core`, `.Infrastructure` |
| **Today** | None. The seed is `Nexus.Platform.Contracts/Identity/IProductRegistry.cs` — right concept, wrong layer; `M-03-1.2` relocates it |
| **Owns** | Product registry, ownership, classification and lifecycle registration · technology registry · brand, trademark, domain, DNS reference and certificate lifecycle · compliance obligations, privacy requirements, data residency · licence registry · external service registry · configuration registry and standards conformance |
| **Does NOT own** | What is being built (Nexus Forge) · how it ships (07) · how it runs (09) · document content (02 owns the trademark certificate PDF; GOVERNANCE owns the trademark status) · the business meaning of a product's features (Products) |
| **Minimum before the gate** | **One record and nothing more.** A minimal `Product` — id, name, owner, classification, lifecycle state — because Nexus Forge's `ProductDevelopment` needs a `ProductId` to hang off |
| **Deferred** | Everything else. Technology, brand, domain, compliance, licence and configuration registries are all P3 |

**The distinction that keeps being missed.** GOVERNANCE says a product *exists and is ours*.
Nexus Forge says what is *being built in it*. Two owners, two schemas, two lifecycles.

---

### 04 — AI
*reasoning, agents, context, models*

**Purpose.** Provide reusable reasoning, model access, agents and intelligent capability to every
layer and product, without any consumer's structure leaking into it.

| | |
|---|---|
| **Repository** | `Nexus.Intelligence` (renamed from `Nexus.Int`, 2026-08-24), deployed at `/intelligence/v1` |
| **Schema** | `ai` |
| **Projects (TARGET)** | `Nexus.AI.Contracts`, `.Core`, `.Context`, `.Memory`, `.Agents`, `.Evaluation`, `.Api` |
| **Today** | **The strongest layer in the system.** `Nexus.Intelligence.Contracts` (17 turn types plus the context types), `.Core` with a complete ten-step `TurnPipeline`, `Planner`, `ExecutionEngine`, `.Context` with `KeywordContextRanker` and `PromptAssembler`, `.Memory`, `.Agents` with `AgentRegistry` and `AgentDispatcher`, `.Api`. `DeveloperAgent.cs` is a 974-byte stub; the tool surface is `EmptyToolCatalog` and `EmptyToolGateway`; every store is in-memory. The `Nexus.AI.*` rename is TARGET |
| **Owns** | Provider routing decisions, model registry and router · prompt management · context engine · memory · agent registry and runtime · tool orchestration · RAG orchestration · planning and reasoning · evaluations and guardrails · per-turn usage and cost attribution · AI observability |
| **Does NOT own** | Product data · conversation storage (10) · documents (02) · provider credentials (01 holds those) · **any knowledge of what a `Workspace` or a `Milestone` is** |
| **Minimum before the gate** | Durable `ITurnTraceStore` and `IResultReportStore` · durable `IMemoryStore` · citations proven end to end against a live model · `DeveloperAgent` real enough to reason about the work graph |
| **Deferred** | Multi-provider routing, prompt versioning, embeddings, RAG, the evaluation harness, guardrails, the tool runtime |

**The invariant.** Consumers flatten their entities into
`ContextItem { Id, Kind, Body, Trust, OccurredAt, Author, RelevanceHint }` and hand over a
`ContextBundle`. `ScopeRef` is opaque. AI returns an `IntelligenceTurnResponse` with citations and a
`DecisionTrace`. This seam works today and **is not to be broken to make any feature easier**.
`AI_DEVELOPMENT_STANDARDS.md` owns how to build on it.

---

### 05 — AUTOMATION
*workflow and process execution*

**Purpose.** Execute reliable, repeatable processes — with or without AI involvement.

| | |
|---|---|
| **Repository** | `Nexus.Platform` |
| **Schema** | `automation` |
| **Projects (TARGET)** | `Nexus.Automation.Contracts`, `.Core`, `.Infrastructure` |
| **Today** | Does not exist |
| **Owns** | Workflow definitions, versions and instances · rules and conditions · triggers and schedules · state machines and transitions · queues, jobs, attempts and retry policy · approval gates and decisions · escalation policies · event handlers · process orchestration · workflow results |
| **Does NOT own** | The business meaning of what it runs — a workflow that approves a purchase order does not know what a purchase order is · reasoning, planning or model calls (04) · what work exists to be done (Nexus Forge) · deployment mechanics (07) · runtime telemetry (09) |
| **Minimum before the gate** | **Nothing, deliberately.** Forge V1a coordinates through explicit state transitions, not a workflow engine |
| **Deferred** | The entire layer. It becomes necessary when Forge V1b dispatches workers autonomously and needs somewhere deterministic for retry and escalation — P2, not P1 |

---

### 06 — SHARED PLATFORM
*reusable product-level capability, scope primitives and shared platform services*

> **RENAMED (Architecture Rebaseline R02, 2026-09-07):** this layer was previously named
> PRODUCT CORE. The rename is conceptual only — see
> `architecture/NEXUS_V2_REBASELINE_R01_REPORT.md` and `_R02_REPORT.md`. No physical project,
> namespace, schema name, or dependency-matrix cell changed as a result; `Nexus.ProductCore.*`
> remains the physical project family until a later, separately-authorized physical migration.
> Every responsibility this layer owned under the old name it still owns in full under the new
> one — the rename widens how the layer is *described*, not what it *does*.

**Purpose.** Provide the reusable half of every product and every consumer of the numbered
Platform — scope, membership, subscriptions, entitlements, quotas, settings, onboarding, and
genuinely common product-facing functions, UI, tools, gateways and SDK/contracts — so nothing
above it rebuilds them.

**Six internal capability areas** (conceptual groupings within this one layer, not separate
numbered layers): Shared Functions · Shared UI · Shared Tools · Shared Product Services (the
scope trunk and entitlement/quota responsibilities below, unchanged from PRODUCT CORE) · Shared
Gateways · Shared SDK / Contracts.

| | |
|---|---|
| **Repository** | `Nexus.Platform` |
| **Schema** | `product_core` (unchanged — see rename note above) |
| **Projects (TARGET)** | `Nexus.ProductCore.Contracts`, `.Core`, `.Scope`, `.Infrastructure` (unchanged; physical rename not performed in this batch) |
| **Today** | The scope trunk exists (`Nexus.ProductCore.Contracts`, `Nexus.ProductCore.Scope` — `Workspace`/`Project`/`Subproject`, `ScopeKind`, `IScopeKindRegistry`). Everything else in the six areas above (Shared Functions/UI/Tools/Gateways/SDK beyond the scope trunk) does not exist yet |
| **Owns** | The scope trunk `Workspace → Project → Subproject` · product profiles and membership · plans, subscriptions and entitlements · feature flags and quotas · product settings, preferences and onboarding state · genuinely common product-facing functions, UI primitives, technical tools/modules, storage/service gateways and SDK/contracts, once they exist |
| **Does NOT own** | Identity (01) · product identity in the registry sense (03) · domain/business behaviour specific to one product (Products) · **development structure below Subproject (Nexus Forge)** · interaction architecture and UX/presentation responsibility (10 EXPERIENCE — see below) · anything Core/Data/Governance/AI/Automation/Delivery/Assurance/Operations already own — this layer composes and re-exposes, it does not duplicate |
| **Minimum before the gate** | **Only the scope primitives** — `Workspace`, `Project`, `Subproject`, plus extensible scope-kind registration so a consumer can declare its own hierarchy without modifying this layer |
| **Deferred** | Membership, profiles, subscriptions, entitlements, quotas, settings, onboarding, and every non-scope-trunk area above — all P3, waiting for a second product and a second user |

**The distinction from CORE is the point.** CORE owns *who you are* — one Nexus identity. SHARED
PLATFORM owns *who you are within a product* — your Vault profile, your Developer profile. And an
architecture test forbids any branch on product identity inside this layer.

**The distinction from EXPERIENCE.** SHARED PLATFORM may own reusable UI *implementation* —
components, primitives, hooks, common panels, reusable screens/patterns — when it is genuinely
common across products. EXPERIENCE owns interaction *architecture* — UX/presentation
responsibility, experience standards, the product/system interaction model itself. Product-specific
UX stays product-owned either way. This layer does not collapse into EXPERIENCE, and EXPERIENCE
does not collapse into it.

**The distinction from Forge.** Forge and Nexus Developer (the product) may consume the same
Shared Platform implementation Forge itself does not become Shared-Platform-owned by doing so, and
Forge's authority over development governance is unaffected by what it happens to reuse. **Shared
implementation does not imply shared authority.** A Forge recovery/bootstrap-critical capability
must remain usable without requiring a healthy running Shared Platform (or any other numbered-layer)
service — a reusable package or library may be shared; a hard runtime dependency that would break
Forge's ability to recover Nexus must not be. **BOOTSTRAP_SAFE / RUNTIME_DEPENDENT / PRODUCT_ONLY
classification (Rebaseline R03/R04, frozen R06).** Three classes, not a spectrum: `BOOTSTRAP_SAFE`
(zero-IO, no dependency on this layer's own product-specific types — Forge may consume it even when
Nexus's runtime/Shared Platform is down), `RUNTIME_DEPENDENT` (fine for Shared Platform's own use,
never for anything Forge needs during recovery), `PRODUCT_ONLY` (a single product's concern, never
shared). A real, evidenced example from Nexus.Developer's own codebase: its DevelopmentControl
contract/protocol types and concurrency-guard subsystem are a proven-zero-IO `BOOTSTRAP_SAFE`
candidate; its Excel workbook adapter is `RUNTIME_DEPENDENT` (ClosedXML-bound, by design the only
place that dependency may live); its runtime product-scope registration (`DeveloperScopeKinds`) is
`PRODUCT_ONLY`. The planned **Git Workspace Tool** (a thin `git worktree` wrapper — `CreateWorktree`
/ `ValidateWorktree` / `ListWorktrees` / `RemoveWorktree` / `GetWorktreeStatus`, no automatic merge,
approval or human-gate bypass) is a further `BOOTSTRAP_SAFE` example: it belongs under this layer's
Shared Tools area, bundled locally with Forge rather than requiring a running Shared Platform
service, and is design-only today — not yet implemented.

**Not a dumping ground.** Reuse when genuinely common; local ownership when genuinely specific.
Contract ownership follows responsibility and valid dependency direction — a type is not moved
here merely because more than one consumer happens to use it, and this layer does not absorb
product/domain business behaviour.

**Why the trunk lives here and not in a product.** With conversation becoming a layer, `Workspace`
and `Project` can no longer belong to one product: Nexus Forge, a plain conversation and machine
work all need them, with completely different structure underneath.

---

### 07 — DELIVERY
*source, build, environments, deployment, infrastructure*

**Purpose.** Safely move source into reproducible running systems, and preserve everything required
to reconstruct them.

| | |
|---|---|
| **Repository** | `Nexus.Platform` for contracts and records; **the pipelines live in each repository's own `.github/workflows/`**, because a pipeline that is not next to the code it builds drifts from it |
| **Schema** | `delivery` |
| **Projects (TARGET)** | `Nexus.Delivery.Contracts`, `.Core`, `.Infrastructure` |
| **Today** | Does not exist, and **neither does any CI.** `Nexus.Platform\.github\workflows\` is empty; the other two repositories have no `.github` directory. No infrastructure-as-code, no environment definitions, no deployment pipeline anywhere |
| **Owns** | Git providers, repositories, branch policy, tags and commits · build infrastructure, CI/CD, artifact registry · environment management and provisioning · deployment and release promotion · infrastructure-as-code · backup, restore and disaster recovery · deployment credentials |
| **Does NOT own** | The *meaning* of a build (Nexus Forge decides whether it satisfies a work item) · whether a requirement was met (08) · runtime health after deployment (09) · product identity (03) |
| **Minimum before the gate** | Deliberately tiny, and **mandatory**: a workflow per repository doing restore, build, test and publish results · branch protection on `main` requiring a green build · NetArchTest wired into CI as a hard gate · results in a form Nexus Forge can ingest · the antivirus exclusion for `C:\Personal\` **verified** and a documented backup of all three repositories |
| **Deferred** | Artifact registry, environments, deployment pipelines, infrastructure-as-code, cloud provisioning, disaster-recovery automation |

**Why it is before the gate.** The gate's acceptance test requires independent build, independent
test and result capture across three simultaneous workers. Without CI that means one human building
three branches by hand on one machine and typing results in — not isolation, not scalable past one
machine, and no evidence trail. `M-08-1.1` is the first work in the entire roadmap.

---

### 08 — ASSURANCE
*prove that requirements have actually been satisfied*

**Purpose.** Verify and validate that what Nexus designs, builds, deploys and operates satisfies its
requirements, quality standards, safety constraints and acceptance criteria.

| | |
|---|---|
| **Repository** | `Nexus.Platform` |
| **Schema** | `assurance` |
| **Projects (TARGET)** | `Nexus.Assurance.Contracts`, `.Core`, `.Infrastructure` |
| **Today** | Does not exist. **New in v2.1** — inserting it renumbered OPERATIONS, EXPERIENCE and PRODUCTS. Renumbered again in v2.2 when 07 DEVELOPER left the numbered set (see §2.2) |
| **Owns** | Quality, verification, validation, test and inspection plans · test cases and inspection characteristics · acceptance criteria · verification and validation methods · verification, validation and inspection runs · evidence · defects, deviations, non-conformances and corrective actions · quality gates, qualification results and traceability links |
| **Does NOT own** | What needs testing and which work item it belongs to (Nexus Forge) · executing build and automated-test pipelines (07) · runtime health (09) · the formal test report **document** (02 owns the document, ASSURANCE owns the result) · the `Requirement` itself (Nexus Forge owns `Requirement`; ASSURANCE owns its `AcceptanceCriterion`) |
| **Minimum before the gate** | Acceptance criteria · a verification method · test and inspection evidence · a pass/fail verdict · one quality gate. Enough to make Definition of Done enforceable, and nothing more |
| **Deferred** | Plans and specifications, test cases, inspection characteristics, AI evaluation, assurance profiles, certification evidence |

**Why it exists.** A green build proves code compiles and some assertions held. It does not prove a
requirement was met. Given that P2 begins autonomous dispatch, an unevidenced definition of done is
exactly how an autonomous system talks itself into believing it succeeded.

**Deliberately broader than software testing.** A boring machine is qualified by measured
characteristics against tolerances; an ERP process by user validation; an AI answer by scored
evaluation. One traceability model, different methods — and a requirement with no acceptance
criterion is **reportable as a traceability gap** rather than silently absent.

**The safety carve-out.** A criterion marked safety-critical cannot be waived by the ordinary
deviation path, only by a named human with recorded authority, and **no agent may create, modify or
waive one.** `ASSURANCE_STANDARDS.md` owns the method detail.

---

### 09 — OPERATIONS
*run and observe deployed systems*

**Purpose.** Keep running systems healthy, secure, observable and recoverable.

| | |
|---|---|
| **Repository** | `Nexus.Platform` |
| **Schema** | `operations` — time-series shaped, and the most likely first candidate to leave the shared database |
| **Projects (TARGET)** | `Nexus.Operations.Contracts`, `.Core`, `.Infrastructure` |
| **Today** | Does not exist. **Nothing is deployed, so nothing is operated.** The layer exists now to reserve the space and stop observability being retrofitted into ten places later |
| **Owns** | Logs, metrics, traces, health checks · incidents and alerts · performance and capacity records · cost records · runtime feature-flag state · security monitoring · recovery operations |
| **Does NOT own** | Anything durable about what was built or why (Nexus Forge) · how it was shipped (07) · whether a requirement was satisfied (08) |
| **Minimum before the gate** | **Only structured logging with correlation IDs** — added during CORE V1 rather than retrofitted, because retrofitting correlation is disproportionately expensive |
| **Deferred** | Metrics, tracing, health checks, incidents, alerting, cost, capacity, feature flags, recovery drills — all waiting for something to actually be deployed |

`OBSERVABILITY_STANDARDS.md` owns log shape, level semantics and correlation flow.

---

### 10 — EXPERIENCE
*reusable human and system interaction*

**Purpose.** Provide reusable human and system interaction capability — above all the conversation
engine — so every layer and product gets chat without rebuilding it, and without conversation
becoming the architecture.

| | |
|---|---|
| **Repository** | `Nexus.Experience` — renamed from `Nexus.Web` (2026-08-24) |
| **Schema** | `experience` |
| **Projects (TARGET)** | `Nexus.Experience.Contracts`, `.Conversation`, `.Core`, `.Infrastructure`, `.Api`, `.Client` |
| **Today** | **Real, but trapped inside the Chat product.** `Conversation` and `ConversationMessage` are Chat aggregates carrying `ConversationType` and `ConversationVisibility`. The React client (`Nexus.Experience.Client`) is real and substantial — `ChatPanel`, `MessageThread`, `ConversationList`, `CitationsPanel`, `ChatTelemetryContext` and the `use*` hooks all survive the extraction intact |
| **Owns** | Conversation, message, participant, attachment, conversation session · memory, knowledge, tool-usage and result references · scope-kind bindings · UI preferences, command definitions, notification delivery |
| **Does NOT own** | Contextual structure of any kind — `Workspace` and `Project` are 06, `Milestone` and `WorkItem` are Nexus Forge's · product domain data · documents · model access |
| **Minimum before the gate** | Conversation core · scope resolution · a reusable chat surface, only as much as Nexus Forge needs |
| **Deferred** | Commands, unified search, forms, approval surfaces, the notification centre and the component system are P3; voice and realtime are P4 |

**The mechanism, in four steps.** A conversation carries an opaque `ScopeRef` and nothing else about
its context. A consuming layer registers a scope kind and an `IScopeResolver`. The engine calls the
resolver, receives a `ContextBundle`, and passes it through **untouched**. AI receives flattened
`ContextItem`s and never learns what a `Milestone` is. Three consumers with nothing in common —
Nexus Forge on milestones, a plain conversation on projects, machine work on operations — served
by one engine simultaneously, with zero shared types.

**The standing constraint.** *Chat must not become the architecture for Nexus.* When something can
be modelled as structure or as conversation, model it as structure and let conversation reference
it. A milestone's dependency is a `Dependency` row a conversation can discuss — never a sentence in
a transcript that has to be re-derived.

**A standalone Chat application, if released, is a product (outside numbered Platform) that
consumes this engine.** The engine is never called a Chat product.

---

## 4a. Outside numbered Platform

Two things depend on the ten numbered layers above without being one of them themselves — see §1
for why numbering either would misstate what depends on what.

### Nexus Forge
*define, plan and build software — permanent development-plane infrastructure*

**Purpose.** Define, plan, build, test, review and coordinate software development, and be the
structured system of record for development state — replacing chat transcripts and markdown. Forge
is **permanent** infrastructure: it is never described as temporary, a bridge, or retired when a
product (including Nexus Developer, below) is available.

| | |
|---|---|
| **Repository** | `Nexus.Forge` — **does not exist yet** (v2.1 named this `Nexus.Developer`; see §2.2) |
| **Schema** | `forge`. The first candidate to split to its own database at P3 — predicted, not decided (`DATABASE_ARCHITECTURE.md` §6) |
| **Projects (TARGET)** | `Nexus.Forge.Contracts`, `.Core`, `.Graph`, `.Orchestration`, `.Infrastructure`, `.Api`, `.Client` |
| **Today** | No repository and no project. Six of the eleven Chat aggregates are its seed, misplaced: `WorkItem`, `Adr`, `Branch`, `Snapshot`, `Session`, `Artifact` |
| **Owns** | The work graph — product development, modules, features, requirements, releases, milestones, work items, tasks, subtasks, dependencies and scope declarations · workers, assignments and development runs · build and test record *interpretation* · reviews, integration runs and development results · derived progress and status history |
| **Does NOT own** | Product identity (03 — Forge references a `ProductId`) · `Workspace`/`Project`/`Subproject` (06 owns the trunk; Forge extends **downward** from Subproject) · repository and CI mechanics (07 produces a build, Forge interprets it) · whether a requirement was actually satisfied (08) · runtime health (09) · specification documents (02 — a `Milestone` links to a `Document` by ID) · conversation storage (10 — Forge supplies scope and context) · the developer-facing console UX (the Nexus Developer product, below, consumes Forge through a versioned contract — it never reaches Forge's data directly) |
| **Minimum before the gate** | The whole of V1a: product and project definition · milestones, features, work items, tasks and subtasks · dependencies and parallel-safe analysis · workers and git worktree coordination · **three simultaneous isolated work items** · build and test result capture · review and controlled integration · derived progress |
| **Deferred** | Autonomous dispatch (V1b), model assignment and per-run cost, requirements and releases, the product and schema designers, capability packs, dashboards |

**Forge consumes numbered layers; it does not absorb them.** It does not implement chat — it
registers its scope hierarchy with SHARED PLATFORM and implements EXPERIENCE's scope resolver. It does
not run CI — DELIVERY produces a build and Forge interprets whether it satisfies a work item. It
does not own documents. These three constraints are what stop the layer becoming a dumping ground,
and **scope creep here is the single highest risk in the plan**.

**The scope extension:** `Subproject → Release → Milestone → Feature → WorkItem → Task → Subtask`.

**Forge vs the Nexus Developer product.** Forge owns the entire coordination engine above. Nexus
Developer is the human-facing console/UX over it — a Product (below), not part of Forge, reaching
Forge only through Forge's own versioned API/contract, never direct database access. See the
approved decision in `NEXUS_V1_TO_V2_DEEP_RECONCILIATION_REPORT.md` §27.

**Bootstrap-safe reuse (Rebaseline R02; classification frozen R03/R04/R06).** Forge may consume
shared implementation from SHARED PLATFORM or any lower numbered layer, but anything Forge genuinely
needs for Nexus recovery/bootstrap must remain usable without requiring a healthy running Nexus
runtime — shared implementation does not imply shared authority (see 06 SHARED PLATFORM, "The
distinction from Forge," above). The classification is `BOOTSTRAP_SAFE` / `RUNTIME_DEPENDENT` /
`PRODUCT_ONLY` — see 06 SHARED PLATFORM's "The distinction from Forge" paragraph above for the
definitions and a worked example.

**Development-control target model (Rebaseline R03/R04/R05, design frozen R06 — not yet
implemented).** Two workbooks, same governed schema, duplicate schema not duplicate records: a
record lives in the workbook that owns the work — `NEXUS_FOUNDATION_DEVELOPMENT_CONTROL.xlsx` for
the numbered-layer/Forge roadmap, `NEXUS_PRODUCTS_DEVELOPMENT_CONTROL.xlsx` for Nexus Developer and
Products work; no third central workbook. The permanent cross-workbook address is
`DevelopmentControlAddress { DevelopmentControlRole Role; NodeId Id; }` — `Role` is `Foundation` or
`Products`, never a routing decision made from a node-ID prefix (prefixes are migration-discovery
evidence only, never routing authority). A `DevelopmentRun` is one execution attempt — the design
explicitly rejects a separate `WorkerAssignment` type; worker/model/branch/worktree/build-test-result
fields expand `DevelopmentRun` directly. `ActiveChange` remains the governed change/reservation
envelope and `PreflightDeclaration` the declared scope/safety envelope a change is checked against —
these three do not collapse into one another. Physical migration to two workbooks, and the
writer-lock protocol both sides must observe (a deterministic workbook-identity lock file with an
OS-level exclusive open, callable from both Forge's PowerShell and Nexus.Developer's C#), are
designed but **not implemented** as of this freeze.

**Dependency/Context Resolver and Task Resolver (Rebaseline R05, frozen R06).** Forge already has
real, working modules under `DevTools-ForgeV2/DevBridge/scripts/ai-routing/` — `DependencyLineage.ps1`
(dependency-graph traversal, cycle detection, historical-evidence reconciliation),
`ContextPackage.ps1` (deterministic context assembly with secret redaction and token budgeting),
`TaskClassification.ps1` (rules-based task type/complexity/risk classification), and a `router/`
module (model/provider eligibility and cost/performance-weighted ranking for Forge's own governed
development work). The future cross-workbook-aware Context Resolver **extends these**, it is not a
green-field build — the missing piece is narrow: `DevelopmentControlAddress` awareness,
cross-workbook resolution, task-eligibility composition, and general DevelopmentControl integration.
A future Task Resolver ("what work is next") is likewise a **thin composition** over existing
pieces — DevelopmentControl work-item eligibility, dependency state, reservation/preflight,
governance gates and Forge's classification/routing — not a large new engine.

**Work Universe (v2.3, WU-02).** A cross-domain relationship model — Product, Feature, Planning, Chat & Context, Development, Outcome, Release & Deployment — is frozen in `WORK_UNIVERSE.md`. It reuses the layer/product concepts named throughout this document; it does not add a numbered layer or change any dependency rule beyond the Nexus Developer → 08 ASSURANCE resolution recorded in `DEPENDENCY_RULES.md` §4.

**Three distinct routing/model-selection concerns — never collapsed.** (1) **01 CORE**'s model
gateway (`AI_ARCHITECTURE.md` §4) is low-level provider/model access infrastructure used by every
consumer. (2) **Nexus Forge's Worker/Model Router** (the `router/` module above) chooses which
AI provider/model performs a *Forge-governed development task* — writing, reviewing or planning
Nexus's own code — never a runtime end-user request. (3) **04 AI**'s own role→model assignment
(`Nexus.Intelligence.Core.Roles`, `AiRole`/`AiRoleResolver`) chooses which model handles a *runtime
end-user conversation turn*. All three are real, independently evidenced, and must never be
described as one concept.

**Gate A and Gate B.** GATE A is *Development Ready* — the earliest safe point business systems can
begin (`ASSURANCE_ARCHITECTURE.md` §13). Its mandatory acceptance evidence is
`PARALLEL_EXECUTION_PROOF` — nine criteria: three independent work items; three distinct workers;
three isolated Git worktrees; overlapping execution; independent build/test evidence per worker; one
worker deliberately fails; unrelated workers continue unaffected; controlled integration; and final
integration verification. GATE B is *Foundation Ready* — the broader reusable-foundation-complete
gate (durable AI memory, usage metering, autonomous dispatch, automated deployment, reusable
Experience components, and similar durable/production-grade capability) — and runs in **parallel**
with business development; it must never block or pause it.

**External development intelligence (Rebaseline R02).** ChatGPT, Claude, Grok Bot and other
external AI tools are optional, replaceable, external development/R&D assistance — never a runtime
dependency of Forge, any numbered layer, or any product, and never a source of production or
merge/deployment authority. Useful discoveries from them become governed Nexus requirements through
the normal human/Forge process, not through any automatic pipeline.

**Historical note on DevBridge.** Prior DevBridge documentation (`DevTools-ForgeV2/DevBridge/docs/
DEVBRIDGE_RETIREMENT_PLAN.md` and related files) describes DevBridge as temporary scaffolding to be
retired once a product replaces it. That framing is **superseded historical guidance** — it predates
this document's "Forge is permanent" position (above) and must not be read as current direction. The
DevBridge documents themselves are not rewritten by this note; a future Forge-specific documentation
batch may add explicit historical banners to them.

---

### Products
*real business and customer problems*

**Purpose.** Solve actual user, business and domain problems by composing Nexus capability.

| | |
|---|---|
| **Repository** | `Nexus.Products.<Name>` — one repository per product, because a product must be removable |
| **Schema** | **Its own database**, not a schema in `NexusPlatform` |
| **Projects (TARGET)** | `Nexus.Products.<Name>.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Client` |
| **Today** | One product: `Nexus.Products.Chat.*` in `Nexus.Experience`, holding eleven aggregates of which six belong to other layers. It is being dissolved — the conversation half becomes EXPERIENCE |
| **Owns** | Everything domain-specific to that product, and only that |
| **Does NOT own** | Anything a lower layer owns · **another product's types — products never reference each other** |
| **Minimum before the gate** | Nothing. Products are what the gate exists to unblock |
| **Deferred** | The product framework and capability-pack composition are P3. Internal business systems become *eligible* at P2, pulled by need rather than scheduled. Consumer products are P4. Machine automation is P5 and gated |

**Composition, not conditionals.** A product declares capability packs —
`Vault = Web + Mobile + Desktop + Documents + AI + Security + Offline Sync`. There is no
`if (Product == Vault)` anywhere in the platform, and an architecture test enforces it.

**Two categories, two scheduling models.** Consumer products (Vault, Trips, Career, Education,
Truck, Games) are planned and sequenced. Internal business systems (ERP core, CRM and field data,
engine works, retreads, transport, knowledge systems, internal tools, machine development) are a
pull queue — their phase means *eligible from*, not *scheduled for*. Machine automation is gated on
its own safety architecture and human sign-off before any milestone may be written. **Nexus
Developer** — the console over Forge — is a product like any other here, consuming Forge rather
than being part of it.

**Product state is eight dimensions, not one field:** lifecycle state, development stage, current
release, current production release, development health, deployment state, operational health and
compliance state — owned across GOVERNANCE, Nexus Forge, DELIVERY and OPERATIONS, each marked
derived or manual. `PRODUCT_DEVELOPMENT_GUIDE.md` owns the procedure for standing one up.

---

## 5. What the layer set does not include

| Not a layer | Why | Where it lives |
|---|---|---|
| Chat | Conversation is universal; a chat *product* is a consumer | 10 EXPERIENCE (engine), Products (any app, outside numbering) |
| Frontend | Not a responsibility, a surface | 10 EXPERIENCE (shared), Products (product-specific) |
| Testing | Four different questions with four owners | Nexus Forge asks, 07 executes, 08 judges, 09 watches |
| Reporting | Reads facts, owns none | The owning layer's API |
| Integration | A capability, not a tier | The consuming layer or product |

---

## 6. References

- `ARCHITECTURE_OVERVIEW.md` — what Nexus is, the current state, the Foundation Gate.
- `DEPENDENCY_RULES.md` — what each layer may reference, and how it is enforced.
- `DATA_OWNERSHIP.md` — the entity-by-entity ownership table and the entity migration matrix.
- `DATABASE_ARCHITECTURE.md` — schemas, databases and the split criteria.
- `REPOSITORY_STRUCTURE.md` — which repository and project a file belongs in.
- `PRODUCT_DEVELOPMENT_GUIDE.md` — standing up a product (outside numbered Platform).
- `AI_DEVELOPMENT_STANDARDS.md` — building on Layer 04 without breaking the seam.
- `ASSURANCE_STANDARDS.md` — Layer 08's methods, evidence and gates.
- `../nexus-roadmap.yaml` — every layer's features, milestones and phases.
- `../NEXUS_MASTER_ARCHITECTURE.md` — the full per-layer treatment and its reasoning.
