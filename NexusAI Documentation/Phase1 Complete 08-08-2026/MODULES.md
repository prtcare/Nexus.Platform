# MODULES.md

Detailed responsibilities for every project and major module in the solution. For the high-level layering rationale, see [ARCHITECTURE.md](./ARCHITECTURE.md).

## NexusAI.Domain

Pure business model, no external dependencies.

| Folder | Contents | Notes |
|---|---|---|
| `Common/` | `Entity<TId>`, `AggregateRoot<TId>`, `IRepository<TDomain,TId>` | Shared base types. `AggregateRoot` currently adds nothing beyond `Entity` — reserved for future domain-event support. |
| `Common/Identifiers/` | `WorkspaceId` | Only Workspace's ID lives in the shared `Identifiers` folder; every other entity's ID (`ProjectId`, `ConversationId`, etc.) is defined in its own entity folder instead. Worth normalizing later — see [DECISIONS.md](./DECISIONS.md). |
| `Workspace/` | `Workspace`, `WorkspaceStatus`, `IWorkspaceRepository` | Top of the memory hierarchy. `Workspace` is an `AggregateRoot`. |
| `Project/` | `Project`, `ProjectStatus`, `IProjectRepository` | Belongs to a `Workspace`. Plain class (not `AggregateRoot`). |
| `Conversation/` | `Conversation`, `ConversationStatus`, `IConversationRepository` | Belongs to a `Project`. |
| `ConversationMessage/` | `ConversationMessage`, `ConversationMessageRole`, `IConversationMessageRepository` | Individual chat turns. `AggregateRoot`. Immutable after creation (no update methods). |
| `WorkItem/` | `WorkItem`, `WorkItemStatus`, `WorkItemType`, `IWorkItemRepository` | Units of planned work, produced by the Planner. |
| `Knowledge/` | `Knowledge`, `KnowledgeSource`, `IKnowledgeRepository` | Long-lived facts scoped to a Workspace. Immutable after creation. |
| `Adr/` | `Adr`, `AdrStatus`, `IAdrRepository` | Architecture decision records, linked to a `Knowledge` entry. `Entity`, mutable status (Accept/Deprecate/Supersede). |
| `Artifact/` | `Artifact`, `ArtifactType`, `IArtifactRepository` | Output produced against a `WorkItem` (code, docs, etc.). Content is mutable. |
| `Branch/` | `Branch`, `BranchStatus`, `IBranchRepository` | A side-thread off a `Conversation`. Entity exists; **no orchestration logic yet** — see below. |
| `Snapshot/` | `Snapshot`, `SnapshotStatus`, `ISnapshotRepository` | A point-in-time record tied to a `Branch`. |

**Note on `Session`**: `Session`/`SessionId`/`SessionStatus`/`ISessionRepository` are physically located under `NexusAI.Application/Session/` but declared under the `NexusAI.Domain.Session` namespace — this is domain logic living in the wrong project physically. See the "Physical/Namespace Mismatches" entry in [DECISIONS.md](./DECISIONS.md).

## NexusAI.Application

Use cases and orchestration. Depends only on Domain.

**Per-entity CRUD** (one folder per aggregate: `Adr`, `Artifact`, `Branch`, `ConversationMessages`, `Conversations`, `Knowledge`, `Projects`, `Session`, `Snapshot`, `WorkItem`, `Workspaces`) — each generally follows a `{Verb}{Entity}Command` + `{Verb}{Entity}Handler` + `{Verb}{Entity}Result` pattern for commands, and `{Verb}{Entity}Query` + `Handler` + `Result` for reads. `ICommandHandler`/`IQueryHandler` (in `Abstractions/`) are marker interfaces; handlers are otherwise plain classes resolved directly from DI, not through a mediator.

**`Chat/`** — the memory-aware chat pipeline.
- `IChatService`/`ChatService` — entry point; loads context, delegates to `SendChatHandler`.
- `SendChatHandler` — the real work: loads conversation + project, persists the user message, loads full history, retrieves ranked `Knowledge`, builds the prompt, calls `ILLMProvider`, persists the response.
- `IConversationContextProvider`/`ConversationContextProvider` (impl in Infrastructure) — fetches prior messages.
- `Prompting/PromptBuilder` — composes the final `ChatRequest` from knowledge + user prompt (history is passed separately on the request).

