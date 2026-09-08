# Data Ownership

> **SUPERSEDED NUMBERING NOTICE (2026-09-05):** This document's entity-to-layer mapping
> and "Does not own" cross-references are built on the v2.1 twelve-layer model, in which
> 07 DEVELOPER was a numbered Platform layer and DELIVERY/ASSURANCE/OPERATIONS/EXPERIENCE
> were numbered 08/09/10/11. Per the approved v2.2 renumbering (`LAYER_MODEL.md` §2.2,
> §4a), Nexus Forge and Nexus Developer (the product) now sit OUTSIDE the ten numbered
> Platform layers, and DELIVERY/ASSURANCE/OPERATIONS/EXPERIENCE are renumbered
> 07/08/09/10. The ownership reasoning below (domain-owns-the-fact, the migration matrix)
> remains historically accurate engineering judgment. Re-deriving the entity-to-layer
> mapping against the v2.2 numbering (and against the Forge/Nexus-Developer-Product
> split) is Wave-D-adjacent decision work and is explicitly NOT done in this batch.

**Status:** Active for the rule and the mapping; **TRANSITION** for reality — six of the eleven
entities that exist today are in the wrong layer, and each row says which milestone moves it
**Owner:** Durai; each layer's owner maintains its own entity list
**Last updated:** 2026-08-21
**Layer:** cross-cutting
**Authoritative for:** which layer owns which structured fact, the domain-owns-the-fact rule, the
complete entity-to-layer mapping, names that legitimately appear in more than one layer, and the
migration matrix for every entity that exists today.

Not authoritative for: what a layer is (`LAYER_MODEL.md`), whether one layer may *reference*
another (`DEPENDENCY_RULES.md`), or where the data physically sits
(`DATABASE_ARCHITECTURE.md`).

---

## 1. The rule

> **The domain owns the structured fact. DATA owns its document representation. Never both, never
> neither.**

**Never both** because a fact stored twice is a fact that disagrees with itself the first time one
copy changes, and nobody knows which copy is authoritative until someone is already acting on the
wrong one. A completion percentage in DEVELOPER's tables *and* in a markdown file is not redundancy;
it is a future incident.

**Never neither** because the alternative to a structured fact is a sentence in a document that
something has to re-derive every time it is needed — which is exactly the state Nexus is being built
out of. Development state living in chat transcripts is the canonical example, and this document set
exists partly because it happened.

The rule has a corollary that catches most mistakes: **a document is never a substitute for a row.**
If a fact is worth querying, filtering, aggregating or depending on, it is a row in the owning
layer, and any document about it links to that row by ID.

---

## 2. The worked examples

Three pairs. Each is a fact and a document that are constantly confused for one another.

| The structured fact | Owner | The document about it | Owner |
|---|---|---|---|
| Milestone `M-07-1.1` exists, is blocked by `M-02-1.4`, has 7 of 10 work items complete | **07 DEVELOPER** | The written specification describing what `M-07-1.1` is for | **02 DATA** |
| The verification run passed, against this criterion, with this evidence, at this time | **09 ASSURANCE** | The formal test report, versioned, signed | **02 DATA** |
| This trademark is registered, in this territory, expiring on this date | **03 GOVERNANCE** | The trademark certificate PDF | **02 DATA** |

Extend the first one across the whole system and the shape of the model becomes visible:

| Thing | Owner | Form |
|---|---|---|
| That the milestone exists, is blocked, and is 70% complete | **07 DEVELOPER** | Structured rows |
| The specification describing what it is for | **02 DATA** | Document, versioned, linked to `M-07-1.1` by ID |
| That the product it belongs to is registered, owned by Durai, classified internal | **03 GOVERNANCE** | Structured rows |
| That the acceptance criterion was verified and by what method | **09 ASSURANCE** | Structured rows plus evidence |
| The branch the work happened on, the artifact it produced, the environment it reached | **08 DELIVERY** | Structured rows |
| That the deployed thing is healthy and served 4,000 requests | **10 OPERATIONS** | Time-series |
| The conversation in which it was all discussed | **11 EXPERIENCE** | Conversation and messages, scoped by an opaque `ScopeRef` |

