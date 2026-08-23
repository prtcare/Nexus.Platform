# Bounded Contexts

**Status:** MIXED — the context map is settled; **eleven of the twelve layers have no code**, so most
rows below are TARGET and each names the milestone that makes it real
**Owner:** Durai; each layer's owner maintains its own block
**Last updated:** 2026-08-21
**Layer:** cross-cutting
**Authoritative for:** the context map — which bounded contexts exist *inside* each of the twelve
layers, what each context owns, which contexts it may and may not depend on, the public contract
surface it exposes, and the extension model by which a consumer extends it without modifying it.

Not authoritative for: what a layer *is* — `LAYER_MODEL.md`; the twelve-by-twelve layer dependency
matrix and its enforcement — `DEPENDENCY_RULES.md`; which layer owns which entity —
`DATA_OWNERSHIP.md`; the physical schema and database split — `DATABASE_ARCHITECTURE.md`; the
internal design of any single layer — its own `*_ARCHITECTURE.md`.

---

## 1. What a bounded context is here

A **layer** is a responsibility band. A **bounded context** is the unit inside it that owns a
consistent model, a transaction boundary and one contract surface. Two contexts in one layer share a
schema and a repository; they do not share types, and neither reaches into the other's tables. Three
properties make something a context rather than a folder:

| Property | Test |
|---|---|
| **One model** | A word means exactly one thing inside it. `Project` in PRODUCT CORE is a scope node; `Project` in DEVELOPER does not exist — DEVELOPER extends *below* Subproject |
| **One contract surface** | Everything outside reaches it through named interfaces or an API, never through its entity types |
| **One extension seam** | A consumer that needs different behaviour registers something, rather than editing the context |

The third is what this document exists for: a context with no extension seam gets extended by
`if (Product == X)` instead — the failure Rule 6 and `M-12-1.2` exist to make impossible.

---

## 2. The context map

Sixty-eight contexts across twelve layers.

