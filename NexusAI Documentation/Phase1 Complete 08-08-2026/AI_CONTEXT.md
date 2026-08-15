# AI_CONTEXT.md

**Read this before touching any code.** This document exists so an AI coding agent starting a new session — with no memory of prior conversations — can get oriented accurately and quickly, without re-deriving the architecture from scratch or (worse) confidently assuming something works the way it looks like it should.

## What NexusAI Is, In One Paragraph

NexusAI is a self-owned, provider-agnostic AI orchestration platform being built by a single developer to run their own business. It combines a structured, permanent memory model (not a flat chat transcript), a registry of specialized agents that do real work, and Microsoft Dataverse as the backend — chosen deliberately to integrate with the Power Platform tools (Power Apps, Power Automate) the business already runs on. Full detail: [VISION.md](./VISION.md).

## Document Map — What to Read For What

| Need to know... | Read |
|---|---|
| Why a decision was made | [DECISIONS.md](./DECISIONS.md) |
| What's currently broken/inconsistent | [DECISIONS.md](./DECISIONS.md) Known Issues log — **read this before assuming current behavior** |
| How the layers fit together | [ARCHITECTURE.md](./ARCHITECTURE.md) |
| What a specific project/folder is responsible for | [MODULES.md](./MODULES.md) |
| Entity fields, Dataverse schema | [DATABASE.md](./DATABASE.md) |
| API routes and request/response shapes | [API.md](./API.md) |
| Naming, ID, numbering, business-rule conventions | [CONVENTIONS.md](./CONVENTIONS.md) |
| C# style to match | [CODING-STANDARDS.md](./CODING-STANDARDS.md) |
| What's planned next, in what order | [ROADMAP.md](./ROADMAP.md) |
| Recipe for adding a new entity | [CONTRIBUTING.md](./CONTRIBUTING.md) |

## Current State (as of this writing)

**Phase 1 (Foundation) is complete. Phase 2 (Real Platform) has not started yet** — no Phase 2 milestone work has landed. If you're being asked to do Phase 2 work, check [ROADMAP.md](./ROADMAP.md) for which milestone, and confirm the milestones before it are actually done rather than assuming sequential completion.

What's real and working today:
- Full domain model (11 entities) with CRUD command/handlers.
- A genuinely functional chat pipeline with real OpenAI integration and basic memory (history + keyword-ranked knowledge).
- An in-memory persistence layer architected to mirror real Dataverse (repository/mapper pattern per entity), so swapping in a real client is intended to be low-risk.
- A REST API covering Chat, Conversations, Projects, WorkItems, Knowledge.
- `NexusAI.Host` as a working end-to-end smoke test.

What's **not** real yet, despite looking plausible in the code:
- Real Dataverse connectivity (still in-memory).
- A working multi-agent framework (see Critical Gotchas below — this is the single most important thing to understand before writing agent code).
- More than one LLM provider.
- Any front-end client (Power Apps, desktop).
- Real planning (the planner is a hardcoded 4-item stub, ignores the actual objective content beyond one string interpolation).

## Critical Gotchas — Things That Look Right But Aren't

These are the highest-value things to know before writing code. Full detail and file locations for all of these in [DECISIONS.md](./DECISIONS.md)'s Known Issues log (numbered #1–20) — cross-references below point to the relevant number.