Seven owners, one milestone, no duplication. Note what is *not* in that list: nothing owns "the
current state of the project" as a single thing. There is no such row, and asking for one is how
architectures acquire a `Status` enum that means four different things.

---

## 3. The test, when you are unsure

Four questions, in order. Stop at the first that answers.

1. **Would you query, filter or aggregate on it?** Then it is a row, not a document.
2. **Which layer would be wrong if it were absent?** That layer owns it. If a milestone with no
   owner is DEVELOPER's problem, `Milestone` is DEVELOPER's.
3. **Is it a statement about something, or the thing itself?** Statements about what exists and who
   is accountable are GOVERNANCE. The thing being built is DEVELOPER. The thing running is
   OPERATIONS.
4. **Do two layers both plausibly own it?** Then you have one name doing two jobs. Split it — see
   §6, where `Session` and `Artifact` are the existing cases.

If none of the four answers, it is genuinely undecided. Write "Not yet decided", name what would
decide it, and do not create the table.

---

## 4. Ownership by layer

Capability-level ownership — what kind of thing each layer owns — is `LAYER_MODEL.md`. This is the
entity list, and it is the authority when the two are read together. Entity names are the type names
as the architecture defines them.

### 01 CORE — schema `core`

| Owns | |
|---|---|
| Identity | `User`, `Credential`, `Session`, `Organisation`, `Tenant` |
| Access | `Role`, `Permission`, `Policy` |
| Governance primitives | `AuditEntry`, `UsageRecord`, `SecretRef` |
| Capability descriptors | `ModelDescriptor`, `ToolDescriptor`, `NotificationChannel` |

**Does not own:** any product concept, any development concept, documents, workflow definitions,
domain rules, conversation content.

### 02 DATA — schema `data`

| Owns | |
|---|---|
| Documents | `Document`, `DocumentVersion`, `DocumentMetadata` |
| Knowledge | `KnowledgeItem`, `Source`, `Reference`, `Provenance` |
| Retrieval | `Embedding`, `IndexEntry` |
| Governance of information | `Classification`, `RetentionPolicy`, `Lineage` |

**Does not own:** the structured facts themselves. Every document in this set — this file included —
becomes a `Document` here at `M-02-2.1`.

### 03 GOVERNANCE — schema `governance`

| Owns | |
|---|---|
| Product registry | `Product`, `ProductOwnership`, `ProductClassification`, `ProductLifecycleState` |
| Technology registry | `Technology`, `TechnologyVersion`, `ProductTechnologyUsage` |
| Brand and domain | `Brand`, `Trademark`, `DomainReference`, `DnsRecordReference`, `Certificate` |
| Compliance | `ComplianceObligation`, `ComplianceAttestation`, `PrivacyRequirement`, `DataResidencyDeclaration` |
| Commercial | `Licence`, `LicenceAssignment`, `ExternalService`, `ExternalServiceDependency` |
| Standards | `ConfigurationEntry`, `Standard`, `StandardConformance` |

**Does not own:** what is being built (07), how it ships (08), how it runs (10), the certificate or
attestation *document* (02).

### 04 AI — schema `ai`

| Owns | |
|---|---|
| Reasoning assets | `Agent`, `AgentCapability`, `Prompt`, `PromptVersion`, `ModelRoute` |
| Turn state | `TurnTrace`, `ResultReport` |
| Memory | `MemoryRecord` |
| Quality | `Evaluation`, `Guardrail` |

**Does not own:** product data, conversation storage (11), documents (02), provider credentials
(01), or any knowledge of what a `Workspace` or a `Milestone` is.

### 05 AUTOMATION — schema `automation`

