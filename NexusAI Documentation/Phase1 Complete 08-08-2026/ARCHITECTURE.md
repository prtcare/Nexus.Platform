# ARCHITECTURE.md

## Overview

NexusAI follows Clean Architecture / DDD-style layering, targeting **.NET 10**. Dependencies point inward: outer layers (Api, Host, Infrastructure) depend on inner layers (Application, Domain), never the reverse. This is what lets the persistence backend (in-memory today, real Dataverse in Phase 2) or the LLM provider (OpenAI today, OpenAI + Claude in Phase 2) be swapped without touching business logic.

```
                    ┌─────────────┐  ┌─────────────┐
                    │  NexusAI.Api │  │ NexusAI.Host │   ← entry points
                    └──────┬──────┘  └──────┬──────┘
                           │                 │
                    ┌──────▼─────────────────▼──────┐
                    │      NexusAI.Infrastructure     │   ← Dataverse, OpenAI,
                    │  (Dataverse persistence, LLM     │      DI wiring
                    │   providers, DI registration)    │
                    └──────────────┬───────────────────┘
                                   │
                    ┌──────────────▼───────────────────┐
                    │       NexusAI.Application          │   ← commands, handlers,
                    │  (use cases, orchestration,        │      services
                    │   Chat/Planning/Execution/         │
                    │   Knowledge services)              │
                    └──────────────┬───────────────────┘
                                   │
                    ┌──────────────▼───────────────────┐
                    │         NexusAI.Domain             │   ← entities, value
                    │  (entities, IDs, status enums,     │      objects, repo
                    │   repository interfaces)           │      interfaces
                    └────────────────────────────────────┘

        ┌────────────────────┐        ┌─────────────────────┐
        │    NexusAI.Core     │◄───────┤   NexusAI.Agents      │  ← agent framework
        │ (IAgent, IAgentRuntime,│      │ (concrete agent        │     + implementations
        │  IAgentRegistry)     │        │  implementations)      │
        └────────────────────┘        └─────────────────────┘
```

`NexusAI.Core` + `NexusAI.Agents` form a parallel "Platform" track (grouped together under the `/Platform/` folder in the `.slnx`) that the Application/Infrastructure layers integrate with for agent execution.

## Layer Responsibilities

- **Domain** — pure business model. Entities (`Workspace`, `Project`, `Conversation`, `ConversationMessage`, `WorkItem`, `Knowledge`, `Adr`, `Artifact`, `Branch`, `Snapshot`, `Session`), strongly-typed IDs (`readonly record struct` wrapping a `Guid`), status enums, and repository interfaces (`IWorkspaceRepository`, etc.). No dependency on any other layer.
- **Application** — use cases. One folder per aggregate, each generally following a `Commands/{Verb}{Entity}Command` + `{Verb}{Entity}Handler` + `{Verb}{Entity}Result` triplet. Also owns the higher-level orchestration services: `ChatService` (chat + memory), `Planner` (objective → work items), `ExecutionEngine` (runs a plan), and the `Knowledge` retrieval pipeline.
- **Core** — the agent framework contract: `IAgent`, `IAgentRuntime`, `IAgentRegistry`, `AgentMetadata`. Deliberately has no dependency on Application or Infrastructure, so agents can, in principle, be hosted and tested independently of the rest of the stack.
- **Agents** — concrete `IAgent` implementations. Currently one: `DeveloperAgent`. See [MODULES.md](./MODULES.md) and [ROADMAP.md](./ROADMAP.md) for the two more planned (Compiler, CNC Retrofit).
- **Infrastructure** — everything that talks to the outside world: the Dataverse-shaped persistence layer (currently in-memory, see [DATABASE.md](./DATABASE.md)), the OpenAI provider implementing `ILLMProvider`, and all dependency injection wiring.
- **Api** — ASP.NET Core Web API exposing REST endpoints (minimal-API style) for front ends to call. See [API.md](./API.md).
- **Host** — a console entry point currently used as an end-to-end integration/smoke test, exercising every handler and repository in sequence.

## Core Architectural Decisions

**Why Dataverse as the backend.** Dataverse was chosen deliberately over a conventional SQL database because it plugs directly into the Power Platform ecosystem the business already runs on: security roles, Power Automate triggers, Power Apps data sources, and built-in auditing/versioning — without standing up and maintaining separate infrastructure. See [DECISIONS.md](./DECISIONS.md) for the full rationale.

