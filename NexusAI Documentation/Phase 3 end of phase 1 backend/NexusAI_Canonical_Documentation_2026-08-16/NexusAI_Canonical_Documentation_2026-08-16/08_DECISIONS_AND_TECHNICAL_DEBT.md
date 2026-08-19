# Decisions and Technical Debt

## Accepted architectural decisions

### ADR-001 — Clean Architecture

**Decision:** Separate Domain, Application, Infrastructure, API/Host, Core, and Agents.  
**Reason:** Protect business concepts from Dataverse, provider, and UI churn.  
**Consequence:** Every feature requires explicit boundary types and registrations.

### ADR-002 — Dataverse is the operational source of truth

**Decision:** Persist operational Nexus records in Microsoft Dataverse.  
**Reason:** Structured relationships, security, Power Platform integration, and enterprise administration.  
**Consequence:** Live schema names and choice values must be governed; analytics uses a separate downstream store.  
**Status:** Superseded by ADR-014 (Azure SQL replaces Dataverse for the Chat product) — see below.

### ADR-003 — Provider-neutral LLM abstraction

**Decision:** Application depends on `ILLMProvider`, not an OpenAI SDK.  
**Reason:** Permit model routing, testing, and future providers.  
**Consequence:** Provider-specific features need capability abstraction rather than leaking SDK types.

### ADR-004 — Structured memory hierarchy

**Decision:** Organize durable context as Workspace, Project, Milestone, Conversation, Knowledge, ADR, Work Item, Artifact, and Result rather than one flat transcript.  
**Reason:** Long-running work needs scope, trust, traceability, and continuation.  
**Consequence:** The UI must hide structural complexity while the backend maintains it.

### ADR-005 — Explicit approval for consequential milestone/decision changes

**Decision:** AI may suggest changes, but agreed milestone outcomes and accepted decisions change only with user approval.  
**Reason:** Prevent silent drift.  
**Consequence:** Approval state/history must be implemented in domain and UI.

### ADR-006 — Registry-based agents

**Decision:** Agents implement common contracts and are selected through a registry/dispatcher.  
**Reason:** Add specialization without hard-coded branching throughout Application.  
**Consequence:** Tool permissions, capability metadata, evaluation, and outcomes become part of agent identity.

### ADR-007 — Strongly typed IDs

**Decision:** Use aggregate-specific ID wrappers internally.  
**Reason:** Prevent accidental interchange of GUIDs.  
**Consequence:** Convert at HTTP/Dataverse boundaries and standardize JSON behavior if IDs are ever exposed.

### ADR-008 — Command/Handler/Result without mandatory mediator dependency

**Decision:** Use explicit handlers and dependency injection.  
**Reason:** Keep flow understandable and avoid framework dependence during foundation work.  
**Consequence:** Cross-cutting pipelines must be implemented deliberately if later needed.

### ADR-009 — Products remain separate from Nexus platform

**Decision:** Chatbot, Vault, ERP, and industrial clients are separate products consuming Nexus APIs.  
**Reason:** Independent UX, deployment, scaling, and product ownership.  
**Consequence:** Platform contracts and tenant/security boundaries must be stable.  
**Status:** Superseded by ADR-011 (Platform is a backbone, not a product container) — see below.

### ADR-010 — Responsive web frontend first

**Decision:** Start the public frontend as React + TypeScript against the API; use Power Apps for appropriate internal clients.  
**Reason:** Fast public-product development, responsive reuse, and broad component ecosystem.  
**Consequence:** Stabilize CORS, auth, OpenAPI, enum serialization, and errors before scaling the UI.

### ADR-011 — Platform is a backbone, not a product container

**Decision:** Nexus Platform holds no product entity and no product database. Workspace, Project, Conversation, Knowledge, and every other domain concept belong to the product that owns them, not to Platform.  
**Reason:** Each product's structure will differ, so a shared structure is wrong.  
**Consequence:** Product teams design and own their own domain model and persistence; Platform's Contracts describe only generic services (identity, model routing, tools, governance) and never gain a dependency on any product's schema.  
**Supersedes:** ADR-009.

### ADR-012 — Intelligence decides, Platform executes

