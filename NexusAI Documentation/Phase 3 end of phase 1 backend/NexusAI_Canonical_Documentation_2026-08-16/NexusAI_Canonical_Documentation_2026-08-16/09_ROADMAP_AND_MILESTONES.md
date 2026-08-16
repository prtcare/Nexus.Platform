# Roadmap and Milestones

## Progress summary

| Phase/Milestone | Status |
|---|---|
| Phase 1 — Foundation | Complete |
| Phase 2 M0 — Core foundation rework | Substantially complete |
| Phase 2 M1 — Real Dataverse backend | Substantially complete for current implemented entities |
| Phase 2 M2 — Persistent memory/intelligence | Partial |
| Phase 2 M3 — API contract and frontend foundation | Current |
| Phase 2 M4 — Developer/Visual Studio agent | Early foundation |
| Phase 2 M5 — Excel-to-Dataverse compiler | Planned/partially designed |
| Phase 2 M6 — CNC/machine automation agent | Planned research/prototype work |
| Phase 3 — Product and business integrations | Not fully scoped |

## Phase 1 — Foundation — Complete

Delivered:

- Clean Architecture solution structure.
- Core Domain/Application/Infrastructure/API boundaries.
- Strongly typed ID and repository patterns.
- Provider-neutral LLM abstraction and OpenAI implementation.
- Chat, planning, execution, and agent abstractions.
- Initial Workspace/Project/Conversation/Work/Knowledge concepts.
- Swagger/minimal API foundation.
- Initial architecture, standards, and vision documentation.

## Phase 2 M0 — Core foundation rework

Delivered or substantially present:

- .NET 10 project baseline.
- Command/query/handler organization.
- Dataverse client/context/repository/mapper pattern.
- Service registration.
- Defensive mapping fixes.
- Server-side filtering improvements.

Remaining:

- Choose canonical Api/Host entry point.
- Add test projects and CI quality gate.
- Remove sample code and resolve dirty working-tree items.

## Phase 2 M1 — Real Dataverse backend

Implemented features include Workspace, Project, Conversation, Conversation Message, Work Item, Knowledge, Branch, Snapshot, Session, Artifact, plus ADR/Memory infrastructure. Recent repository history records live-Dataverse fixes and completion of Session and Artifact vertical slices.

Remaining:

- Reconcile target naming registry with live schema.
- Complete ADR/Memory intended application/API lifecycle.
- Implement Project Milestone and Milestone Criterion.
- Add membership/team/access features when multi-user work begins.
- Add robust integration tests and release pipeline validation.

## Phase 2 M2 — Intelligence and durable context

Scope:

- Knowledge approval, status, source, and retrieval quality.
- Conversation summaries and links.
- Context selection by workspace/project/milestone/conversation.
- Persistent Memory separated from curated Knowledge.
- ADR capture and supersession.
- Results/outcomes, feedback, and evaluation.
- Multi-provider model routing.

Completion criteria: Nexus can explain which context it used, retain an approved decision, link advice to its actual result, and resume a project without manually pasting old conversations.

## Phase 2 M3 — API contract and frontend foundation — Current

### Gate A — Backend readiness

- Clean restore/build passes.
- First-slice endpoints pass Swagger tests against development Dataverse.
- CORS configured.
- Enum values documented/frozen.
- Common API errors and list shapes agreed.
- Canonical API entry point selected.

### Gate B — Frontend Slice 1

- Responsive app shell.
- Workspace list/create.
- Project list/create.
- Conversation list/create.
- Message history and Chat composer.
- Refresh proves persistence.

### Gate C — Milestones

- Implement Project Milestone and Criterion backend.
- Add active milestone and approval-aware editing in the Project UI.
- Link Conversations and Work Items to milestones.

### Gate D — Execution and context

- Work Items and Artifacts.
- Knowledge and decisions.
- Branches, Snapshots, Sessions.
- Authentication, authorization, and tenant boundaries before wider use.

## Phase 2 M4 — Developer agent

Goal: an agent that can inspect a checked-out solution, propose a bounded change, edit allowed files, run build/tests, and present evidence for approval.

Required safeguards:

- repository-scoped permissions;
- explicit plan and changed-file summary;
- no secret exposure;
- human approval for deployment/destructive operations;
- test/build evidence;
- recorded outcome linked to the task and artifact.

## Phase 2 M5 — Power Platform compiler

Goal: compile a governed workbook/specification into Dataverse/Power Platform solution components.

Scope includes solutions, tables, columns, relationships, indexes/keys, choices, roles, permissions, flows, workflows, business rules, deployment configuration, validation, YAML/spec output, and data seeding.

Deliver incrementally: schema validation → table/column generation → relationships/choices → security → automation → packaging/deployment → drift detection.

## Phase 2 M6 — Machine automation

Goal: connect Nexus planning and knowledge to controlled industrial automation, beginning with boring-machine retrofit research and measurement-assisted workflows.

Machine control is safety-critical. Use deterministic controllers/PLC/LinuxCNC for real-time motion and interlocks. AI may plan, diagnose, document, or propose parameters, but must not bypass hard limits, emergency stops, operator approval, or validated control logic.

## Phase 3 — Products and business integration

Build separate products and clients on Nexus:

- Nexus public chatbot/workspace product.
- Vault by Nexus.
- PRT internal operational clients and ERP modules.
- Developer, Power Platform, and Dataverse tools.
- Knowledge-capture and machine-assistance tools.

Scope one product vertical at a time and prove user value before broad platform expansion.

## Immediate next actions

1. Clean build and verify the first-slice API.
2. Decide canonical host and React/TypeScript frontend structure.
3. Implement Workspace screen against real API.
4. Continue Project, Conversation, Messages/Chat.
5. Implement Project Milestones before deep Project-page polishing.