**Why a provider abstraction for the LLM.** `ILLMProvider` (Application layer) is implemented by `OpenAIProvider` (Infrastructure). This exists so NexusAI is never locked to one vendor's pricing, rate limits, or context window — Phase 2 adds a second implementation (`AnthropicProvider`) behind the same contract, selectable per conversation or globally via configuration.

**Why memory is structured, not a flat transcript.** A raw chat history eventually exceeds any model's context window regardless of provider. NexusAI's memory model is deliberately layered:
- **Hierarchy**: `Workspace → Project → Conversation → ConversationMessage` — already implemented in the Domain layer exactly as designed.
- **Milestone window** *(planned, Phase 2)*: a per-project, human-approval-gated summary that only changes when the user explicitly approves an update — distinct from automatic knowledge extraction, so a project's key facts can never silently drift.
- **Branching** *(entity exists, orchestration planned Phase 2)*: side questions in a main conversation spin off into a `Branch` — its own conversation thread — and only the resolved conclusion folds back into the parent conversation's context, keeping the main thread lean.
- **Knowledge retrieval**: long-lived facts live as `Knowledge` records, retrieved by relevance (currently keyword-based via `KeywordKnowledgeRanker`; Phase 2 upgrades this to embeddings-based ranking) rather than replayed in full.

**Why a registry-based agent model.** Rather than one monolithic agent, `IAgentRegistry` is designed to hold multiple specialized agents (`AgentMetadata` per agent), dispatched by the `IAgentDispatcher`/`ExecutionEngine` pipeline. This is what lets NexusAI grow from one agent to many (Developer, Compiler, CNC Retrofit, and future business-function agents) without restructuring the execution pipeline each time. See "Current Deviations" below — this is the target design; the current wiring doesn't fully realize it yet.

**Why multiple front ends share one Api.** Both the planned Power Apps chat client and the Visual Studio-built desktop client are intentionally "dumb" — all business logic, memory, and orchestration lives behind `NexusAI.Api`. Neither client holds state beyond what it fetches from the API. This is why `NexusAI.Api` exists as a separate project rather than folding endpoints directly into `Host`.

## Primary Flows

**Chat flow** (`POST /api/chat` → `ChatService.SendAsync`):
`ChatEndpoint` → `IChatService.SendAsync` → loads conversation + project → persists the user message → loads full message history → retrieves relevant `Knowledge` for the workspace (keyword-ranked) → `IPromptBuilder` composes a `ChatRequest` (knowledge context + user prompt + history) → `ILLMProvider.ChatAsync` calls the live model → persists the assistant's reply.

**Planning + execution flow** (`CreatePlanHandler` → `ExecutePlanHandler`):
An objective string goes to `IPlanner.CreatePlanAsync`, which produces a list of `WorkItem`s persisted via `IWorkItemRepository`. `ExecutePlanHandler` then calls `IExecutionEngine.ExecuteAsync`, which builds an `AgentContext` and dispatches to `IAgentDispatcher`, which resolves an agent by type and runs it. See [DECISIONS.md](./DECISIONS.md) for an important caveat: this pipeline currently always resolves to a placeholder agent, not `DeveloperAgent` — a known gap Phase 2 Milestone 0 closes.

## Current Deviations from Target Architecture

This document describes the *intended* architecture. The codebase, as of Phase 1, has a few concrete deviations from it — most importantly, **two separate and currently-incompatible `IAgent` abstractions exist** (one in `NexusAI.Core.Agents`, one in `NexusAI.Application.Agents`), and the execution pipeline only wires up to the latter. `DeveloperAgent` (which implements the former) is currently invoked only via a one-off manual call in `NexusAI.Host/Program.cs`, disconnected from the planning/execution pipeline.

This is tracked in detail — with the exact call chain — in the **Known Issues** section of [DECISIONS.md](./DECISIONS.md), and resolving it is the first item in **Phase 2, Milestone 0** (see [ROADMAP.md](./ROADMAP.md)). It's called out here explicitly so this document doesn't overstate what's actually wired up today.

## Solution-Level Structure (`.slnx`)

The solution groups projects into folders that hint at intended structure, including two reserved-but-currently-empty groups worth knowing about: `/Clients/` (for future front-end client projects — the Power Apps and desktop app work will likely register here) and `/Tests/` (for the test suite, not yet populated).