**Decision:** Intelligence selects the model, agent, tools, and context to use; Platform performs the invocation, meters it, and audits it.  
**Reason:** Keeps the boundary between deciding what to do and executing it precise, so the two layers' responsibilities don't blur as both grow.  
**Consequence:** Intelligence never calls a vendor SDK directly, and Platform never learns what a Conversation is — each side depends only on the other's Contracts.

### ADR-013 — Three solutions, Platform shipped as NuGet libraries

**Decision:** Split into three solutions/repos — Nexus.AI (Platform), Nexus.Int (Intelligence), Nexus.Web (Chat product) — with Platform consumed by Intelligence as NuGet libraries rather than as a network service.  
**Reason:** Platform has exactly one consumer, Intelligence, by design, so a network hop buys nothing; running in-process inside the Intelligence host avoids that cost.  
**Consequence:** Platform's Contracts are still shaped as if they were HTTP, so Platform can be lifted into a standalone service later without changing callers.

### ADR-014 — Azure SQL replaces Dataverse for the Chat product

**Status:** Accepted. **Date:** 2026-08-18. **Applies to:** `Nexus.Web` — the Chat product only. Platform and Intelligence are unaffected.  
**Supersedes:** ADR-002 (Dataverse is the operational source of truth).

Merged verbatim from §1 of `ADR-014_AZURE_SQL_MIGRATION.md` (that document's §2–4 is the migration plan, verification checklist, and rollback procedure — not repeated here; see the file itself).

#### Context

ADR-002 chose Microsoft Dataverse as the operational store, reasoning: structured
relationships, built-in security, Power Platform integration, and enterprise administration.
Those reasons were sound for an internal Power Platform tool. They do not hold for a
multi-user chat product.

The V2.1 restructure (2026-08-17/18) confined all Dataverse code to
`Nexus.Products.Chat.Infrastructure`. The Domain does not know Dataverse exists, the
Application depends only on repository interfaces declared in the Domain, and Intelligence
and Platform are structurally forbidden from referencing it. That makes the store
replaceable at a cost that will never be lower than it is today.

#### Decision

The Chat product persists to **Azure SQL Database**. Dataverse is removed entirely from
`Nexus.Web`, including the `Microsoft.PowerPlatform.Dataverse.Client` package.

Access is via **EF Core**, with the Domain kept persistence-ignorant: no EF attributes, no
navigation-property pollution, no base classes. Mapping lives in
`IEntityTypeConfiguration<T>` classes inside Infrastructure, and strongly typed IDs are
handled by value converters.

#### Drivers

All four applied, which is why this is not a marginal call:

| Driver | Detail |
|---|---|
| **Cost and licensing** | Dataverse is per-user licensed. A chat product with many light users is close to the worst possible shape for that model. Azure SQL is priced on compute and storage, not seats. |
| **Query power and retrieval** | Knowledge and Memory need full-text and vector retrieval. Dataverse offers neither usefully. Azure SQL can hold operational data and vectors in one store. |
| **Latency and throughput** | Every chat turn reads history, knowledge, ADRs and work items before the model is invoked. That is several throttled Web API round-trips on the hot path. |
| **Independence from Power Platform** | Products must be free to choose their own store (V2.1 §1.3). Vault and ERP may need shapes Dataverse cannot serve. Staying would make Power Platform licensing a dependency of every future product. |

#### Consequences

**Positive**

- Removes per-seat licensing from the product's cost model
- Retrieval becomes a first-class capability rather than a workaround
- Removes several network hops from every chat turn
- Removes the `NU1903` high-severity vulnerability inherited transitively via the
  Dataverse client
- EF Core migrations give schema versioning that the Dataverse solution export never did
- Proves the V2.1 boundary claim in practice — see `ADR-014_AZURE_SQL_MIGRATION.md` §2, Stage 1

**Negative**

- Loses Dataverse's built-in row-level security. Authorization becomes the product's job,
  and lands on the same critical path as identity (D-1, still unimplemented) — see the
  Critical technical-debt item below.
- Loses Power Platform interop: no model-driven apps, flows or connectors over this data
  without building an API surface for them
- Loses the Dataverse audit trail; auditing becomes application-level
- Adds EF Core migrations and an Azure SQL instance to operate and pay for

**Neutral**

- No data migration. The current Dataverse contents are smoke-test records only
  (`PRJ-00000007`, `CON-00000003/4/5`) and are **confirmed disposable**. This is a schema
  rewrite, not a data migration — substantially cheaper.

#### What does not change

- The Domain model. Same 11 aggregates, same strongly typed IDs, same invariants.
- Repository interfaces (`IWorkspaceRepository` and siblings) stay in the Domain, unchanged.
- The Application layer. Not one handler should need editing.
- Every API contract and response shape.
- Platform and Intelligence. Neither has ever known what a Conversation is.

**If any of the above needs to change, that is an architecture leak, not a migration task.
Stop and report it.**

#### Open question to settle before Stage 5

Azure SQL's vector capabilities have moved quickly. Confirm current support, dimension
limits, index types and pricing against live Microsoft documentation before designing
Knowledge and Memory retrieval. Do not design from memory — including mine.

#### Fate of the Dataverse solution

`N_001_Nexus` in the `PRT (Dev)` environment is not deleted by this ADR. It stops being
read or written by `Nexus.Web`. Retire it as a separate, deliberate act once nothing
depends on it.

## Technical debt and open decisions

| Priority | Item | Required action |
|---|---|---|
| Critical | Authentication and authorization absent — `ChatTurnIdentity` returns a hardcoded tenant and a placeholder actor with fixed permissions | Implement real identity/tenant resolution before multi-user/public release; priority rises further once Azure SQL replaces Dataverse, because Dataverse's row-level security goes with it |
| High | API contract inconsistencies | Standardize errors, lists, enums, nullable fields, versioning |
| High | Dataverse registry may differ from live mappings | Reconcile logical names and choice values before new schema work |
| High | Automated test projects not evident | Add domain, mapper, repository, API contract, and end-to-end tests |
| High | Metering, quota and audit are in-memory placeholders (`InMemoryUsageMeter`, `PermissiveQuotaPolicy`, `ConsoleAuditLog`) | Replace with persistent implementations — cost is not enforceable and nothing survives a restart |
| High | Memory and turn traces are in-memory | Persist them — the Result Loop cannot exist until they do |
| High | `ProjectBrief`, `ProjectMilestone`, `MilestoneCriterion`, `Team`, `TeamMember`, `WorkspaceMember`, and `ProjectMember` exist as Dataverse tables but have no C# aggregate | Model these aggregates; the Objective context item sent to Intelligence currently carries only the project name, at Authoritative trust — this is the largest available answer-quality lever |
| Medium | ADR and Memory lack public API | Define intended UI use and expose only required operations |
| Medium | Knowledge update/archive absent | Add governed lifecycle operations |
| Medium | No pagination/search/sorting | Add before data volume grows |
| Medium | Delete/archive policy unclear | Prefer lifecycle statuses and retention rules |
| Medium | Repository handoff includes build/Git internals | Clean ignore/package process |
| Medium | Working tree in supplied ZIP was dirty | Resolve modified csproj and untracked libman intentionally |
| Medium | Provider SPI (`IModelCatalogSource`, `INamedModelGateway`) lives in `Nexus.Platform.Core` rather than `Nexus.Platform.Contracts` | Move the SPI to Contracts so a third-party provider author needs only the two interfaces, not the routing and metering implementations |
| Medium | Product API returns raw .NET stack traces on malformed request bodies | Add a problem-details contract before deployment — this is an information leak |
| Medium | `Nexus.Platform.Tools`, `.Identity`, and `.Persistence` are scaffolds; no tool executes | Implement real tool execution and the Identity/Persistence backends |
| Medium | Anthropic provider is a scaffold, so ADR-003's provider neutrality is structural but unproven | Implement the Anthropic provider to prove the neutrality claim with a second real vendor |
| Low | Nexus.Int has no git remote | Push it to a remote once it's ready to be shared/backed up |
| Future | Outcome/result learning not modeled | Design Result entity, evaluation, feedback, and link to agent/action |

## ADR maintenance rule

When a decision changes, do not erase history. Add a new ADR entry that supersedes the former decision, record the reason and consequences, and update the technical-debt table if implementation remains pending.