| Owns | |
|---|---|
| Definition | `WorkflowDefinition`, `WorkflowVersion`, `WorkflowStep`, `Rule`, `Condition`, `StateMachineDefinition` |
| Execution | `WorkflowInstance`, `StateTransition`, `WorkflowResult` |
| Scheduling | `Trigger`, `Schedule`, `EventHandler` |
| Jobs | `Queue`, `Job`, `JobAttempt`, `RetryPolicy` |
| Human gates | `ApprovalGate`, `ApprovalDecision`, `EscalationPolicy` |
| Composition | `ProcessOrchestration` |

**Does not own:** the business meaning of what it runs.

### 06 PRODUCT CORE — schema `product_core`

| Owns | |
|---|---|
| Scope trunk | `Workspace`, `Project`, `Subproject` |
| Product-level identity | `ProductProfile`, `ProductMembership` |
| Commercial | `Plan`, `Subscription`, `Entitlement`, `Quota`, `FeatureFlag` |
| Preferences | `ProductSetting`, `Preference`, `OnboardingState` |

**Does not own:** identity (01), product identity in the registry sense (03), domain data (12),
development structure below `Subproject` (07).

### 07 DEVELOPER — schema `developer`

| Owns | |
|---|---|
| Work graph | `ProductDevelopment`, `Module`, `Feature`, `Requirement`, `Release`, `Milestone`, `WorkItem`, `Task`, `Subtask` |
| Analysis | `Dependency`, `ScopeDeclaration` |
| Execution | `Worker`, `WorkerAssignment`, `DevelopmentRun` |
| Interpretation | `BuildRecord`, `TestRun`, `Review`, `IntegrationRun`, `DevelopmentResult` |
| Derived state | `ProgressState`, `StatusHistory` |

**Does not own:** `Product` identity (03), the scope trunk (06), the pipeline that produced the
build (08), whether the requirement was satisfied (09), runtime health (10), the specification
document (02), conversation storage (11).

`BuildRecord` and `TestRun` are DEVELOPER's *interpretation* of what DELIVERY produced. DELIVERY
owns `PipelineRun` and `BuildArtifact`; DEVELOPER owns the judgement that a given run satisfies a
given work item. Two rows, two layers, one event.

### 08 DELIVERY — schema `delivery`

| Owns | |
|---|---|
| Source | `Repository`, `GitBranch`, `Tag`, `Commit` |
| Build | `Pipeline`, `PipelineRun`, `BuildArtifact` |
| Environments | `Environment`, `Deployment`, `InfrastructureResource` |
| Recovery | `BackupRecord`, `RestorePoint` |

**Does not own:** the meaning of a build (07), whether a requirement was met (09), runtime health
(10), product identity (03).

`GitBranch` is named with the `Git` prefix deliberately — `Branch` alone collides with the existing
Chat aggregate and with the branch concept in any product that has one.

### 09 ASSURANCE — schema `assurance`

| Owns | |
|---|---|
| Plans | `QualityPlan`, `VerificationPlan`, `ValidationPlan`, `TestPlan`, `InspectionPlan` |
| Specification | `TestCase`, `InspectionCharacteristic`, `AcceptanceCriterion`, `VerificationMethod`, `ValidationMethod` |
| Execution | `VerificationRun`, `ValidationRun`, `InspectionRun`, `Evidence` |
| Findings | `Defect`, `Deviation`, `NonConformance`, `CorrectiveAction` |
| Verdict | `QualityGate`, `QualificationResult`, `TraceabilityLink` |

**Does not own:** what needs testing and which work item it belongs to (07), executing pipelines
(08), runtime health (10), the formal test report *document* (02), the `Requirement` itself (07 owns
`Requirement`; ASSURANCE owns its `AcceptanceCriterion`).

That last split is the sharpest ownership boundary in the system and the one most likely to be got
wrong: **a requirement and its acceptance criterion live in different layers, deliberately.** The
requirement is what someone wants; the criterion is how anyone would know it was delivered, and the
layer that judges must own the second.

### 10 OPERATIONS — schema `operations`

| Owns | |
|---|---|
| Telemetry | `LogStream`, `Metric`, `Trace` |
| Health | `HealthCheck`, `Incident`, `Alert` |
| Efficiency | `PerformanceRecord`, `CapacityRecord`, `CostRecord` |
| Runtime control | `FeatureFlagState` |