**`Planning/`** — `IPlanner`/`Planner` turns an objective string into a list of `WorkItem`s. **Currently fully hardcoded**: every call returns the same four items (Analyze → Design Solution → Implement → Validate) regardless of the objective's actual content beyond string-interpolating it into the first item's title. Real LLM-driven planning is a Phase 2 item.

**`Execution/`** — `IExecutionEngine`/`ExecutionEngine` runs a plan by dispatching to an agent via `IAgentDispatcher`. See "Agent Framework" below for the important caveat about which agent actually runs.

**`Knowledge/Services/`** — the retrieval pipeline: `IKnowledgeContextProvider` (fetches all workspace knowledge) → `IKnowledgeRanker`/`KeywordKnowledgeRanker` (scores by literal term overlap) → `IKnowledgeRetrievalService` (orchestrates both, returns top N). Phase 2 replaces the ranker with embeddings-based similarity.

**`Providers/`** — the LLM abstraction: `ILLMProvider`, `ChatMessage`, `ChatRequest`, `ChatResponse`. Implemented today only by `OpenAIProvider` (Infrastructure). Phase 2 adds `AnthropicProvider` behind the same contract.

**`Agents/`** — ⚠️ **a second, separate agent abstraction**, distinct from `NexusAI.Core.Agents`. Contains `IAgent`, `AgentContext`, `AgentResult`, `AgentType` (enum: Developer/Reviewer/Architect/Research/Test), and `DummyAgent` — a placeholder implementation. This is the abstraction the Execution pipeline actually dispatches to today. See [DECISIONS.md](./DECISIONS.md) for the full explanation of why two abstractions exist and the plan to unify them.

## NexusAI.Core

The agent framework contract, isolated from Application/Infrastructure so agents could in principle run independently.

- `IAgent` — `Metadata` (id/name/description) + `RunAsync(AgentContext, ct)` returning `Task` (no result payload).
- `IAgentRuntime`/`AgentRuntime` — runs a given `IAgent` against a context. Note: `AgentRuntime`'s source file is physically here but declared under `namespace NexusAI.Infrastructure.Agents` — another physical/namespace mismatch, tracked in [DECISIONS.md](./DECISIONS.md).
- `IAgentRegistry` — intended to hold the set of available agents (`GetAgents()` returning `AgentMetadata`). **Currently has zero implementations anywhere in the codebase** — it's an unused, unregistered interface today.
- `AgentMetadata` — `Id`, `Name`, `Description`.
- `Abstractions/IClock` — a small testability seam for "now," implemented by `SystemClock` (Infrastructure).
- `Modules/INexusModule` — a `Register(IServiceCollection)` contract, implemented today only by `CoreModule` (Infrastructure).

## NexusAI.Agents

Concrete agent implementations.

- `DeveloperAgent` (`DeveloperAgent/DeveloperAgent.cs`) — implements `NexusAI.Core.Agents.IAgent`. Currently a stub: `RunAsync` writes a line to the console and returns. **Not connected to the Planning/Execution pipeline** — only invoked via a manual, one-off call at the end of `NexusAI.Host/Program.cs`.

Planned additions (Phase 2, see [ROADMAP.md](./ROADMAP.md)):
- A **Compiler agent** — Excel spec → validated Dataverse tables/solutions/relationships, with YAML export to GitHub for backup/restore.
- A **CNC Retrofit agent** — machine spec → LinuxCNC HAL/INI config, Mesa card pin mapping, advisory-only (no direct motion control).

## NexusAI.Infrastructure

Everything that talks to the outside world.

**`Dataverse/`** — the persistence layer, structured to look like real Dataverse access even though it's currently backed by an in-memory dictionary:
- `IDataverseContext`/`InMemoryDataverseContext` — generic Create/Retrieve/Update/RetrieveMultiple, keyed by `Guid`.
- `Clients/IDataverseClient`/`DataverseClient` — a thin wrapper, not yet backed by the real Dataverse SDK.
- `Common/DataverseRepositoryBase`, `Common/IRepositoryMapper` — shared base for turning a domain entity into a Dataverse-shaped entity and back.
- `Entities/*Entity.cs` — one per domain aggregate (`WorkspaceEntity`, `ProjectEntity`, etc.), all inheriting `DataverseEntity` (`Id: Guid`, `CreatedAt: DateTimeOffset`). No logical table-name metadata yet — needed before a real Dataverse client can be written (Phase 2, Milestone 1).
- `Mapping/*Mapper.cs` — one per aggregate, implementing `IDataverseMapper`/`IRepositoryMapper`.
- `Repositories/*DataverseRepository.cs` — one per aggregate, implementing the corresponding Domain repository interface.
- `Configuration/DataverseOptions` — `Url`, `TenantId`, `ClientId`, `ClientSecret`. No environment/in-memory toggle yet.