| Layer | # | Context | Owns, in one line | Extension seam |
|---|---|---|---|---|
| **01 CORE** | 1.1 | Identity | `User`, `Credential`, `Session` | None — one implementation |
| | 1.2 | Tenancy | `Organisation`, `Tenant`, `ITenantResolver` | None — deliberately closed |
| | 1.3 | Authorization | `Role`, `Permission`, `Policy` | Policy rules as data (`M-01-3.2`) |
| | 1.4 | Audit and Usage | `AuditEntry`, `UsageRecord`, quota verdicts | `IAuditLog`, `IUsageMeter`, `IQuotaPolicy` |
| | 1.5 | Secrets | `SecretRef`, resolution order | `ISecretResolver` implementations |
| | 1.6 | Model Gateway | `ModelDescriptor`, routing, provider credentials | `INamedModelGateway`, `IModelCatalogSource` |
| | 1.7 | Tool Gateway | `ToolDescriptor`, `SideEffectClass`, invocation | `IToolCatalog` registration |
| | 1.8 | Events and Notifications | `NotificationChannel`; the future bus | Handler registration (`M-01-8.1`) |
| **02 DATA** | 2.1 | Persistence Foundation | Id/Seq/Ref, migrations, schema ownership | Conventions, not plug-ins |
| | 2.2 | Document Store | `Document`, `DocumentVersion`, `DocumentMetadata` | Entity links by layer/type/id |
| | 2.3 | Knowledge Store | `KnowledgeItem`, `Source`, `Provenance` | Source adapters (`M-02-6.2`) |
| | 2.4 | Retrieval | `Embedding`, `IndexEntry`, ranking, RAG | `IContextRanker` implementations |
| | 2.5 | Data Governance | `Classification`, `RetentionPolicy`, `Lineage` | Policies as data |
| | 2.6 | Import and Sync | Import, export, external synchronisation | Format handlers |
| **03 GOVERNANCE** | 3.1 | Product Registry | `Product`, ownership, classification, lifecycle | Classification values as data |
| | 3.2 | Technology Registry | `Technology`, `TechnologyVersion`, usage links | Catalogue rows |
| | 3.3 | Brand and Domain | `Brand`, `Trademark`, `DomainReference`, `Certificate` | Registry rows |
| | 3.4 | Compliance and Privacy | Obligations, attestations, residency | Obligation catalogue as data |
| | 3.5 | Licence and Service | `Licence`, `ExternalService`, dependencies | Registry rows |
| | 3.6 | Configuration and Standards | `ConfigurationEntry`, `Standard`, conformance | Standards as data |
| **04 AI** | 4.1 | Turn Pipeline | `TurnPipeline` and its nine steps | Step composition |
| | 4.2 | Context Engine | `ContextBundle`, `ContextItem`, prompt assembly | `IContextRanker`, `IPromptAssembler` |
| | 4.3 | Memory | `MemoryRecord`, `MemoryKind`, `IMemoryStore` | `IMemoryStore` implementations |
| | 4.4 | Agents | `IAgent`, `AgentRegistry`, `AgentDispatcher` | `IAgent` registration |
| | 4.5 | Model Selection | `ModelRoute`, `ModelSelector` — selection only | Routing rules as data |
| | 4.6 | Evaluation and Guardrails | `Evaluation`, `Guardrail` | Question sets as data |
| | 4.7 | Result Reporting | `ResultReport`, `ResultOutcome`, `TurnTrace` | `IResultReportStore` |
| **05 AUTOMATION** | 5.1 | Job Runner | `Queue`, `Job`, `JobAttempt`, `RetryPolicy` | Handler per queue name |
| | 5.2 | Workflow | Definition, version, instance, step, result | Definitions as data |
| | 5.3 | Triggers and Schedules | `Trigger`, `Schedule`, `EventHandler` | Trigger bindings as data |
| | 5.4 | Rules and State Machines | `Rule`, `Condition`, `StateMachineDefinition` | Rules as data |
| | 5.5 | Approvals | `ApprovalGate`, `ApprovalDecision`, escalation | Approver sets as data |
| | 5.6 | Process Orchestration | `ProcessOrchestration`, compensation | Parent/child declarations |
| **06 PRODUCT CORE** | 6.1 | Scope | `Workspace`, `Project`, `Subproject` | **`ScopeKindRegistration`** (`M-06-1.2`) |
| | 6.2 | Membership and Profiles | `ProductProfile`, `ProductMembership`, product roles | Roles as data |
| | 6.3 | Commercial | `Plan`, `Subscription`, `Entitlement`, `FeatureFlag` | Plans as data |
| | 6.4 | Quotas | `Quota`, product-level enforcement | Quota rows |
| | 6.5 | Settings | `ProductSetting`, `Preference`, override chain | Setting keys as data |
| | 6.6 | Onboarding | `OnboardingState` machine | Step definitions as data |
| **07 DEVELOPER** | 7.1 | Work Graph | `ProductDevelopment` → `Subtask`, `Requirement`, `Release` | Node kinds are closed |
| | 7.2 | Dependency and Safety | `Dependency`, `ScopeDeclaration`, the five rules | Rules are code with a test each |
| | 7.3 | Orchestration | `Worker`, `WorkerAssignment`, `DevelopmentRun` | Worker kinds as data |
| | 7.4 | Result Interpretation | `BuildRecord`, `TestRun`, `Review`, `IntegrationRun` | Ingestion of DELIVERY's artifact |
| | 7.5 | Progress | `ProgressState`, `StatusHistory` — derived | Derivation is closed |
| | 7.6 | Developer Conversation | Its `IScopeResolver` and its surface | Registered, not built |
| | 7.7 | Architecture Definition | Designer, schema definition, capability packs | Declarations as data |
| **08 DELIVERY** | 8.1 | Source Control | `Repository`, `GitBranch`, `Tag`, `Commit` | Per-repository configuration |
| | 8.2 | Continuous Integration | `Pipeline`, `PipelineRun`, **the result artifact** | Pipeline config lives per repo |
| | 8.3 | Artifact Registry | `BuildArtifact`, immutability, retention | Retention policy as data |
| | 8.4 | Environments | `Environment`, `InfrastructureResource` | Environment definitions in source |
| | 8.5 | Deployment | `Deployment`, promotion | Promotion policy as data |
| | 8.6 | Backup and Recovery | `BackupRecord`, `RestorePoint` | Schedules as data |
| **09 ASSURANCE** | 9.1 | Specification | `AcceptanceCriterion`, `VerificationMethod` | Five methods — **closed set** |
| | 9.2 | Planning | Quality, verification, validation, test, inspection plans | Plans as data |
| | 9.3 | Execution and Evidence | `VerificationRun`, `InspectionRun`, `Evidence` | Evidence kinds as data |
| | 9.4 | Findings | `Defect`, `Deviation`, `NonConformance`, `CorrectiveAction` | Lifecycle is closed |
| | 9.5 | Gates and Qualification | `QualityGate`, `QualificationResult` | Gate composition as data |
| | 9.6 | Traceability | `TraceabilityLink` — polymorphic, constraint-free | Any layer/type/id |
| | 9.7 | Profiles | `AssuranceProfile` | **Profile selection** (`M-09-7.1`) |
| **10 OPERATIONS** | 10.1 | Logging and Correlation | `LogStream`, correlation id propagation | Sink configuration |
| | 10.2 | Metrics and Tracing | `Metric`, `Trace` | Instrument registration |
| | 10.3 | Health | `HealthCheck` | Check registration |
| | 10.4 | Incidents and Alerts | `Incident`, `Alert` | Alert rules as data |
| | 10.5 | Cost and Capacity | `CostRecord`, `PerformanceRecord`, `CapacityRecord` | Baselines as data |
| | 10.6 | Runtime Flags | `FeatureFlagState` | Flag rows |
| **11 EXPERIENCE** | 11.1 | Conversation Core | `Conversation`, `Message`, `Participant`, `Attachment` | **Nothing** — deliberately sealed |
| | 11.2 | Scope Resolution | `ScopeKindBinding`, dispatch | **`IScopeResolver`** (`M-11-2.1`) |
| | 11.3 | References Out | `MemoryReference`, `KnowledgeReference`, `ToolUsage` | Reference kinds as data |
| | 11.4 | Interaction Surface | Chat components, `CommandDefinition`, approval surface | Command registration (`M-11-4.1`) |
| | 11.5 | Design System | `UIPreference`, tokens, primitives | Theme as data |
| **12 PRODUCTS** | 12.1 | Product Framework | Template, `CapabilityPack`, the eight-dimension state model | **Capability packs** (`M-12-1.2`) |
| | 12.2 | *Per product:* Product Core | That product's identity, context, settings, state | Configures layer 06 |
| | 12.3 | *Per product:* Domain Modules | The actual business capability | The product owns it outright |
| | 12.4 | *Per product:* Capability Integrations | How this product consumes 01–11 | Declared, never coded |