**Does not own:** anything durable about what was built or why (07), how it shipped (08).

`FeatureFlag` (06 PRODUCT CORE) is the *definition* — this product has this flag. `FeatureFlagState`
(10) is the *runtime value* in a given environment. Same word, two facts, two layers.

### 11 EXPERIENCE — schema `experience`

| Owns | |
|---|---|
| Conversation core | `Conversation`, `Message`, `Participant`, `Attachment`, `ConversationSession` |
| References out | `MemoryReference`, `KnowledgeReference`, `ToolUsage`, `ResultReference` |
| Extensibility | `ScopeKindBinding` |
| Interaction surface | `UIPreference`, `CommandDefinition`, `NotificationDelivery` |

**Does not own:** contextual structure of any kind — `Workspace` and `Project` are 06, `Milestone`
and `WorkItem` are 07 — product domain data, documents, or model access.

The four `*Reference` types are the whole design: the conversation core *points at* memory,
knowledge, tool results and outcomes without owning or interpreting any of them.

### 12 PRODUCTS — own database per product

Owns everything domain-specific to that product, and only that. There is no shared list, because a
shared list would be a shared kernel. A product owns no entity that a lower layer owns — if a
product finds itself defining `Workspace`, `Subscription` or `Milestone`, that is the boundary
being broken, not a new requirement.

---

## 5. Facts that are derived, not owned

Some values look like facts and must never be stored as one. Storing them creates a second source of
truth that drifts.

| Value | Derived from | Owner of the derivation |
|---|---|---|
| Progress % | Completed children ÷ total children, weighted by estimate | 07 DEVELOPER |
| `DevelopmentStage` | Milestone states | 07 DEVELOPER |
| `DevelopmentHealth` | Blocked work items and failing builds | 07 DEVELOPER |
| Blocked | Any unmet blocking dependency | 07 DEVELOPER |
| `CurrentProductionRelease` | Deployment records | 08 DELIVERY |
| `DeploymentState` | Deployment records | 08 DELIVERY |
| `OperationalHealth` | Health checks | 10 OPERATIONS |
| Actual completion date | The integration record | 07 DEVELOPER |

**The honesty rule.** Derived progress on an incomplete breakdown is worse than no progress at all,
because it looks authoritative. A milestone with three declared work items out of an eventual twenty
reports 33% and means nothing. Progress is therefore derived **only where the parent is explicitly
marked `BreakdownComplete`**; until then it reports "not estimable". That one flag is the difference
between a progress model that earns trust and one that quietly lies.

Manual by design: risk, declared dependencies, target date, `ProductLifecycleState`,
`ComplianceState`, `CurrentRelease`.

---

## 6. Names that legitimately appear twice

These are not duplication. They are different facts that share an English word, and each pair needs
distinct type names so the collision cannot survive into code.

| Word | One fact | The other |
|---|---|---|
| **Session** | `Session` — a signed-in user session (**01 CORE**) | `DevelopmentRun` — a worker's development session (**07 DEVELOPER**). The Chat aggregate `Session` conflates them |
| **Artifact** | `BuildArtifact` — a build output (**08 DELIVERY**) | A work product attached to a work item (**07 DEVELOPER**). The Chat aggregate `Artifact` conflates them |
| **Project** | `Project` — the scope trunk node (**06 PRODUCT CORE**) | A product's own project concept, if it has one (**12**) — which it should not, because the trunk exists |
| **Branch** | `GitBranch` — real git state (**08 DELIVERY**) | Nothing else. DEVELOPER *references* a branch; it does not own one |
| **Conversation** | `Conversation` — the universal core (**11 EXPERIENCE**) | Consumer-specific conversation attributes, held by the consumer |
| **FeatureFlag** | `FeatureFlag` — the definition (**06 PRODUCT CORE**) | `FeatureFlagState` — the runtime value (**10 OPERATIONS**) |
| **Feature** | `Feature` — a unit of the work graph (**07 DEVELOPER**) | A capability a product offers — expressed as `Entitlement` (**06**), never as a second `Feature` |
| **Product** | `Product` — the registry row (**03 GOVERNANCE**) | `ProductDevelopment` — what is being built in it (**07**), `ProductProfile` — who you are inside it (**06**) |
| **Result** | `WorkflowResult` (**05**), `ResultReport` (**04**), `DevelopmentResult` (**07**), `QualificationResult` (**09**) | Four genuinely different verdicts. Never a shared `Result` type |

