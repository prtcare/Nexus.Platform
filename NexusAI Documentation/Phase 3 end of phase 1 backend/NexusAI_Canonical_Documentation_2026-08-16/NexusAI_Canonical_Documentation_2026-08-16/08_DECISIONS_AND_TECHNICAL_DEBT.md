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

### ADR-010 — Responsive web frontend first

**Decision:** Start the public frontend as React + TypeScript against the API; use Power Apps for appropriate internal clients.  
**Reason:** Fast public-product development, responsive reuse, and broad component ecosystem.  
**Consequence:** Stabilize CORS, auth, OpenAPI, enum serialization, and errors before scaling the UI.

## Technical debt and open decisions

| Priority | Item | Required action |
|---|---|---|
| Critical | No clean build recorded for this handoff | Restore/build/test with .NET 10 on developer PC |
| Critical | Authentication/authorization absent | Choose identity/tenant model and implement before multi-user/public release |
| High | Project Milestone missing | Implement after first frontend slice |
| High | API contract inconsistencies | Standardize errors, lists, enums, nullable fields, versioning |
| High | Canonical host unclear | Decide between Api and Host and consolidate registration/configuration |
| High | Dataverse registry may differ from live mappings | Reconcile logical names and choice values before new schema work |
| High | Automated test projects not evident | Add domain, mapper, repository, API contract, and end-to-end tests |
| Medium | ADR and Memory lack public API | Define intended UI use and expose only required operations |
| Medium | Knowledge update/archive absent | Add governed lifecycle operations |
| Medium | No pagination/search/sorting | Add before data volume grows |
| Medium | Delete/archive policy unclear | Prefer lifecycle statuses and retention rules |
| Medium | Sample Weather Forecast code remains | Remove it |
| Medium | Repository handoff includes build/Git internals | Clean ignore/package process |
| Medium | Working tree in supplied ZIP was dirty | Resolve modified csproj and untracked libman intentionally |
| Future | Outcome/result learning not modeled | Design Result entity, evaluation, feedback, and link to agent/action |
| Future | Permissions tables planned but unimplemented | Complete WorkspaceMember/Team/AccessGrant model |

## ADR maintenance rule

When a decision changes, do not erase history. Add a new ADR entry that supersedes the former decision, record the reason and consequences, and update the technical-debt table if implementation remains pending.