Also present but **dead code**: `Repositories/Workspace/InMemoryWorkspaceRepository.cs` — a second, unused `IWorkspaceRepository` implementation that competes conceptually with `WorkspaceDataverseRepository` (the one actually registered in DI). See [DECISIONS.md](./DECISIONS.md).

**`OpenAI/`** — `OpenAIProvider` (implements `ILLMProvider`, wraps the OpenAI SDK's `ChatClient`) and `OpenAIOptions` (`ApiKey`, `Model`, default `gpt-4.1`). This is real and functional — the only fully "live" piece of the platform today.

**`Registration/` and root `ServiceCollectionExtensions.cs`** — dependency injection wiring, split across three places that overlap: `ServiceCollectionExtensions.AddInfrastructure()` (the primary one — registers nearly everything, used by both Api and Host), `Registration/CoreModule.cs` (registers `IClock` + `IAgentRuntime`), and `Registration/ModuleExtensions.cs` (calls `CoreModule`, plus contains unreachable dead code after an early `return`). See [DECISIONS.md](./DECISIONS.md) for the consolidation plan (Phase 2, Milestone 0).

**`Services/`** — `AgentDispatcher` (implements `IAgentDispatcher`, resolves an `Application.Agents.IAgent` by `AgentType`), `ConversationContextProvider` (implements `IConversationContextProvider`), `SystemClock` (implements `IClock`).

## NexusAI.Api

ASP.NET Core Web API, minimal-API style (not MVC, aside from one leftover scaffolded `WeatherForecastController`/`WeatherForecast.cs` that should be deleted).

- `Endpoints/Chat/`, `Endpoints/Conversations/`, `Endpoints/Projects/`, `Endpoints/WorkItems/`, `Endpoints/Knowledge/` — each a static class with a `Map{X}Endpoints(IEndpointRouteBuilder)` extension method, called from `Program.cs`. See [API.md](./API.md) for the full route list.
- `Program.cs` — calls only `AddInfrastructure()`, **not** `AddNexusAI()`/`AddInfrastructureModules()`. This means `IAgentRuntime` and the `CoreModule` registrations are **not available** in the Api's DI container today. Not currently a problem (no Api endpoint touches agents yet), but worth knowing before adding one.
- Swagger/OpenAPI is configured and live at `/swagger`.

There are currently **no endpoints for Workspaces, Planning, Execution, Branches, or Agents** — only Chat, Conversations, Projects, WorkItems, and Knowledge are exposed. This is a known gap for front-end integration, tracked in [ROADMAP.md](./ROADMAP.md) (Milestone 3).

## NexusAI.Host

A console entry point (`Host.CreateApplicationBuilder`) currently used as an end-to-end integration/smoke test rather than a production host. `Program.cs` sequentially: creates a workspace → project → conversation → work item → session → knowledge entry → branch → artifact → ADR → snapshot (verifying each via its repository), runs the planner and the execution pipeline, runs a two-turn "does the AI remember my name" chat memory test against live OpenAI, and finally manually invokes `IAgentRuntime.RunAsync` against `DeveloperAgent` directly (bypassing the Execution pipeline entirely). Ends with `app.Run()`, which blocks as a generic host with no background services — harmless, but not meaningful for a script that's already finished its work.

## NexusAI.Foundation

Reserved for shared, low-level cross-cutting utilities (e.g., result types, guard clauses) that don't belong in Domain. Currently contains only the `.csproj` — no code yet.

## tests/ and tools/

Both directories exist at the solution root and are referenced in the `.slnx` (`/Tests/` folder), but contain no files yet. `tools/` is the intended home for the Phase 2 Dataverse schema-deployer utility (see [ROADMAP.md](./ROADMAP.md), Milestone 1).
