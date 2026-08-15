# MILESTONES.md

A trackable, task-level checklist of every phase and milestone in NexusAI's development. Where [ROADMAP.md](./ROADMAP.md) explains *why* the work is sequenced this way, this document exists to track *whether it's actually done* — check items off as they're completed, and update the Progress Summary below as milestones close.

## Progress Summary

| Phase | Status |
|---|---|
| Phase 1 — Foundation | ✅ Complete |
| Phase 2, Milestone 0 — Core Foundation Rework | ⬜ Not started |
| Phase 2, Milestone 1 — Real Dataverse Backend | ⬜ Not started |
| Phase 2, Milestone 2 — Multi-Provider + Persistent Memory | ⬜ Not started |
| Phase 2, Milestone 3 — API + Front-End Contract | ⬜ Not started |
| Phase 2, Milestone 4 — Agent: Developer / VS Sub-Agent | ⬜ Not started |
| Phase 2, Milestone 5 — Agent: Compiler (Excel → Dataverse) | ⬜ Not started |
| Phase 2, Milestone 6 — Agent: CNC Retrofit | ⬜ Not started |
| Phase 3 — Business Integration | ⬜ Not scoped in detail |

---

## Phase 1 — Foundation ✅ Complete

- [x] Clean Architecture solution structure (Domain / Application / Infrastructure / Core / Agents / Api / Host / Foundation)
- [x] Full domain model: Workspace, Project, Conversation, ConversationMessage, WorkItem, Knowledge, Adr, Artifact, Branch, Snapshot, Session
- [x] Strongly-typed IDs and status enums for every entity
- [x] Command/Handler/Result pattern implemented across nearly all entities
- [x] Working chat pipeline (`ChatService`/`SendChatHandler`) with message persistence and history
- [x] Real OpenAI integration (`OpenAIProvider` implementing `ILLMProvider`)
- [x] Keyword-based knowledge retrieval (`KeywordKnowledgeRanker`)
- [x] Planner stub (`Planner`/`IPlanner`) — fixed 4-item output
- [x] Execution pipeline stub (`ExecutionEngine`/`IAgentDispatcher`)
- [x] First agent implementation (`DeveloperAgent`, stub behavior)
- [x] In-memory Dataverse-shaped persistence layer, full repository/mapper pattern
- [x] REST API: Chat, Conversations, Projects, WorkItems, Knowledge endpoints + Swagger
- [x] `NexusAI.Host` end-to-end smoke test
- [x] Security fix: rotated leaked OpenAI API key, moved to User Secrets
- [x] Full documentation baseline (13 docs: README, VISION, ARCHITECTURE, MODULES, DATABASE, API, CONVENTIONS, CODING-STANDARDS, DECISIONS, ROADMAP, CHANGELOG, CONTRIBUTING, AI_CONTEXT)

**Known gaps left behind** (tracked individually in [DECISIONS.md](./DECISIONS.md)): dual/incompatible agent abstractions, unimplemented `IAgentRegistry`, several DI duplications, a Dataverse query interface incompatible with a real backend, and an API contract with unwrapped-ID serialization inconsistencies.

---

## Phase 2 — Real Platform

### Milestone 0 — Core Foundation Rework
- [ ] Unify `NexusAI.Core.Agents.IAgent` and `NexusAI.Application.Agents.IAgent` into one real contract
- [ ] Implement a real `IAgentRegistry` (currently zero implementations)
- [ ] Rework `IAgentDispatcher`/`ExecutionEngine` to route by agent type/intent, not hardcode a single agent
- [ ] Define a generic agent tool-calling contract (shared interface for file ops, API calls, etc. that any agent can use)
- [ ] Rework `PromptBuilder`/`ConversationContextProvider` to compose milestone window + knowledge + branch conclusions + recent messages within a token budget
- [ ] Implement real branch orchestration: detect tangent → spin up branch → run to resolution → summarize → fold into parent
- [ ] Consolidate the three overlapping DI registration paths (`AddApplication`, `AddInfrastructure`, `AddInfrastructureModules`/`CoreModule`) into one
- [ ] Delete dead code: `InMemoryWorkspaceRepository.cs`, unreachable block in `ModuleExtensions.cs`
- [ ] Fix namespace/physical-location mismatches (`AgentRuntime.cs`, `Session.cs`, `IRepository<TDomain,TId>`)
- [ ] Rename `WorkItemMapper.cs` namespace to match siblings
- **Exit criteria**: a conversation can branch, resolve, and fold a summary back into the main thread; any future agent registers itself without touching dispatcher code.