**What exists on disk today**, and it is short: 1.4 partially (`ConsoleAuditLog`, `InMemoryUsageMeter`,
`PermissiveQuotaPolicy`), 1.6 (`RoutingModelGateway`, `OpenAIModelGateway`), 4.1–4.4 and 4.7 in
memory-backed form, 2.1 in the one proven migration, and a working conversation implementation in the
wrong place — `Nexus.Products.Chat.*`, which 11.1 and 11.4 will become. Everything else is TARGET.

---

## 3. The seven extension models, and when each applies

Every seam in the map above is one of seven shapes. Adding an eighth is an architecture decision, not
a convenience.

| # | Shape | Mechanism | Canonical instance |
|---|---|---|---|
| 1 | **Registered implementation** | The context owns an interface; the consumer supplies a type and registers it in the composition root | `IScopeResolver` (11.2), `INamedModelGateway` (1.6), `IAgent` (4.4) |
| 2 | **Registered kind** | The consumer declares a new node kind the context stores but does not interpret | `ScopeKindRegistration` (6.1) — DEVELOPER registers `Milestone`, `Feature`, `WorkItem`, `Task` |
| 3 | **Declared profile** | A row selects which of the context's behaviours are mandatory | `AssuranceProfile` (9.7), the five product profiles |
| 4 | **Declared capability pack** | A row states which layers a product consumes | `CapabilityPack` (12.1) |
| 5 | **Definition as data** | Behaviour is authored as a stored definition the context executes | `WorkflowDefinition` (5.2), `Rule` (5.4), `Schedule` (5.3) |
| 6 | **Polymorphic reference** | The context points at a row in a layer it may not reference: layer, type, id, no FK | `TraceabilityLink` (9.6), `Document` entity links (2.2) |
| 7 | **Flatten to a neutral type** | The consumer converts its own model into a shape the context already knows | `ContextBundle` of `ContextItem` (4.2) |