1. **There are two different, incompatible `IAgent` interfaces** — `NexusAI.Core.Agents.IAgent` (implemented by `DeveloperAgent`) and `NexusAI.Application.Agents.IAgent` (implemented by `DummyAgent`). **The execution pipeline (`ExecutePlanHandler` → `ExecutionEngine` → `AgentDispatcher`) only knows about the second one.** This means: running a plan through the normal application flow always runs `DummyAgent`, never `DeveloperAgent`, no matter what the plan contains. `DeveloperAgent` is currently only reachable via one hardcoded manual call at the very bottom of `NexusAI.Host/Program.cs`, completely outside the planning/execution pipeline. **Do not assume `DeveloperAgent` runs when a plan executes — it doesn't, today.** *(Known Issue #1, resolved by Roadmap Milestone 0.)*
2. **`IAgentRegistry` is unimplemented and unused** — don't build against it assuming it does something; it currently has zero concrete implementations anywhere. *(Known Issue #2.)*
3. **`IDataverseContext.RetrieveMultipleAsync` takes a `Func<TEntity,bool>` predicate** — this only works because the current backend is an in-memory `Dictionary`. It **cannot** be used as-is against a real Dataverse client; a lambda can't be translated into a server-side query. If you're building the real Dataverse client (Milestone 1), this interface has to change first — see [DECISIONS.md](./DECISIONS.md) #9 for the exact replacement design (attribute/value filter — every real usage today is a simple equality filter, nothing more complex is needed).
4. **`SendChatCommand.History` is fetched but never used** — `ChatService` loads history via `IConversationContextProvider` and passes it into the command, but `SendChatHandler` ignores that parameter and re-fetches history itself. Not a correctness bug, just don't be surprised the parameter appears unused if you trace it. *(Known Issue #14.)*
5. **Several entity ID types leak into the API unwrapped**, serializing as `{"value": "guid"}` instead of a plain string — specifically `SendChatRequest.ConversationId`, `CreateKnowledgeResponse.KnowledgeId`, and anywhere a raw Application-layer `Result` record is returned directly via `Results.Ok(result)` instead of being mapped to a dedicated Response DTO (this happens for both Knowledge GET endpoints and the conversations-list endpoint). If you're integrating a client against the API, expect this inconsistency until Milestone 3 fixes it. *(Known Issue #15.)*
6. **There's no `POST /api/workspaces` endpoint** — a workspace can only be created by calling `CreateWorkspaceHandler` directly (as `Host` does), not through the API. *(Known Issue #16.)*
7. **Namespace ≠ physical project location, in three places**: `AgentRuntime.cs` (physically Core, namespaced Infrastructure.Agents), `Session.cs`+related (physically Application, namespaced Domain.Session), `IRepository<TDomain,TId>` (physically Domain, namespaced Infrastructure.Dataverse.Common). If you're searching for a type by its namespace, it might not be in the project you'd expect. *(Known Issue #3.)*
8. **`NexusAI.Api` doesn't call `AddNexusAI()`/`AddInfrastructureModules()`** — only `AddInfrastructure()`. `IAgentRuntime` and anything `CoreModule` registers are unavailable in the Api's DI container. Trying to inject `IAgentRuntime` into a new Api endpoint will throw at runtime unless this is fixed first. *(Known Issue #8.)*

## Patterns to Follow (condensed — full detail in CODING-STANDARDS.md / CONVENTIONS.md)

- File-scoped namespaces. Sealed classes for entities/handlers/services. Records for commands/results/DTOs. No public setters on domain entities — named behavior methods only.
- Strongly-typed IDs (`readonly record struct` wrapping `Guid`) everywhere in Domain/Application — but **unwrap to plain `Guid` at the Api boundary** (see Gotcha #5 above for what happens if you don't).
- New enums start at `1`. Never renumber an existing enum once real data could exist against it.
- Dataverse naming: `nexus_` prefix, `nexus_{table}id` primary keys, `nexus_{parenttable}id` lookup columns.
- One handler = one public `HandleAsync` method, injected directly — no mediator library.
- Before adding a DI registration, check both `AddApplication()` and `AddInfrastructure()` — there's already duplication (#6); don't add a third place.

## Explicit Do-Not List

- Do not build new agent functionality against `NexusAI.Application.Agents.IAgent` (the `DummyAgent` one) as if it's the long-term interface — it's the one Milestone 0 is expected to retire in favor of a unified contract. Check [ROADMAP.md](./ROADMAP.md) Milestone 0's status before choosing which interface to extend.
- Do not assume the planner (`Planner.CreatePlanAsync`) produces meaningful, objective-specific work items — it's a hardcoded 4-item stub today.
- Do not add real credentials (API keys, Dataverse client secrets) to any `appsettings.json` — use User Secrets. This already happened once (Known Issue #19); don't repeat it.
- Do not treat `ChangeStatus`/similar methods as validated — they currently accept any transition, including nonsensical ones. If a task depends on valid-transition enforcement, that logic doesn't exist yet and needs to be built, not assumed.
- Do not resolve `IAgentRuntime` from an Api endpoint without first fixing Gotcha #8, or it will throw.

## Where Things Are (quick reference)

- Agent framework abstractions (target design): `NexusAI.Core/Agents/`
- Agent framework abstractions (currently actually wired to execution): `NexusAI.Application/Agents/`
- Concrete agents: `NexusAI.Agents/`
- LLM provider abstraction: `NexusAI.Application/Providers/ILLMProvider.cs`; implementation: `NexusAI.Infrastructure/OpenAI/OpenAIProvider.cs`
- Chat/memory pipeline: `NexusAI.Application/Chat/`
- Planning: `NexusAI.Application/Planning/`
- Execution: `NexusAI.Application/Execution/`
- Knowledge retrieval: `NexusAI.Application/Knowledge/Services/`
- Dataverse persistence: `NexusAI.Infrastructure/Dataverse/`
- DI registration (primary): `NexusAI.Infrastructure/ServiceCollectionExtensions.cs`
- API endpoints: `NexusAI.Api/Endpoints/`
- End-to-end smoke test: `NexusAI.Host/Program.cs`

## What's Likely Next

Check [ROADMAP.md](./ROADMAP.md) for the authoritative current milestone, but as of this document's writing, **Milestone 0 (Core Foundation Rework)** is the next planned work — specifically unifying the dual agent abstraction (Gotcha #1) and fixing the Dataverse query interface (Gotcha #3) are the two highest-leverage first tasks, since real Dataverse work (Milestone 1) and every future agent (Milestones 4–6) depend on both being resolved first.

## Keeping This Document Accurate

If you're an AI agent making changes to this codebase: when you resolve a gotcha listed above, update this document and [DECISIONS.md](./DECISIONS.md) in the same piece of work — move the item to "resolved" rather than leaving both documents claiming it's still broken. If you discover a new inconsistency, add it to [DECISIONS.md](./DECISIONS.md)'s Known Issues log and, if it's significant enough to trip up the next agent, add it here too. This document is only useful if it stays true.