### Milestone 1 — Real Dataverse Backend for NexusAI
- [ ] Replace `IDataverseContext.RetrieveMultipleAsync`'s `Func<TEntity,bool>` with an attribute/value filter
- [ ] Add logical table-name metadata to every `*Entity` class
- [ ] Finalize and review the full 11-table (+ `ProjectMilestone`) schema in [DATABASE.md](./DATABASE.md)
- [ ] Package the schema as an importable Dataverse solution
- [ ] Build the schema-deployer console tool in `tools/` (reusable later by the Compiler agent)
- [ ] Implement a real `IDataverseClient`/`IDataverseContext` using the official SDK
- [ ] Wire up app-registration client-credentials authentication (Azure App Registration + Application User in Dataverse)
- [ ] Add an environment/in-memory toggle to `DataverseOptions`
- [ ] Map status enums to Dataverse Choice columns instead of raw `int`
- **Exit criteria**: `NexusAI.Host`'s smoke test runs end-to-end against a real Dataverse environment; every entity round-trips correctly.

### Milestone 2 — Multi-Provider + Persistent Memory
- [ ] Implement `AnthropicProvider` behind `ILLMProvider`
- [ ] Add config-driven provider selection (per-conversation or global)
- [ ] Add `ProjectMilestone` entity end-to-end (Domain + Dataverse entity + mapper + repository)
- [ ] Build the approval-gated update flow for `ProjectMilestone` (never auto-updates)
- [ ] Replace `KeywordKnowledgeRanker` with an embeddings-based ranker
- [ ] Build automatic knowledge extraction from conversations (distinct from the milestone window)
- **Exit criteria**: switching provider mid-project doesn't lose memory; a milestone persists unchanged until explicitly approved; old conversations are retrievable as relevant context, not full replay.

### Milestone 3 — API + Front-End Contract
- [ ] Fix wrapped-ID serialization inconsistency (`SendChatRequest`, `CreateKnowledgeResponse`, raw `Results.Ok(result)` leaks)
- [ ] Add `POST /api/workspaces` (currently missing entirely)
- [ ] Add endpoints for Branches, Sessions, Artifacts, ADRs, Snapshots
- [ ] Add endpoints for Planning and Execution (trigger a plan, run execution)
- [ ] Add endpoints for Agents (list available agents, invoke one)
- [ ] Add an auth/session model shared by both front ends
- [ ] Remove leftover `WeatherForecastController` template cruft
- **Exit criteria**: both the Power Apps app and the Visual Studio desktop app can create/list workspaces, projects, conversations, and send chats against the live API. See [FRONTEND-DESIGN.md](./FRONTEND-DESIGN.md) for what these clients need from the API.

### Milestone 4 — Agent: Developer / VS Sub-Agent
- [ ] Build file read/write tools on the Milestone 0 tool contract
- [ ] Build `dotnet build`/`dotnet test` execution tools
- [ ] Build diff-reporting output for proposed changes
- [ ] Wire `DeveloperAgent` into the real, unified agent pipeline (not the current disconnected manual call)
- **Exit criteria**: given a work item, the agent reads a file, proposes a change via a tool call, and reports the result.

### Milestone 5 — Agent: Compiler (Excel → Dataverse)
- [ ] Design the Excel spec template/schema
- [ ] Build Excel ingestion + validation service
- [ ] Build table/relationship generation service (reusing the Milestone 1 client + schema-deployer pattern)
- [ ] Build YAML export
- [ ] Build GitHub backup commit flow
- [ ] Build restore-from-YAML flow
- **Exit criteria**: a defined Excel sheet produces validated, correctly-related Dataverse tables, with a YAML snapshot committed to GitHub, and can be restored from that snapshot.

### Milestone 6 — Agent: CNC Retrofit
- [ ] Design the machine spec template (axis config, motor specs, Mesa card pinout)
- [ ] Build spec ingestion + validation
- [ ] Build LinuxCNC HAL file generation
- [ ] Build LinuxCNC INI file generation
- [ ] Build Mesa card pin-mapping logic
- [ ] Build a wiring/safety checklist generator
- [ ] Confirm advisory-only scope — no direct motion control
- **Exit criteria**: given a machine spec, the agent produces valid HAL/INI files and a wiring/safety checklist.

---

## Phase 3 — Business Integration *(not yet scoped into milestones)*

- [ ] Business data connectors (SharePoint, SQL, other Dataverse environments) feeding `Knowledge`
- [ ] Data/reporting agent
- [ ] Automation agent (triggers Power Automate flows)
- [ ] Ops/PM agent (tracks work across the business)
- [ ] Power Apps and Power Automate as first-class integration targets
- [ ] Multi-tenant/multi-user access, if needed

This phase will get its own milestone breakdown once Phase 2 is far enough along to know what it actually needs.