**Shapes 1, 2 and 7 are the load-bearing three** — the whole reason one conversation engine serves
DEVELOPER, a plain project conversation and a future machine domain with zero shared types
(`EXPERIENCE_ARCHITECTURE.md` §4).

**What is not an extension model:** a `switch` on product identity, a "common" assembly both sides
reference, a nullable field added to a shared entity for one consumer, or a configuration flag whose
values are product names. The last two are the first one wearing a hat.

---

## 4. 01 CORE

**Repository** `Nexus.Platform` · **schema** `core` · **contracts** `Nexus.Platform.Contracts`

| | |
|---|---|
| **May depend on** | Nothing. CORE is the bottom and stays there |
| **Must never reference** | Any other Nexus layer, any `Nexus.Products.*`, any `Nexus.Intelligence.*`. No EF Core in Contracts |
| **Contract surface** | `Governance/` — `IAuditLog`, `IQuotaPolicy`, `IUsageMeter`, `AuditEntry`, `UsageRecord`, `QuotaVerdict`. `Identity/` — `IIdentityService`, `ITenantResolver`, `ResolvedIdentity`. `Models/` — 13 types including `IModelCatalog`, `IModelGateway`, `ModelDescriptor`. `Secrets/ISecretResolver`. `Tools/` — `IToolCatalog`, `IToolGateway`, `ToolDescriptor`, `SideEffectClass` |

**Cross-context rules inside CORE.** Tenancy (1.2) is consumed by every sibling and depends on
Identity (1.1) alone. Model Gateway (1.6) depends on Secrets (1.5) and nothing else — the invariant
that keeps provider credentials inside CORE (`AI_ARCHITECTURE.md` §4). Audit (1.4) is written to by
all seven siblings and reads from none.

**Extension.** Providers extend 1.6 by *adding a project* — `Nexus.Platform.Providers.<Vendor>` — not
by adding a case; a direct provider SDK call outside such a project is forbidden. Tools extend 1.7 by
registering a `ToolDescriptor` that declares its `SideEffectClass` before it can be invoked at all.

**TRANSITION.** `IProductRegistry` sits in `Nexus.Platform.Contracts/Identity/` and belongs to
GOVERNANCE; `M-03-1.2` moves it. `Nexus.Platform.Identity/IdentityProvider.cs` is a 240-byte stub
that `M-01-1.1` deletes rather than extends.

---

## 5. 02 DATA

**Repository** `Nexus.Platform` · **schema** `data` · **contracts** `Nexus.Data.Contracts` (TARGET)

| | |
|---|---|
| **May depend on** | 01 CORE only |
| **Must never reference** | Anything above 01, any `Nexus.Products.*`, any product `DbContext` |
| **Contract surface** | TARGET. Document and knowledge repositories, `IContextRanker` implementations for 04's Context Engine, and the retrieval query surface |

**The context that is not a context.** 2.1 Persistence Foundation owns conventions — Id/Seq/Ref,
migration ordering, cascade rules, schema ownership — and exposes no runtime interface.
`DATABASE_STANDARDS.md` owns the mechanics.

**The ownership line other layers get wrong.** DATA owns the *document about* a fact; the fact itself
belongs to the layer whose domain it is. A milestone's specification is a `Document` in 2.2 that
DEVELOPER's `Milestone` references by id. `DATA_OWNERSHIP.md` §2 owns this rule.

**Extension.** 2.4 Retrieval is the only context here with a live seam today: `KeywordContextRanker`
implements `IContextRanker` in `Nexus.Intelligence.Context`, and `M-02-4.2` adds a vector ranker
compared against it on a fixed question set rather than replacing it by assertion.

---

## 6. 03 GOVERNANCE

**Repository** `Nexus.Platform` · **schema** `governance` · **contracts** `Nexus.Governance.Contracts`

| | |
|---|---|
| **May depend on** | 01 CORE, 02 DATA |
| **Must never reference** | 04–12, any product type. `Product` here is a registry row, never a product's domain type |
| **Contract surface** | `IProductRegistry` after `M-03-1.2`, plus registry query interfaces per context |

**Cross-context rules inside GOVERNANCE.** 3.1 Product Registry is the root: 3.2 through 3.6 all hang
off a `ProductId` and none references another. That shape is why an end-of-life `TechnologyVersion`
resolves to a product list (`M-03-2.2`) without any registry knowing about any other.

