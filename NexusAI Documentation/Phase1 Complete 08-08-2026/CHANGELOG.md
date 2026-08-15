# CHANGELOG.md

All notable changes to NexusAI are recorded here. Format loosely follows [Keep a Changelog](https://keepachangelog.com/); versioning is pre-1.0 (`0.x.y`) while the platform is still being established — expect breaking changes between minor versions during this period.

## [Unreleased] — Phase 2

Planned. See [ROADMAP.md](./ROADMAP.md) for the full Milestone 0–6 breakdown. Nothing in Phase 2 has landed yet as of this entry.

## [0.2.0] — 2026-08-08 — Documentation Baseline

### Added
- Full documentation set: `VISION.md`, `ARCHITECTURE.md`, `MODULES.md`, `DATABASE.md`, `API.md`, `CONVENTIONS.md`, `CODING-STANDARDS.md`, `DECISIONS.md`, `ROADMAP.md`, `CONTRIBUTING.md`, `AI_CONTEXT.md` — written from a full audit of the Phase 1 codebase, not just aspirational descriptions.
- `DECISIONS.md` Known Issues log: 20 specific, code-verified inconsistencies and gaps, each mapped to the roadmap milestone that resolves it.

### Notes
- This pass is documentation-only — no application code changed in this version.

## [0.1.0] — Phase 1 Foundation

### Added
- Solution structure across 8 projects: Domain, Application, Core, Agents, Infrastructure, Api, Host, Foundation.
- Full domain model: `Workspace`, `Project`, `Conversation`, `ConversationMessage`, `WorkItem`, `Knowledge`, `Adr`, `Artifact`, `Branch`, `Snapshot`, `Session` — each with a strongly-typed ID and status enum.
- Command/Handler/Result triplets for CRUD across nearly every entity.
- `ChatService`/`SendChatHandler` — a working chat pipeline with message persistence, history loading, and keyword-based knowledge retrieval, backed by a real OpenAI integration (`OpenAIProvider`).
- `Planner`/`IPlanner` — objective-to-work-items planning (currently a fixed 4-item stub; see [DECISIONS.md](./DECISIONS.md)).
- `ExecutionEngine`/`IAgentDispatcher` — plan execution pipeline (currently dispatches to a placeholder `DummyAgent`; see [DECISIONS.md](./DECISIONS.md)).
- `DeveloperAgent` — the first concrete agent implementation (currently a stub, not yet wired into the execution pipeline).
- In-memory Dataverse-shaped persistence layer (`InMemoryDataverseContext`) with a full repository/mapper pattern per entity, designed so a real Dataverse client can be dropped in later.
- REST API (`NexusAI.Api`) covering Chat, Conversations, Projects, WorkItems, and Knowledge, with Swagger UI.
- `NexusAI.Host` — an end-to-end smoke test exercising every handler and repository in sequence.

### Fixed
- Rotated and removed a live OpenAI API key that had been committed in plaintext in `appsettings.json` (both `NexusAI.Api` and `NexusAI.Host`); switched to User Secrets for local development. See [DECISIONS.md](./DECISIONS.md) Known Issue #19.

### Known Issues
See [DECISIONS.md](./DECISIONS.md) for the full, current list — 20 tracked items as of this release, spanning a dual/incompatible agent abstraction, several dependency-injection duplications, an API contract that isn't yet real-Dataverse-ready, and a handful of naming/namespace inconsistencies. None are blocking for continued Phase 1-style development, but several (particularly the agent abstraction split and the Dataverse query interface) are first-priority items for Phase 2.
