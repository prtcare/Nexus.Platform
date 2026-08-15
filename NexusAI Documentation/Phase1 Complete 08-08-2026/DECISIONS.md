# DECISIONS.md

Architecture Decision Record. Each entry captures a decision, why it was made, and what it costs. The second half of this document is a **Known Issues / Technical Debt Log** — this is the single source of truth for "what's actually broken or inconsistent right now," found by direct code audit. Other docs link here rather than repeating this list.

---

## ADR-001: Clean Architecture layering (Domain / Application / Infrastructure / Core+Agents / Api / Host)

**Status**: Accepted.
**Context**: NexusAI needs to swap its persistence backend (in-memory → real Dataverse) and its LLM provider (OpenAI → OpenAI+Claude) without rewriting business logic, and needs multiple front ends (Api, Host, eventually Power Apps and a desktop client) sharing the same core logic.
**Decision**: Strict inward-pointing dependencies — Domain has no dependencies; Application depends only on Domain; Infrastructure depends on Application+Domain; Api/Host depend on Infrastructure. The agent framework (Core+Agents) is kept as a parallel track so agents can, in principle, be tested independent of the rest of the stack.
**Consequences**: More files and indirection than a single-project app would need. In exchange, the backend swap planned for Phase 2 Milestone 1 should be close to a drop-in change, and a new front end only needs to call the Api, not reimplement logic.

## ADR-002: Microsoft Dataverse as the system of record

**Status**: Accepted (implementation pending — currently in-memory, see Known Issues).
**Context**: The business already runs on Power Apps, Power Automate, and Visual Studio. A separate SQL database would mean maintaining a second security/governance model and manually bridging data into Power Platform tools.
**Decision**: Dataverse is the backend, not an optional integration. The persistence layer is architected around Dataverse's shape (entities, logical names, choice columns) from the start, even before the real SDK client exists.
**Consequences**: Real Dataverse work requires an Azure app registration, a Dataverse environment, and ongoing awareness of Dataverse-specific concepts (logical names, choice columns, API request limits) that a generic ORM wouldn't require. In exchange, NexusAI's data is automatically visible to Power Automate flows and Power Apps built against the same environment.

## ADR-003: Provider-agnostic LLM abstraction (`ILLMProvider`)

**Status**: Accepted; `OpenAIProvider` is the only implementation so far.
**Context**: Relying on a single AI vendor risks rate limits, pricing changes, and context-window limits dictating the platform's capabilities.
**Decision**: All LLM calls go through `ILLMProvider` (`ChatAsync(ChatRequest) → ChatResponse`). Nothing in Application or Domain references the OpenAI SDK directly.
**Consequences**: Adding `AnthropicProvider` (Phase 2) should require zero changes outside `NexusAI.Infrastructure/` and a DI registration change/config toggle.

## ADR-004: Structured memory hierarchy instead of a flat chat transcript

**Status**: Accepted; hierarchy implemented, milestone window and branch orchestration planned.
**Context**: A single growing chat transcript eventually exceeds any model's context window, regardless of provider, and provides no way to durably "remember" something without replaying the whole conversation.
**Decision**: Memory is layered — `Workspace → Project → Conversation → ConversationMessage` for structure, `Knowledge` for retrievable long-term facts, a planned `ProjectMilestone` for human-approved durable summaries, and `Branch` for isolating side-questions from the main thread.
**Consequences**: Significantly more complex than "send the whole history every time," but the only design that lets a project's memory outlive any single model's context window or any single provider's session.

## ADR-005: Milestone content changes only on explicit user approval

**Status**: Accepted (not yet implemented — planned Phase 2, Milestone 2).
**Context**: An automatically-summarized "memory" risks the system confidently misremembering a decision that was never actually made.
**Decision**: `ProjectMilestone` content is written only through an explicit approval action by the user — never inferred or auto-updated, no matter how confident the extraction step is.
**Consequences**: Milestones will lag behind the conversation until the user actively curates them — a deliberate trade of freshness for trustworthiness.

## ADR-006: Registry-based multi-agent framework