**The rule every registry here shares:** a registry holds a **reference**, never the material.
`M-03-3.3` stores a certificate thumbprint and a secret reference, never private key material;
`M-03-4.3` stores a `DocumentId`, never document bytes; `M-03-6.1` rejects a value written to an entry
marked `IsSecret` and accepts only a secret reference. The registry is an index, and an index that
holds the thing it indexes is a second copy with worse access control.

---

## 7. 04 AI

**Repository** `Nexus.Intelligence` · **schema** `ai` · **contracts** `Nexus.Intelligence.Contracts`

| | |
|---|---|
| **May depend on** | 01 CORE, 02 DATA |
| **Must never reference** | Any `Nexus.Products.*`, `Nexus.Experience.*`, `Nexus.Developer.*`. **No AI type may name `Workspace`, `Project`, `Milestone`, `WorkItem`** or any other consumer concept |
| **Contract surface** | `Turns/` — 17 types including `IntelligenceTurnRequest`/`Response`, `ScopeRef`, `ActorRef`, `DecisionTrace`, `PlanStep`, `ProposedAction`. `Context/` — `ContextBundle`, `ContextItem`, `ContextItemKind`, `TrustLevel`, `Citation`, `PersistenceHint`. `Results/` — `ResultReport`, `ResultOutcome`. `Client/IIntelligenceClient` |

**4.5 Model Selection is not model access.** The AI layer decides *which* model; CORE 1.6 holds the
credential and performs the call. Two consequences: an AI-layer defect cannot reach the provider
account, and a layer needing one completion can have it without depending on reasoning, agents,
context and memory.

**The seam, stated as a boundary rather than a feature.** Consumers flatten their entities into
`ContextItem` and hand over a `ContextBundle`. `ScopeRef` travels through opaque and is never parsed.
This is the best-designed boundary in the system and must not be broken to make any feature easier.
`AI_ARCHITECTURE.md` §6 owns the mechanism; `DEPENDENCY_RULES.md` Rule 5 owns the enforcement.

**Extension.** 4.2 by `IContextRanker` and `IPromptAssembler`; 4.3 by `IMemoryStore`; 4.4 by
registering an `IAgent` — `DeveloperAgent` proves the rule by holding no DEVELOPER type at all
(`M-04-3.1`).

**CURRENT.** Everything stateful here is in-memory and does not survive a restart:
`InMemoryTurnTraceStore`, `InMemoryMemoryStore`, `InMemoryResultReportStore`. `M-04-1.1` makes traces
and result reports durable; `M-04-1.2` does the same for memory. `DeveloperAgent.cs` is a 974-byte
stub.

---

## 8. 05 AUTOMATION

**Repository** `Nexus.Platform` · **schema** `automation` · **contracts** `Nexus.Automation.Contracts`

| | |
|---|---|
| **May depend on** | 01 CORE, 02 DATA, 03 GOVERNANCE |
| **Must never reference** | 04 and above, any product type |
| **Contract surface** | TARGET. Job enqueue with an idempotency key, handler registration by queue name, workflow definition publication, approval decision submission |

**The layer's sentence:** *Intelligence reasons, Automation executes.* AUTOMATION never calls a model
and never plans; it runs a definition somebody else authored and records what happened.

**Cross-context rules inside AUTOMATION.** 5.1 Job Runner is the foundation the rest sit on — 5.2
consumes job leases, 5.3 enqueues jobs, 5.6 composes instances. 5.5 Approvals is the one context
reaching outward, to CORE 1.3 for the approver permission check (`M-05-5.1`).

**Extension.** Shape 5 throughout — definitions, rules, schedules and trigger bindings are stored
data. The one code seam is a job handler registered against a queue name; the handler interprets the
payload, the runner does not know what it means.

**The property that makes it safe to build on.** Two concurrent dispatchers claiming the same queue
never execute the same job twice, proven by a contention test (`M-05-1.2`); enqueuing with the same
idempotency key twice yields one job row (`M-05-1.1`). Both are acceptance criteria.

---

## 9. 06 PRODUCT CORE

**Repository** `Nexus.Platform` · **schema** `product_core` · **contracts** `Nexus.ProductCore.Contracts`