When a word appears twice, the rule is: **the more specific name goes to the newer owner.** `Branch`
became `GitBranch`; `Session` splits into `Session` and `DevelopmentRun`; `Artifact` splits into
`BuildArtifact` and a work-item attachment.

---

## 7. Entity migration matrix

### 7.1 The eleven Chat aggregates

Every aggregate in `Nexus.Products.Chat.Domain` today, each with an aggregate root, a strongly-typed
ID, a status enum and a repository interface. **`KEEP` and `MOVE` dominate — nothing is thrown
away.**

| Entity | Action | Goes to | Reasoning |
|---|---|---|---|
| `Workspace` | **MOVE** | 06 PRODUCT CORE | Recurs across every consumer, so it cannot belong to one product. Not CORE's `Organisation` — that is tenancy, this is a workspace. `M-06-1.1` |
| `Project` | **MOVE** | 06 PRODUCT CORE | Same reasoning; it is the second node of the shared scope trunk. `M-06-1.1` |
| `Conversation` | **SPLIT** | 11 EXPERIENCE (core) + the consumer | The universal core — participants, messages, scope binding — becomes the engine. `ConversationType` and `ConversationVisibility` are consumer attributes and do not follow it. `M-11-1.1` |
| `ConversationMessage` | **MOVE** | 11 EXPERIENCE | Part of the universal core. Becomes `Message`. `M-11-1.1` |
| `Knowledge` | **MOVE** | 02 DATA | Knowledge is explicitly a DATA concept. It is a Chat aggregate only because Chat was the only product. `M-02-3.1` |
| `Adr` | **MOVE + REFACTOR** | 02 DATA | An ADR is a `Document` with a decision lifecycle, not its own aggregate. Becomes `Document` plus decision metadata. `M-02-2.1`, and `ADR_STANDARD.md` owns the format |
| `WorkItem` | **MOVE** | 07 DEVELOPER | Already the right shape, wrong home. It carries a `milestone` foreign key pointing at a `Milestone` that does not exist yet; `M-07-1.1` supplies it |
| `Artifact` | **SPLIT** | 07 DEVELOPER + 08 DELIVERY | A build output is `BuildArtifact` and DELIVERY's. A work product attached to a work item is DEVELOPER's. Currently one type doing both jobs |
| `Branch` | **MOVE** | 08 DELIVERY | Git branch state is DELIVERY's; it becomes `GitBranch`. DEVELOPER references it, never owns it |
| `Snapshot` | **MOVE** | 08 DELIVERY | Same reasoning as `Branch` — a point-in-time source state is delivery mechanics |
| `Session` | **SPLIT** | 01 CORE + 07 DEVELOPER | A user session is CORE's `Session`. A development session is DEVELOPER's `DevelopmentRun`. Two different things sharing a name |

**Sequencing — this is the part that gets rushed.** Every `MOVE` above is **post-gate**. Doing them
before the gate means moving code while simultaneously building on it. `M-02-1.2` and `M-02-1.3`
migrate all eleven aggregates to Azure SQL **in place**, under the Chat product; they change layer
afterwards. The one exception: `WorkItem` may move during `M-07-1.1` if that proves cheaper than
referencing across the boundary.

**The observation behind all of it:** six of the eleven aggregates in the Chat product are not about
chat. `WorkItem`, `Adr`, `Branch`, `Snapshot`, `Session` and `Artifact` are about *software
development*, and they are the seed of DEVELOPER.

### 7.2 Everything else that exists

Summarised; `NEXUS_MASTER_ARCHITECTURE.md` Part 16 carries the full per-type reasoning.