**Status**: Accepted as target design; current implementation diverges from it (see Known Issues #1–2).
**Context**: NexusAI's roadmap includes at least three structurally different agents (a Developer/VS agent, a Dataverse-schema Compiler agent, a CNC machine retrofit agent), with more expected over time as the platform grows into a business-wide tool.
**Decision**: `IAgentRegistry` holds available agents by metadata; `IAgentDispatcher`/`IExecutionEngine` route work to the right one. Adding a new agent should mean implementing one interface and registering it — not modifying the dispatch pipeline.
**Consequences**: The target design isn't fully realized yet — see Known Issue #1. Unifying this is the first item in Phase 2, Milestone 0, specifically so the Compiler and CNC agents don't each have to work around the current inconsistency.

## ADR-007: Strongly-typed IDs over raw `Guid`

**Status**: Accepted.
**Decision**: Every entity ID is a distinct `readonly record struct` wrapping a `Guid` (e.g. `WorkspaceId`), never a bare `Guid` in method signatures.
**Consequences**: Compile-time protection against passing the wrong entity's ID into a method that expects a different one. Costs a small amount of boilerplate per entity and currently causes an API serialization inconsistency (Known Issue #15) where these types leak into request/response DTOs unwrapped.

## ADR-008: Command/Handler/Result pattern without a mediator library

**Status**: Accepted.
**Context**: A mediator library (e.g. MediatR) adds indirection (can't Ctrl+click from a call site to its handler) and a runtime dependency for a pattern that's straightforward to hand-roll for a single-developer project.
**Decision**: Handlers are plain classes with one public `HandleAsync` method, injected directly wherever needed (an endpoint delegate, another handler, `Host`).
**Consequences**: Slightly more DI registration boilerplate (each handler registered individually) in exchange for direct, traceable call chains.

---

## Known Issues / Technical Debt Log

Found by direct code audit. Each entry lists the issue, where it lives, its actual impact, and where it's tracked for resolution.

| # | Issue | Location | Impact | Resolution |
|---|---|---|---|---|
| 1 | **Two separate, incompatible `IAgent` abstractions exist.** `NexusAI.Core.Agents.IAgent` (Metadata + `RunAsync`→`Task`) is implemented by `DeveloperAgent`. `NexusAI.Application.Agents.IAgent` (Type + `ExecuteAsync`→`Task<AgentResult>`) is implemented only by `DummyAgent`. The Execution pipeline (`ExecutionEngine`→`AgentDispatcher`) only knows about the latter. | `NexusAI.Core/Agents/`, `NexusAI.Application/Agents/`, `NexusAI.Infrastructure/Services/AgentDispatcher.cs` | **`DeveloperAgent` is never invoked by the planning/execution pipeline.** Running a plan through `ExecutePlanHandler` always runs `DummyAgent`, regardless of plan content. `DeveloperAgent` is only reachable via a manual, hardcoded call at the bottom of `NexusAI.Host/Program.cs`. | Roadmap Milestone 0 |
| 2 | `IAgentRegistry` has **zero implementations** anywhere in the codebase and is not registered in DI. | `NexusAI.Core/Agents/IAgentRegistry.cs` | Dead interface; nothing can currently look up "what agents are available." | Roadmap Milestone 0 |
| 3 | **Physical/namespace mismatches** (file lives in one project, namespace says another): `AgentRuntime.cs` (in `NexusAI.Core`, namespaced `NexusAI.Infrastructure.Agents`); `Session.cs`/`SessionId.cs`/`SessionStatus.cs` (in `NexusAI.Application`, namespaced `NexusAI.Domain.Session`); `IRepository<TDomain,TId>` (in `NexusAI.Domain`, namespaced `NexusAI.Infrastructure.Dataverse.Common`). | See locations above | Confusing for navigation and for any static analysis tooling; not a build error today only by chance of how `using` directives happen to line up. | Cleanup pass; see CODING-STANDARDS.md rule |
| 4 | `WorkItemMapper.cs` is namespaced `NexusAI.Infrastructure.Dataverse.Mappers` (plural) — every sibling mapper uses `.Mapping` (singular). Only compiles because `ServiceCollectionExtensions.cs` has a matching stray `using ...Mappers;`. | `NexusAI.Infrastructure/Dataverse/Mapping/WorkItemMapper.cs` | Cosmetic/consistency only — no functional break. | Rename namespace to `.Mapping` |
| 5 | `ModuleExtensions.AddInfrastructureModules()` has an early `return services;` followed by unreachable dead code (duplicate `ConversationMessage` mapper/repository registration). | `NexusAI.Infrastructure/Registration/ModuleExtensions.cs` | None functionally (dead code never executes) — but misleading to read. | Delete the unreachable block |
| 6 | Duplicate DI registrations: `CreateProjectHandler`, `CreateConversationHandler`, `CreateKnowledgeHandler` registered in both `AddApplication()` and `AddInfrastructure()`; `CreateWorkItemHandler` registered **twice** within `AddInfrastructure()` alone; `IClock` registered in both `CoreModule` and `AddInfrastructure()`. | `NexusAI.Application/DependencyInjection/`, `NexusAI.Infrastructure/ServiceCollectionExtensions.cs`, `NexusAI.Infrastructure/Registration/CoreModule.cs` | Harmless today (same concrete type, `AddScoped`/`AddSingleton` — last registration simply wins) but signals the three registration entry points aren't coordinated. | Roadmap Milestone 0 — consolidate to one path |
| 7 | `InMemoryWorkspaceRepository.cs` is a second, unused `IWorkspaceRepository` implementation — dead code, never registered in DI (`WorkspaceDataverseRepository` is the one actually used). | `NexusAI.Infrastructure/Dataverse/Repositories/Workspace/InMemoryWorkspaceRepository.cs` | None functionally; confusing to encounter while navigating repositories. | Delete |
| 8 | `NexusAI.Api/Program.cs` calls `AddInfrastructure()` but never `AddNexusAI()`/`AddInfrastructureModules()` — so `IAgentRuntime` (and anything else `CoreModule` registers) is **not available** in the Api's DI container. | `NexusAI.Api/Program.cs` | No current impact (no Api endpoint resolves `IAgentRuntime` yet) but would throw a DI resolution error the moment one does. | Roadmap Milestone 0/3 |
| 9 | `IDataverseContext.RetrieveMultipleAsync<TEntity>(Func<TEntity,bool> predicate, ...)` only works against an in-memory collection — a real Dataverse client can't execute an arbitrary C# lambda; it needs a query built from column/value pairs before any request is sent. Every current real usage (`ProjectDataverseRepository`, `WorkItemDataverseRepository`, `ConversationMessageDataverseRepository`) is a simple single-column equality filter. | `NexusAI.Infrastructure/Dataverse/IDataverseContext.cs` | **Blocks real Dataverse connectivity entirely** until fixed — this is the single most important item for Phase 2. | Roadmap Milestone 1 (first task) |
| 10 | `DataverseEntity`/`*Entity.cs` classes carry no logical Dataverse table-name metadata. | `NexusAI.Infrastructure/Dataverse/Entities/` | A real client wouldn't know which table to address. | Roadmap Milestone 1 |
| 11 | `DataverseOptions` has no environment/in-memory toggle. | `NexusAI.Infrastructure/Dataverse/Configuration/DataverseOptions.cs` | Can't switch backends via configuration; would require a code change. | Roadmap Milestone 1 |
| 12 | Status enums are stored as raw `int` on `*Entity` classes rather than mapped to Dataverse Choice/OptionSet columns. | `NexusAI.Infrastructure/Dataverse/Entities/*.cs` | Fine for in-memory; loses Dataverse's built-in option labels/validation once real. | Roadmap Milestone 1 |
| 13 | Status enum numbering is inconsistent — some start at `0`, some at `1` (see CONVENTIONS.md). | `NexusAI.Domain/*/*.cs` | No functional bug today; a latent footgun if `0` is ever treated as "uninitialized." | Documented convention going forward; **do not renumber existing enums** once real data exists — that's a breaking migration, not a quick fix |
| 14 | `SendChatCommand.History` is populated by `ChatService` (via `IConversationContextProvider`, itself a repository call) but silently ignored by `SendChatHandler`, which independently re-fetches history from `IConversationMessageRepository`. | `NexusAI.Application/Chat/ChatService.cs`, `SendChatHandler.cs` | One redundant repository round trip per chat message — not a correctness bug, just wasted work. | Low priority cleanup |
| 15 | API serialization inconsistency: `SendChatRequest.ConversationId`, `CreateKnowledgeResponse.KnowledgeId`, and the raw `GetKnowledgeResult`/`ListKnowledgeResult`/`ListConversationResult` (returned directly via `Results.Ok(result)` instead of a mapped Response DTO) all expose wrapped value-object ID types, serializing as `{"value": "guid"}` instead of a plain GUID string — unlike every other endpoint. | `NexusAI.Api/Endpoints/Chat/`, `Endpoints/Knowledge/`, `Endpoints/Conversations/ConversationEndpoint.cs` (list endpoint) | Real integration gotcha for any client (Power Apps, desktop app) — inconsistent contract shape across the same API. | Roadmap Milestone 3 — fix before front-end integration |
| 16 | No `POST /api/workspaces` endpoint exists at all. | `NexusAI.Api/Endpoints/` | A front-end client cannot create a workspace today — only `Host` can, by calling the handler directly. | Roadmap Milestone 3 |
| 17 | Leftover scaffolded `WeatherForecastController`/`WeatherForecast.cs` template cruft. | `NexusAI.Api/Controllers/`, `NexusAI.Api/WeatherForecast.cs` | None — just noise. | Delete whenever convenient |
| 18 | No transition validation on status-change methods (e.g. `WorkItem.ChangeStatus` accepts any value, including nonsensical transitions like `Cancelled → New`). | `NexusAI.Domain/*/*.cs` (`ChangeStatus`/`Accept`/`Merge` etc.) | No current impact (nothing exercises invalid transitions yet) but will matter once multiple agents/users can change status concurrently. | Unscheduled — revisit when business rules matter more |
| 19 | **Historical**: a live OpenAI API key was committed in plaintext in `appsettings.json` (both `NexusAI.Api` and `NexusAI.Host`). | `appsettings.json` (historical) | Should already be rotated — flagged the moment it was found. Recorded here so the same mistake isn't repeated with the Dataverse `ClientSecret`. | Resolved by rotation + User Secrets; recorded for history |
| 20 | `NexusAI.Host/Program.cs` ends with `app.Run()` after the smoke-test script finishes, blocking as a generic host with no background services registered. | `NexusAI.Host/Program.cs` | Harmless (just sits idle) but doesn't match the file's actual purpose as a one-shot smoke test. | Low priority — consider a plain console app instead of a generic `Host` |

This log should be updated whenever an item is resolved (move it to a "Resolved" section with the date) or a new inconsistency is found during Phase 2 work — it's meant to stay accurate, not aspirational.