| | |
|---|---|
| **May depend on** | 01 CORE, 02 DATA, 03 GOVERNANCE |
| **Must never reference** | 07–12, any product type, **and any branch on product identity** — tested rather than reviewed (`M-06-1.2`) |
| **Contract surface** | TARGET. `ScopeKindRegistration`, the scope query surface, membership and entitlement evaluation |

**The split with CORE, in one line.** CORE owns *who you are*; PRODUCT CORE owns *who you are within
a product*. Removing a product membership must not affect the Nexus identity — the `M-06-2.1`
criterion that tests whether the split held.

**6.1 Scope is the most-extended context in the system.** It owns exactly three node types —
`Workspace` → `Project` → `Subproject` — and every consumer extends *below* Subproject by
registering scope kinds:

```
Workspace → Project → Subproject          owned by 06, fixed
                          ├── DEVELOPER   → Release → Milestone → Feature → WorkItem → Task → Subtask
                          ├── a plain conversation   → stops here
                          └── a machine domain       → its own hierarchy entirely
```

`ScopeKindRegistration` is extension shape 2: the consumer declares a kind, 06 stores it and never
learns what it means. The acceptance criterion is explicit that a machine-domain consumer registers
an entirely different hierarchy **without a code change in layer 06**.

**TRANSITION.** `Workspace` and `Project` exist today as Chat product types in
`Nexus.Products.Chat.Domain`, persisted to the pre-convention `org` schema. `M-06-1.1` moves them
here — *gone, not duplicated* — and `M-02-1.5` establishes the schema convention.

---

## 10. 07 DEVELOPER

**Repository** `Nexus.Developer` (TARGET, does not exist) · **schema** `developer`

| | |
|---|---|
| **May depend on** | 01–06, and DELIVERY (08) for build results |
| **Contested** | 07 → 09 ASSURANCE and 07 → 11 EXPERIENCE. **Both are undecided; do not write the reference** — `DEPENDENCY_RULES.md` §5 |
| **Must never reference** | 12 PRODUCTS, a product's `DbContext`, a product database connection string. It holds a `ProductId` from GOVERNANCE, never a product |
| **Contract surface** | TARGET `Nexus.Developer.Contracts`. Work graph queries, dependency and parallel-safety analysis, run and result ingestion |

**Cross-context rules inside DEVELOPER.** 7.1 Work Graph is the root. 7.2 reads it and writes only
`Dependency` and `ScopeDeclaration`. 7.3 reads 7.2's verdict and never re-derives it. 7.4 ingests
DELIVERY's result artifact; **DELIVERY produces the build, DEVELOPER decides whether it satisfied a
work item**, and that sentence is the whole boundary. 7.5 derives from 7.1 and 7.4 and accepts no
hand-entered percentage — a parent not marked `BreakdownComplete` reports *not estimable* rather than
a number (`M-07-5.2`).

**7.6 Developer Conversation is a consumer, not an implementation.** DEVELOPER registers its scope
kinds with 06 and implements EXPERIENCE's `IScopeResolver` (`M-07-6.1`). It does not build chat. Its
acceptance criterion is the boundary test: *Layer 11 EXPERIENCE contains no DEVELOPER type; AI
contains no DEVELOPER type.* **GATE A owns 7.1 through 7.5 and nothing else** — 7.6 is P2, and
DEVELOPER V1a has an API and a work-graph view, no conversation surface.

---

## 11. 08 DELIVERY

**Repository** `Nexus.Platform` for contracts and records; **pipelines live per repository** ·
**schema** `delivery`

| | |
|---|---|
| **May depend on** | 01 CORE, 03 GOVERNANCE |
| **Must never reference** | 02, 04–07, 09–12. A pipeline knows repositories and artifacts; it does not know what a milestone is |
| **Contract surface** | **8.2's result artifact is this layer's most important contract** — a versioned JSON document carrying branch, commit, outcome and test counts, retrievable by branch name (`M-08-1.3`). Everything DEVELOPER learns about a build arrives through it |

**Cross-context rules.** 8.3 Artifacts are immutable once published, which makes 8.5's promotion rule
enforceable: *promotion to Staging deploys the identical artifact already in Development*
(`M-08-5.2`). 8.4 Environments carries **no maturity field** and maturity carries no environment
field — a Beta release can be running in Production without contradiction (`M-08-4.1`, `M-07-7.2`).

