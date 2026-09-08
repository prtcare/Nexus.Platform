# Work Universe — Cross-Domain Relationship Model

> **Status** Authoritative · **Owner** Durai · **Last updated** 2026-09-07 · **Architecture version** v2.3
> **Authoritative for** the Work Universe cross-domain relationship model: its domains, lifecycle, what is reused from existing V2.3 concepts, what is genuinely new, and the traceability it targets. This document does not restate the detailed architecture of any layer or product — it links to the specialized docs that already own those subjects (see the reference table in §7).

This document was produced by WU-01 (read-only existing-model inventory) and WU-02 (minimal architecture amendment). It sits inside the frozen Nexus V2 Foundation Baseline — Architecture Baseline v2.3. **It is not a new architecture, not Nexus V3, not a numbered Platform layer, and not a graph-database requirement.** It is a naming and relationship discipline layered over concepts that, in most cases, already exist and already work.

---

## 1. Definition

**Nexus Work Universe** — the set of domains and typed relationships that let anyone, human or AI, trace a unit of work from idea to operations.

Primary domains: **PRODUCT · FEATURE · PLANNING · CHAT & CONTEXT · DEVELOPMENT · OUTCOME · RELEASE & DEPLOYMENT**

Lifecycle: **IDEA → PRODUCT → FEATURE → PLANNING → DEVELOPMENT → VERIFICATION → OUTCOME → RELEASE → DEPLOYMENT → OPERATIONS / FEEDBACK**

Chat & Context may attach at any point in this lifecycle, not only at the start.

This is a **cross-domain relationship model**, not a rigid hierarchy. Trees remain valid for local organization and navigation within a domain (e.g. Branch → Snapshot inside Chat). Typed links provide traceability *across* domains. No domain is required to route through another domain's storage to exist.

---

## 2. Final object map