| Item | Action | Owner after | When |
|---|---|---|---|
| `Nexus.Platform.Contracts/Models/*` (13 types), `Tools/*` | **KEEP** | 01 CORE | — |
| `IIdentityService`, `ITenantResolver`, `ResolvedIdentity` | **KEEP + EXTEND** | 01 CORE | `M-01-1.1` makes them real |
| `IAuditLog`, `IUsageMeter`, `IQuotaPolicy` | **KEEP + EXTEND** | 01 CORE | Durable implementations at `M-01-4.1` |
| `IProductRegistry` | **MOVE** | 03 GOVERNANCE | `M-03-1.2`. Right concept, wrong layer — it currently sits in `Contracts/Identity/` |
| `ConsoleAuditLog`, `InMemoryUsageMeter`, `PermissiveQuotaPolicy` | **REPLACE** | 01 CORE | Keep as test doubles; replace in production wiring at `M-01-4.1` |
| `IdentityProvider.cs` (240 B), `PlatformStore.cs` (308 B) | **REPLACE** | 01 CORE | Superseded entirely by `M-01-1.1` / `M-01-4.1` |
| `ChatTurnIdentity` — hardcoded tenant and permissions | **REPLACE** | 01 CORE | `M-01-1.1`. This is the only authorization subject in the system today |
| `Nexus.Intelligence.Contracts/Turns/*`, `Context/*` | **KEEP — do not touch** | 04 AI | The seam. `ContextBundle`, `ContextItem`, `TrustLevel`, `ScopeRef` |
| `InMemoryTurnTraceStore`, `InMemoryResultReportStore`, `InMemoryMemoryStore` | **REPLACE** | 04 AI | `M-04-1.1`, `M-04-1.2` |
| `DeveloperAgent.cs` (974 B) | **EXTEND** | 04 AI | Becomes the agent that reasons about the work graph, `M-04-3.1` |
| Dataverse repositories, mappers and packages | **REMOVE** | — | `M-02-1.4`, with the `Nexus:Persistence` switch and the `System.Security.Cryptography.Xml` pin |
| `NexusAI.Agents`, `.Api`, `.Core`, `.Domain`, `.Foundation`, `.Host`, `.Infrastructure` | **REMOVE** | — | Empty gitignored husks. Deleting them removes ambiguity about where code goes |

---

## 8. Not yet decided

| Question | What would decide it |
|---|---|
| Where a product's own conversation attributes live once `Conversation` splits | `M-11-1.1`. Candidates: a consumer-owned side table keyed by conversation ID, or attributes carried in the `ScopeRef` resolution |
| Whether `Requirement` stays in DEVELOPER once ASSURANCE owns criteria and plans | `M-07-7.1` Requirements. The current split — DEVELOPER owns `Requirement`, ASSURANCE owns `AcceptanceCriterion` — is deliberate but has not survived contact with an implementation |
| Which entities are soft-deleted, and their retention class | `M-02-5.1` Classification and retention |
| Whether the work-item attachment half of `Artifact` is a DEVELOPER entity or a DATA `Document` | `M-07-4.1`. It may be that DEVELOPER owns only the link and DATA owns the artifact itself, which would be more consistent with §1 |

Each is genuinely open. None is blocked on this document.

---

## 9. References

- `LAYER_MODEL.md` — what each layer is, and its capability-level ownership.
- `DEPENDENCY_RULES.md` — whether the owning layer may be referenced from where you are standing.
- `DATABASE_ARCHITECTURE.md` — which schema and which database a layer's entities live in.
- `DATABASE_STANDARDS.md` — how to model an entity once you know its layer.
- `ASSURANCE_STANDARDS.md` — the traceability chain from requirement to evidence to verdict.
- `DOCUMENTATION_STANDARDS.md` — the structured-state-versus-document division, from the document
  side.
- `../NEXUS_MASTER_ARCHITECTURE.md` Parts 6 and 16 — the ownership and entity migration matrices.
- `../nexus-roadmap.yaml` — each layer's `owns:` and `does_not_own:` lists.