**CURRENT — this layer is half absent.** Source control (8.1) is in daily use across three
repositories. 8.2 through 8.6 do not exist: `NexusAI\.github\workflows\` is empty and the other two
repositories have no `.github` directory at all. `M-08-1.1` through `M-08-1.4` are the first work in
the roadmap, because the GATE A acceptance test cannot be demonstrated without CI.

---

## 12. 09 ASSURANCE

**Repository** `Nexus.Platform` · **schema** `assurance` · **cross-cutting**

| | |
|---|---|
| **May depend on** | 01 CORE, 02 DATA, 03 GOVERNANCE |
| **Must never reference** | 04–08, 10–12, **and any branch on product identity** (`M-09-7.1`) |
| **Contract surface** | TARGET. Criterion registration, evidence submission, gate evaluation, qualification query |

**9.6 Traceability is why this layer can be cross-cutting at all.** ASSURANCE verifies DEVELOPER work
items and DELIVERY pipeline runs and may reference neither. `TraceabilityLink` therefore stores what
it verified as layer, type and id in plain columns with no foreign key — extension shape 6.
Referential integrity moves into application code and is proven by test. The trade is deliberate:
losing DB-enforced integrity is cheaper than welding two layers together in the schema.

**9.1 Specification holds a closed set.** `VerificationMethod` is one of `Test`, `Inspection`,
`Analysis`, `Demonstration`, `Evaluation` — five, and adding a sixth is an architecture decision.
9.7 Profiles chooses which are *mandatory* for a product; it never adds one. **The carve-out that is
not a profile:** A safety-critical criterion cannot be waived by the ordinary
`Deviation` path, and **no agent may create, modify or waive one** — `M-09-7.2`, absolute, no
exception path.

---

## 13. 10 OPERATIONS

**Repository** `Nexus.Platform` · **schema** `operations` · **cross-cutting**

| | |
|---|---|
| **May depend on** | 01 CORE only |
| **Must never reference** | 02–09, 11, 12. It observes a running process; it does not know what built it |
| **Contract surface** | TARGET. Ingestion for logs, metrics, traces and health results; correlation-id propagation is a middleware contract rather than a type |

**10.1 is the only context contributing to GATE A**, and it contributes one thing: a correlation id
generated at the edge or accepted from the caller, propagating through the Experience API, the
Intelligence turn and the model invocation, so **one request is retrievable end to end by that id
alone** (`M-10-1.1`).

**The boundary that is easiest to blur.** 10.4 Incidents describe a *running system's* health. An
`Incident` may *produce* a DEVELOPER work item (`M-10-3.2`) — it may not hold development state.
**NOT SELECTED.** No logging library has been chosen — no Serilog, nothing. Do not write one into a
design. `OBSERVABILITY_STANDARDS.md` §2 owns that decision and records it as open.

---

## 14. 11 EXPERIENCE

**Repository** `Nexus.Experience` (TARGET) · **schema** `experience`

| | |
|---|---|
| **May depend on** | 01 CORE, 02 DATA, 04 AI, 06 PRODUCT CORE |
| **Must never reference** | 03, 05, 07, 08, 09, 10, 12 — and the conversation core must not name `Workspace`, `Project`, `Milestone`, `Feature`, `WorkItem`, `Task`, `Adr`, `Build`, `Release`, `Repository` or `Worker` (`M-11-1.2`, with the forbidden list explicit and reviewed) |
| **Contract surface** | TARGET `Nexus.Experience.Contracts`. **`IScopeResolver` and `ScopeKindBinding` are the whole outward surface**, plus the conversation API |

**11.1 Conversation Core is the one context in this document with no extension seam, on purpose.** It
stays lightweight and never learns what a Milestone is. Everything variable lives in 11.2.

**The handoff, which is the entire design:**

```
consumer registers  (scope kind, IScopeResolver)
conversation carries  ScopeRef        ← opaque, never parsed
engine calls          resolver(ScopeRef) → ContextBundle
engine passes         ContextBundle → AI, untouched
```

*Untouched* means untouched — not enriched with conversation metadata, not filtered, not reordered,
not inspected to choose an agent. The engine is a courier. That single rule is what produces **zero
shared types** between DEVELOPER, a plain conversation and a machine domain. An unregistered scope
kind produces **a clear error, not an empty bundle** (`M-11-2.1`) — an empty bundle is
indistinguishable from a misconfiguration and is how this seam would rot silently.

**TRANSITION, and it is the largest structural move in the roadmap.** A working conversation
implementation exists as `Nexus.Products.Chat.*` in `Nexus.Web`. It is a product and must become a
layer. Its `Workspace` and `Project` go to 06; its `WorkItem`, `Adr`, `Branch`, `Snapshot`,
`Artifact` and `Session` go to 07, 08 and 02; what remains becomes 11.1 and 11.4 — `M-11-1.1` and
`M-11-1.2`. **EXPERIENCE contributes nothing to GATE A**, deliberately. A standalone end-user chat
application, if ever released, is a PRODUCT at layer 12 consuming this engine; the universal
conversation engine is never called a Chat product.

---

## 15. 12 PRODUCTS

**Repository** `Nexus.Products.<Name>`, one per product · **its own database**, not a shared schema

| | |
|---|---|
| **May depend on** | 01–06, 08, 10, 11 |
| **Contested** | 12 → 07 DEVELOPER and 12 → 09 ASSURANCE — see `DEPENDENCY_RULES.md` §5. Undecided; do not write the reference |
| **Must never reference** | **Any other `Nexus.Products.*` assembly**, any other product's database, or a platform table inside its own database |
| **Contract surface** | Its own HTTP API. A product exposes types to nobody — cross-product data crosses through the owning product's API, never its database |

**12.1 is shared framework; 12.2, 12.3 and 12.4 repeat per product and never merge across products.
12.1 is the only shared context in layer 12, and it must contain no product identity.** `CapabilityPack` is a declaration a product supplies; the framework reads declarations
and never learns whose they are. `M-12-1.2` makes a branch on product identity **a build failure
across the whole solution**, and `M-06-1.2` and `M-09-7.1` state the same rule independently for
PRODUCT CORE and ASSURANCE. Three layers say it separately because it is the rule most likely to be
broken by a small, reasonable-looking change.

**CURRENT.** One product exists — Chat, in `Nexus.Web` — and it predates all of this. It is a
reference, not a template, and most of what it currently owns belongs to layers 02, 06, 07, 08
and 11. `PRODUCT_ARCHITECTURE.md` §13 carries the detail.

---

## 16. Reading the map against reality

Three honest statements, because a context map describing only the target is a map of a system nobody
can build against today.

1. **Nine of the twelve layers have no assemblies.** The direction is correct almost everywhere *by
   accident*. These contexts are written now so the nine are built correctly rather than corrected later.
2. **Three architecture test projects exist and no pipeline runs them** — `PlatformBoundaryTests.cs`,
   `BoundaryRuleTests.cs`, `BoundaryTests.cs`. `M-08-1.4` makes them a hard gate; until then every
   boundary here is held by review.
3. **Exactly two behaviour tests exist in the entire system** — `KeywordContextRankerTests.cs` and
   `ChatContextBundleMapperTests.cs`. The second is the only test covering the context seam.

**The rule when a context is not yet real:** build the *seam* first even when the implementation is a
stub. A context registered through shape 1 or 2 with one implementation costs almost nothing today
and is the difference between adding the second consumer and rewriting for it.

---

## 17. References

- `LAYER_MODEL.md` — what each layer is, its purpose and its gate slice.
- `DEPENDENCY_RULES.md` — the seven rules, the 12×12 matrix, the contested cells, enforcement status.
- `DATA_OWNERSHIP.md` — which layer owns which entity, and the migration matrix for what exists today.
- `DATABASE_ARCHITECTURE.md` — schema per layer, one database per product, cross-schema access.
- `INTEGRATION_ARCHITECTURE.md` · `EVENT_ARCHITECTURE.md` · `SECURITY_ARCHITECTURE.md` ·
  `PRODUCT_ARCHITECTURE.md` — how these contexts talk, emit, are bounded, and are composed.
- `AI_ARCHITECTURE.md`, `ASSURANCE_ARCHITECTURE.md`, `DELIVERY_ARCHITECTURE.md`,
  `DEVELOPER_ARCHITECTURE.md`, `EXPERIENCE_ARCHITECTURE.md`, `OPERATIONS_ARCHITECTURE.md` — the
  internal design of six of these layers.
- `../nexus-roadmap.yaml` — each layer's `owns`, `does_not_own`, `depends_on` and features.