| Object | Domain | Disposition | Notes |
|---|---|---|---|
| Product identity (id, slug, name, classification, lifecycle state) | PRODUCT | **ARCHITECTURE-ONLY-SP1** — owner decided, not yet built | Owned by 03 GOVERNANCE (`Product` aggregate, `IProductRegistry`), per the already-specified `M-03-1.1`/`M-03-1.2`. Nexus Developer references this identity; it does not define its own. See §3. |
| Product profile / membership / entitlement | PRODUCT (runtime context) | ARCHITECTURE-ONLY-SP2 | Owned by 06 SHARED PLATFORM (`ProductProfile`, `ProductMembership`), distinct from identity. Not required for SP1 traceability. |
| Nexus Developer's work-management view of a product | PRODUCT (work projection) | NEW-SP1 (minimal) | A thin `ProductDevelopment`-style projection in Nexus Developer, referencing GOVERNANCE's `ProductId` once it exists — never inventing its own identity. |
| `Feature` | FEATURE | **KEEP** | Nexus Developer Core, unchanged. |
| Subfeature | FEATURE | NEW-SP1 (minimal) | Represented as `Feature.ParentFeatureId` (self-reference), not a parallel aggregate. A Feature with no parent displays as Feature; a Feature whose parent is another Feature displays/interprets as Subfeature. Feature identity is preserved either way. |
| `Milestone`, `MilestoneLink`, `Task`, `Subtask` | PLANNING | **KEEP** | Nexus Developer Core, unchanged. ADR-005's non-hierarchical Feature↔Milestone decision remains — Feature → Milestone → Task is not a mandatory hierarchy; many-to-many `MilestoneLink` remains the mechanism. |
| `Issue`, `IssueLink` | PLANNING | **KEEP** | Unchanged. |
| `WorkItemDependency`, `DependencyGraphTraversal` | PLANNING | **KEEP** | Unchanged. |
| `Conversation`, `ConversationMessage`, `Session` | CHAT & CONTEXT | **KEEP** | Nexus.Experience, canonical Chat, unchanged. |
| `ObjectChatLink`, `ObjectChatLinkTargetType` | CHAT & CONTEXT | **KEEP** | Nexus Developer Core, canonical Chat↔work-object linkage, unchanged. Target vocabulary extends later (Product/Outcome/etc.) only once those domains are real. |
| `Branch` + `ParentBranchId` | CHAT & CONTEXT (Subchat) | NEW-SP1 (minimal extension) | Subchat is a UX/domain interpretation of a nested `Branch`, not a second chat aggregate. `Branch` already exists; only a self-referencing `ParentBranchId` is new. |
| Turn/evidence traceability (`ConversationMessage` ↔ `TurnTrace`/`ResultReport` via `ScopeRef`) | CHAT & CONTEXT | NEW-SP1 (minimal plumbing) | See §4. Extends `ScopeRef` population and adds indexing — no new conversation/context subsystem. |
| `DevelopmentRun` | DEVELOPMENT | **KEEP, unchanged meaning** | One actual execution attempt. Not renamed to "Development." See §5. |
| `ActiveChange` | DEVELOPMENT | **KEEP, unchanged meaning** | Governed change/reservation envelope. |
| `PreflightDeclaration` | DEVELOPMENT | **KEEP, unchanged meaning** | Declared scope/safety envelope. |
| "Development" (Work Universe lifecycle stage) | DEVELOPMENT | Strong default: **A — no persisted aggregate**, a projection/view over `DevelopmentRun`/`ActiveChange`/`PreflightDeclaration`/`Task`/`Subtask` | See §5. `DevelopmentUnit`/`DevelopmentStep` are NOT introduced in SP1 — no concrete requirement demonstrated; both strongly overlap existing types. |
| `PipelineRunResult` | DEVELOPMENT / RELEASE | **KEEP** | Partial evidence input for future Release/Deployment; not itself a Release or Deployment aggregate. |
| Outcome | OUTCOME | NEW-SP1 (minimal) | Genuinely new. See §6. Must reference Assurance evidence, never self-declare verification status. |
| Release, Deployment, Environment, Version, Artifact | RELEASE & DEPLOYMENT | **ARCHITECTURE-ONLY-SP2** | Terminology, responsibility and traceability expectations frozen now (§7 below and `DEVELOPMENT_WORKFLOW.md`); full implementation is SP2. A minimal nullable reference placeholder is allowed only where a concrete SP1 traceability need exists — none has been demonstrated yet. |
| Generic `WorkRelationship { From, RelationshipType, To }` engine | (cross-cutting) | **REJECTED for SP1** | Specialist relationships (`MilestoneLink`, `IssueLink`, `WorkItemDependency`, `ObjectChatLink`, Forge's `Dependencies & Blockers`) retain authority for their own semantics. Narrow, purpose-built link types are added only where a real cross-domain relationship is missing (Product↔Feature, Feature↔Outcome, Release↔Deployment, roadmap-ledger↔runtime aggregate) — never a universal relationship table, and never directional duplicate records where one link already suffices. |
| Roadmap-ledger ↔ runtime-aggregate identity bridge | (cross-cutting) | NEW-SP1 (narrow) | See §8. Two identity systems remain: the roadmap ledger (`F-`/`M-`/`WI-`/`T-`/`S-`/…) and Guid-backed runtime Core aggregates. They are not merged and neither is renumbered. |
| `DevelopmentControlAddress { DevelopmentControlRole Role; NodeId Id; }` | (cross-cutting, DevelopmentControl) | **Approved SP1 implementation milestone** — not part of WU-02 itself | Serves DevelopmentControl objects across Foundation/Products. `WorkObjectAddress` is not introduced yet; revisit only after real `DevelopmentControlAddress` usage exists. |
| Two-workbook split (Foundation / Products, same governed schema, Role + NodeId addressing) | (cross-cutting, DevelopmentControl) | **SP1 work, not WU-02 implementation** | Design frozen since R05; not classified as SP2. |
| Forge `DependencyLineage.ps1`, `ContextPackage.ps1`, `TaskClassification.ps1`, `router/*` | (Forge, Development Plane) | **KEEP** | Reused and extended at the resolution layer only (see §9); Forge gains no new context engine. |

---

## 3. Product identity — corrected finding

WU-01 characterized Product as "genuinely greenfield" based on inspecting only Nexus Developer's Core layer. WU-02 rechecked this against the wider Platform and found the ownership question is **already decided, just not yet executed**:

- **Product identity** (id, slug, name, classification, lifecycle state, `IProductRegistry`) is documented as belonging to **03 GOVERNANCE** — see `PRODUCT_ARCHITECTURE.md`, `NEXUS_MASTER_ARCHITECTURE.md`, and `nexus-roadmap.yaml` milestones `M-03-1.1` (Governance schema and Product record) and `M-03-1.2` (relocate `IProductRegistry` into `Nexus.Governance.Contracts`). A stub `IProductRegistry` interface already exists in `Nexus.Platform.Contracts.Governance`, unimplemented, awaiting relocation per `M-03-1.2`.
- **Product profile / membership / entitlement** (the runtime "who you are inside it" context) is documented as belonging to **06 SHARED PLATFORM** (`ProductProfile`, `ProductMembership`), a distinct concept from identity, not required for SP1 Work Universe traceability.
- **Nexus Developer's work-management view of a product** is genuinely new and belongs in Nexus Developer — but it must **reference** GOVERNANCE's `ProductId` once implemented, not invent its own identity.
- **The canonical roadmap's `products:` entries** are planning/build-out metadata only, keyed by a legacy `feature.id` (inherited from the old v2.2 layer-12 taxonomy) — not a runtime `ProductId`, and not the identity Nexus Developer or GOVERNANCE should treat as authoritative at runtime.

No `ProductId` type/value-object exists in code today anywhere (only a plain `string ProductId` field, e.g. in `InvocationIdentity`, `ResolvedIdentity`, Intelligence's turn/result/memory contracts, and one hard-coded value `"nexus.chat"` in Nexus.Experience). Standing up a typed `ProductId` and the GOVERNANCE `Product`/`IProductRegistry` implementation (`M-03-1.1`/`M-03-1.2`) is a precondition for Nexus Developer's product work-management view (`M-07-1.1`), not a parallel, independent decision Developer can make on its own.

---

## 4. Turn / evidence traceability

A real, confirmed gap: `TurnId` is currently discarded once `SendChatHandler` (Nexus.Experience) completes a turn, and `TurnTrace`/`ResultReport` (Nexus.Intelligence) are not indexed by `Scope`/conversation. The minimum future design to close this, using only existing types:

```
Work Object → ObjectChatLink → Conversation → ConversationMessage / Turn → TurnTrace → ResultReport → Evidence
```

- `ScopeRef(Kind, Key, Path)` is already generic enough to carry a work-object kind directly; it simply is not populated that way by any caller today.
- `ObjectChatLink` remains the canonical Chat↔work-object bridge; its `MessageRangeStart/End` fields already give message-level provenance.
- No second conversation/context subsystem is introduced. The gap is closed by (a) persisting the returned `TurnId` back onto the conversation/message side, and (b) indexing `TurnTrace`/`ResultReport` storage by `Scope` so a work object can be traced forward to every turn and outcome that touched it.

---

## 5. Development-domain mapping

The Work Universe's "Development" lifecycle stage is **not** a rename of `DevelopmentRun`, and WU-02 does not freeze that interpretation. The already-approved V2.3 meanings remain exactly as they were:

- `DevelopmentRun` = one actual execution attempt.
- `ActiveChange` = governed change/reservation envelope.
- `PreflightDeclaration` = declared scope/safety envelope.
- `Task`/`Subtask` = planned/executable work.

Strong default for "Development" as a Work Universe concept: **(A) no persisted aggregate — a projection/view over the four types above.** `DevelopmentUnit` and `DevelopmentStep` are explicitly **not** introduced in SP1: `DevelopmentStep` strongly overlaps `Task`/`Subtask`, and `DevelopmentUnit` may overlap `ActiveChange`. Reuse is preferred over new granularity unless concrete evidence demonstrates a real requirement neither existing type can serve.

---

## 6. Outcome — minimum SP1 design

Outcome is confirmed genuinely new — no analog exists in Nexus.Developer, Forge, the roadmap ledger, or Nexus.Intelligence/Experience. Outcome means the actual result/value produced by a unit of work — **not** merely Task Complete, Build PASS, Test PASS, Assurance PASS, or Deployment succeeded, each of which is an input signal, not the Outcome itself.

Candidate minimum shape (frozen as a direction, not a final schema — actual field adoption happens at implementation time, not in this document):

- `OutcomeId`
- Product/Feature relationship (what this Outcome is an outcome of)
- Description
- Success criteria
- Status
- Assurance evidence reference (never a self-declared verification status — see §10)
- Achieved timestamp
- Optional measurable value

Assurance evidence remains authoritative for any verification claim an Outcome carries. Nexus Developer cannot self-declare Assurance PASS on an Outcome or any other object.

---

## 7. Release & Deployment — architecture only, SP2

No Release/Deployment/Environment/Artifact implementation exists, and none is authorized by this document. WU-02 freezes only:

- **Terminology**: Release → Deployment → DeploymentStep; pipeline shape BUILD ONCE → VERIFY → VERSIONED ARTIFACT → DEV → TEST → PROD.
- **Responsibility**: DELIVERY (07) remains responsible for the build/deploy mechanics; RELEASE & DEPLOYMENT as a Work Universe domain is the traceability wrapper connecting Outcomes to what shipped and where.
- **Traceability expectation**: a Release should be able to answer which Outcomes it includes and which Deployments delivered it; a Deployment should be traceable back to its Release and to the `PipelineRunResult` evidence that produced it.
- **Extension point**: `PipelineRunResult` (Nexus Developer Core) is the one existing partial building block — a future Release/Deployment aggregate consumes it as evidence, it is not itself promoted into that aggregate.

Full implementation remains SP2. A minimal nullable/reference placeholder may be added only if a concrete SP1 traceability need is demonstrated — none has been, as of this document.

---

## 8. Roadmap-ledger ↔ runtime identity bridge

Two identity systems remain, deliberately unmerged:

- **Roadmap ledger** — `F-`/`M-`/`WI-`/`T-`/`S-`/… string ids in `nexus-roadmap.yaml`, shared with Forge's `idSpaces.RoadmapNode` and its `Dependencies & Blockers` sheet. Tracks *building Nexus itself* (all layers, Forge, and each product's own build-out).
- **Runtime Core** — Guid-backed domain aggregates in Nexus Developer (`Feature`, `Milestone`, `Task`, etc.). Tracks *any product's ongoing work* at runtime, including, eventually, Nexus Developer's own.

No cross-reference field exists between them today. WU-02 does not blindly add a `roadmapLedgerId` field to every aggregate. The narrow bridge to evaluate at implementation time is a single optional reference value (naming direction: `RoadmapLedgerReference`, `SourceRoadmapNodeId`, or `ExternalPlanningReference` — final name decided at implementation, following existing naming conventions) placed only on the specific aggregate(s) that actually need it (most plausibly `Feature` and `Task`, where the build-out ledger and the runtime graph are most likely to describe the same unit of work during the bootstrap phase). Forge must be able to follow this bridge without requiring a live Nexus Developer runtime — it remains `BOOTSTRAP_SAFE`.

---

## 9. Forge integration

Forge consumes the Work Universe; it does not gain a new context engine. `DependencyLineage.ps1`, `ContextPackage.ps1`, `TaskClassification.ps1`, and `router/*` are extended, not replaced, consistent with the design already frozen in `LAYER_MODEL.md`. Future Forge context resolution should be able to resolve, where available: the roadmap/work item, the corresponding runtime work object, Product, Feature/Subfeature, Planning, Chat/turn evidence, `ActiveChange`/Preflight, `DevelopmentRun`, dependencies, repository reality, Assurance evidence, and Outcome. Forge remains `BOOTSTRAP_SAFE` — no mandatory running Nexus Developer dependency.

---

## 10. Assurance dependency — formal rule

Resolves the `REQUIRES DECISION` cell in `DEPENDENCY_RULES.md` §4 (Nexus Developer product → 08 ASSURANCE):

- Nexus Developer **may** consume 08 ASSURANCE through published contracts, services, verification results, and evidence interfaces.
- Nexus Developer **must not** self-authoritatively declare Assurance PASS, must not mutate Assurance evidence authority, and must not make Assurance depend on Developer.
- 08 ASSURANCE must not reference Nexus Developer.

This rule is now recorded as authoritative in `DEPENDENCY_RULES.md` itself (see that document for the resolved matrix cell and note) — it is not restated in full detail here beyond this summary, per the no-duplication rule in `DOCUMENTATION_INDEX.md` §1.

---

## 11. Nexus Developer integration

No UI work is authorized by this document. Navigationally, the Work Universe's new domains extend Nexus Developer's existing `Workspace` → `Project` → `Subproject` → `Feature` navigation one level up (Product above Feature, once GOVERNANCE's identity exists) and one lifecycle stage down (Outcome/Release below Development). Nexus Developer eventually presents graph projections/views — Products, Features, Planning, Chats, Development, Issues, Outcomes, Releases, Deployments — over existing domain truth. These are projections, not duplicate stores.

---

## 12. Non-goals

This document does not: introduce a numbered Platform layer; require a graph database; introduce a generic `WorkRelationship` engine; rename `DevelopmentRun` to "Development"; merge the roadmap-ledger and runtime-aggregate id spaces; implement Product, Outcome, Release, or Deployment; implement `DevelopmentControlAddress` or the two-workbook split (both remain approved SP1 milestones, executed separately); or authorize any source, workbook, or database change.

---

## Reference

| Subject | Owning document |
|---|---|
| The 10 numbered layers, dependency rules | `LAYER_MODEL.md`, `DEPENDENCY_RULES.md` |
| Product framework, product state model | `PRODUCT_ARCHITECTURE.md` |
| Data ownership, entity-to-layer mapping | `DATA_OWNERSHIP.md` |
| Nexus Developer entity model, dependency graph | `DEVELOPER_ARCHITECTURE.md` |
| Conversation engine, ScopeRef, context handoff | `EXPERIENCE_ARCHITECTURE.md` |
| Turn pipeline, context seam, provider abstraction | `AI_ARCHITECTURE.md` |
| Traceability model, verification, evidence, quality gates | `ASSURANCE_ARCHITECTURE.md` |
| Source to running system, environments, deployment | `DELIVERY_ARCHITECTURE.md` |
| Structured work — features, milestones, work items, tasks, subtasks | `nexus-roadmap.yaml` |
| State transitions, entry conditions | `DEVELOPMENT_WORKFLOW.md` |
