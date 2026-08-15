# ROADMAP.md

## Phase 1 — Foundation (Complete)

The solution skeleton: Clean Architecture layering across Domain/Application/Infrastructure/Core/Agents/Api/Host, the full domain model (`Workspace`, `Project`, `Conversation`, `ConversationMessage`, `WorkItem`, `Knowledge`, `Adr`, `Artifact`, `Branch`, `Snapshot`, `Session`), CRUD command/query handlers for nearly every entity, a working OpenAI-backed chat loop with a real (if basic) memory pipeline, an in-memory persistence layer shaped to mirror real Dataverse, and a REST API surface covering Chat/Conversations/Projects/WorkItems/Knowledge. Runnable end-to-end via `NexusAI.Host`'s smoke test.

Phase 1 proved the architecture holds together. It did **not** produce: real Dataverse connectivity, a functioning multi-agent framework, more than one LLM provider, or any front-end client. See [DECISIONS.md](./DECISIONS.md) for the precise list of gaps and inconsistencies Phase 1 left behind — Phase 2 is largely about closing them before building on top.

## Phase 2 — Real Platform

Sequenced so nothing gets built twice: the core gets fixed and made real first (Milestones 0–3), then agents get built on top of a stable foundation (Milestones 4–6).

### Milestone 0 — Core Foundation Rework
*Do this first. Building agents on the current dual-agent-abstraction mess means redoing work later.*
- Unify the two incompatible `IAgent` interfaces (`NexusAI.Core.Agents` vs `NexusAI.Application.Agents`) into one real contract, with a real `IAgentRegistry` and dispatcher that actually routes to the intended agent — see [DECISIONS.md](./DECISIONS.md) Known Issue #1 for why this matters concretely.
- Define a generic agent tool-calling contract so every future agent (Compiler, CNC, Developer) plugs into the same pattern.
- Rework `PromptBuilder`/`ConversationContextProvider` to compose: milestone window + relevant knowledge + branch conclusions + recent messages, respecting a token budget — not raw history replay.
- Implement real branch orchestration: detect a tangent → spin up a branch conversation → run to resolution → summarize → fold the conclusion into the parent.
- Consolidate the three overlapping DI registration paths into one.
- **Exit criteria**: a conversation can branch, resolve, and return only a summary to the main thread; any future agent can register itself and expose tools without touching dispatcher code.

### Milestone 1 — Real Dataverse Backend for NexusAI
- Fix `IDataverseContext.RetrieveMultipleAsync`'s `Func<TEntity,bool>` signature — replace with an attribute/value filter a real query can be built from (see [DECISIONS.md](./DECISIONS.md) Known Issue #9 — this blocks everything else in this milestone).
- Add logical table-name metadata to every `*Entity` class.
- Full schema design for all 11 entities plus the planned `ProjectMilestone` — see [DATABASE.md](./DATABASE.md).
- Package the schema as a Dataverse solution (not manual portal clicking) for repeatable deployment across environments.
- Build a real `IDataverseClient`/`IDataverseContext` implementation using the official Dataverse SDK, authenticated via app registration (client credentials).
- Add an environment/in-memory toggle to `DataverseOptions` so local dev can still run without a live connection.
- **Exit criteria**: the `Host` smoke test runs end-to-end against a real Dataverse environment; every entity round-trips correctly.

### Milestone 2 — Multi-Provider + Persistent Memory
- `AnthropicProvider` implementing `ILLMProvider`; config-driven provider selection.
- `ProjectMilestone` entity (domain + Dataverse + mapper + repository) — approval-gated, only updates on explicit user confirmation.
- Replace `KeywordKnowledgeRanker` with an embeddings-based `IKnowledgeRanker`.
- Automatic knowledge extraction from conversations (distinct from the milestone window, which stays human-approved only).
- **Exit criteria**: switching provider mid-project doesn't lose memory; a project's milestone window persists unchanged until explicitly approved; old conversations are retrievable as relevant context, not full replay.

### Milestone 3 — API + Front-End Contract
- Fix the API serialization inconsistency where value-object IDs leak unwrapped into JSON (see [DECISIONS.md](./DECISIONS.md) Known Issue #15).
- Add the missing endpoints: Workspaces (no creation endpoint exists today), Agents, Branches, Milestones, Planning, Execution.
- Add an auth/session model so two front ends (Power Apps, desktop) can both call the same API safely.
- **Exit criteria**: the Power Apps app and the Visual Studio desktop app can both create/list workspaces, projects, conversations, and send chats against the live API.

### Milestone 4 — Agent: Developer / VS Sub-Agent
- First real agent built on the Milestone 0 tool contract: file read/write, `dotnet build`/`test` execution, diff reporting.
- Proves the tool-calling pattern end-to-end on the simplest case before the bigger agents.
- **Exit criteria**: given a work item, the agent reads a file, proposes a change via a tool call, and reports the result.

### Milestone 5 — Agent: Compiler (Excel → Dataverse)
Uses the same `IDataverseClient` built in Milestone 1, but targets new business-domain tables — completely independent of NexusAI's own operational tables.
- Excel ingestion + validation service.
- Table/relationship generation service (reusing Milestone 1's client and the schema-deployer tool pattern).
- YAML export + GitHub backup/restore.
- **Exit criteria**: a defined Excel sheet produces validated, correctly-related Dataverse tables, with a YAML snapshot committed to GitHub, and can be restored from that snapshot.

### Milestone 6 — Agent: CNC Retrofit
- Machine spec ingestion (same spec-sheet-driven pattern as the Compiler agent).
- LinuxCNC HAL/INI generation, Mesa card pin mapping, motor config validation.
- Advisory-only tools — outputs config files and a wiring/safety checklist for manual review and application; no direct motion control.
- **Exit criteria**: given a machine spec, the agent produces valid HAL/INI files and a wiring/safety checklist.

## Phase 3 — Business Integration (Not Yet Scoped in Detail)

- Business data connectors (SharePoint, SQL, other Dataverse environments) feeding the `Knowledge` layer.
- Additional business-function agents: data/reporting, an automation agent that triggers Power Automate flows, an ops/PM agent for tracking work across the business.
- Power Apps and Power Automate as first-class integration targets, not just "the client happens to be a Power App."
- Multi-tenant/multi-user access, if the platform grows beyond a single-developer tool.

Phase 3 will be broken into its own milestone plan once Phase 2 is far enough along to know what it actually needs — deliberately left loose here rather than over-planned this early.
