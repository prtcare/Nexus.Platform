# Nexus Architecture V2 — Platform / Intelligence / Products

**Status:** Proposed — supersedes the layer definitions in `02_ARCHITECTURE_AND_MODULES.md` and `11_FUTURE_OF_NEXUS_AI.md`
**Date:** 2026-08-17
**Author:** Durai + Claude
**Applies to:** `NexusAI` (backend), `Nexus.Web` (frontend)

---

## 0. What changed and why

The V1 codebase is one Clean-Architecture monolith. `NexusAI.Domain` holds Workspace, Project, Conversation, Knowledge, WorkItem, Artifact, Branch, Snapshot, Session and ADR; `NexusAI.Infrastructure` holds both the Dataverse repositories *and* the OpenAI provider; `NexusAI.Application` holds both product use-cases *and* the thin intelligence pieces (`PromptBuilder`, `Planner`, `ExecutionEngine`, `KeywordKnowledgeRanker`). Everything is compiled together and everything can reach everything.

That works for one product. It does not survive the second one.

**The corrected model:**

| Layer | Owns | Explicitly does NOT own |
|---|---|---|
| **Nexus Platform** | The backbone between products and AI: model providers, model catalog, tool/connector execution, credentials, identity & tenancy, usage metering, cost, quota, audit. | Any product's entities. Any product's database. Workspace, Project, Conversation, Knowledge — **none of these live here.** |
| **Nexus Intelligence** | The deciding layer: what to do, where to do it, how to do it. Intent, policy, planning, agent selection, model selection, tool selection, context ranking, memory, results, evaluation, explanation. | Knowledge of any product's schema. Direct provider SDK calls. Any product database. |
| **Nexus Products** | Everything a user actually sees and everything a product stores. Product #1 = **Nexus Chat** (the current chatbot) and it owns Workspace → Project → Conversation → Message → Knowledge → WorkItem → Artifact → Branch → Snapshot → Session → ADR, plus its Dataverse solution and its own frontend. | Provider SDKs, API keys, model selection logic, agent orchestration. |

**The one rule that makes this hold:**

> **Intelligence decides. Platform executes. Products own the data and the experience.**

Intelligence never calls OpenAI. It asks Platform *what models exist and what they cost*, picks one, and hands Platform an invocation to execute. Platform never knows what a Conversation is. Products never know that OpenAI exists.

---

## 1. Layer definitions

### 1.1 Nexus Platform — the backbone

Platform is the **only** code in the system that holds a vendor SDK or a vendor credential.

**Capabilities:**

| Capability | Contract | Responsibility |
|---|---|---|
| Model catalog | `IModelCatalog` | Lists available models with capabilities, context window, cost/1k, latency class, vendor. Read-only to Intelligence. |
| Model gateway | `IModelGateway` | Executes an invocation against the chosen model. Handles retries, timeouts, streaming, rate limits, provider-specific translation. Returns normalised result + usage. |
| Tool catalog | `IToolCatalog` | Lists registered tools/connectors with input schema, permission requirement, side-effect class (`read` / `write` / `irreversible`). |
| Tool gateway | `IToolGateway` | Executes a tool invocation under permission and budget checks. Enforces approval gates. |
| Identity & tenancy | `IIdentityService`, `ITenantResolver` | Resolves a caller to `{ tenantId, userId, roles, entitlements }`. Cross-product — a user is one user across Chat, Vault, ERP. |
| Product registry | `IProductRegistry` | Which products exist, which tenant is entitled to which, per-product configuration. |
| Metering & cost | `IUsageMeter`, `IQuotaPolicy` | Records tokens/cost/latency per tenant/product/turn. Enforces budget ceilings. |
| Audit | `IAuditLog` | Append-only record of every model call, tool call and decision. |
| Secrets | `ISecretResolver` | Provider keys, connector credentials. Never leaves Platform. |

**Platform's store** holds tenants, users, products, entitlements, provider configuration, the usage ledger and the audit log. This is *platform* data, not *product* data — it has no Workspace, no Project, no Conversation. It is deliberately small and deliberately not Dataverse.

> **Open decision (D-1):** Identity is placed in Platform on the reasoning that "a user" is a backbone concept shared by every product, not a product structure. If you'd rather each product own its own users, move `Nexus.Platform.Identity` into the product layer and Platform becomes purely the AI gateway. Flag this before Stage 2 of the migration.

### 1.2 Nexus Intelligence — the deciding layer

Intelligence is the layer you described as "what to do, where to do, how to do". It is **schema-agnostic by construction**: it cannot compile against a product type, because it does not reference any product assembly.

**Capabilities:**

| Capability | Responsibility |
|---|---|
| Intent & task classification | Is this a question, a task, a plan request, an approval, an event? |
| Policy & permission gate | Given the actor's roles and the constraint envelope, what is allowed this turn? |
| Context ranking & assembly | Takes the product-supplied `ContextBundle`, ranks items by relevance and trust, fits them to the chosen model's window, builds the prompt. |
| Planning & decomposition | Objective → ordered steps with dependencies and acceptance criteria. |
| Agent selection & orchestration | Registry of agents; selects by capability + permission + past outcome, runs the loop. |
| Model routing | Asks `IModelCatalog`, chooses by capability / cost / latency / constraint, hands to `IModelGateway`. |
| Tool selection | Chooses which tools the turn may use; Platform executes them. |
| Memory | Forms, consolidates and expires memories keyed by **opaque scope references**, never product foreign keys. |
| Results & evaluation | The Result Loop: product reports real-world outcome, Intelligence scores the original recommendation and updates routing/agent statistics. |
| Explanation | Why this model, this agent, these context items, this recommendation. |

**Intelligence's store** holds turn traces, memories, results, evaluations and routing statistics. Every row is keyed by `(tenantId, productId, scopeKey)` where `scopeKey` is an opaque string the product supplies. Intelligence can therefore hold memory *about* a conversation without knowing what a conversation is.

### 1.3 Nexus Products — the experiences

Each product is an independently deployable application with its own domain model, its own database, its own API and its own frontend.

**Product #1 — Nexus Chat** (the current chatbot) owns:

- Domain: Workspace, Project, Milestone, Conversation, ConversationMessage, Knowledge, ADR, WorkItem, Artifact, Branch, Snapshot, Session
- Persistence: Dataverse solution `N_001_Nexus`, publisher prefix `du_` — unchanged
- API: `/api/v1/*` — the only thing `Nexus.Web` ever calls
- Frontend: `Nexus.Web.Client`

Future products (Vault, ERP, Nexus Build, Nexus Machines) each get their own domain, their own store — relational, document, Dataverse, whatever fits — and their own API. This is exactly why Platform must not hold product structure: **each product's structure will be different.**

---

## 2. Allowed dependency edges

```
                 ┌──────────────────────────────────────┐
   Nexus.Web ──▶ │  Nexus.Products.Chat.Api  (/api/v1)  │
   (browser)     └──────────────┬───────────────────────┘
                                │  HTTP, versioned
                                │  Nexus.Intelligence.Contracts
                                ▼
                 ┌──────────────────────────────────────┐
                 │  Nexus.Intelligence.Api (/intel/v1)  │
                 │  Core · Context · Agents · Memory    │
                 └──────────────┬───────────────────────┘
                                │  in-process
                                │  Nexus.Platform.Contracts
                                ▼
                 ┌──────────────────────────────────────┐
                 │  Nexus.Platform.Core                 │
                 │  Providers · Tools · Identity · Meter│
                 └──────────────┬───────────────────────┘
                                │
                       OpenAI · Anthropic · Azure · connectors
```

**Reference rules — enforced by architecture tests, not by discipline:**

| Assembly | May reference | Must never reference |
|---|---|---|
| `Nexus.Products.*` | `Nexus.Intelligence.Contracts`, `Nexus.Shared.Kernel` | `Nexus.Platform.*`, `Nexus.Intelligence.Core/Context/Agents/Memory`, any provider SDK |
| `Nexus.Intelligence.*` | `Nexus.Platform.Contracts`, `Nexus.Shared.Kernel` | `Nexus.Products.*`, any provider SDK, any Dataverse type |
| `Nexus.Platform.*` | `Nexus.Shared.Kernel`, provider SDKs | `Nexus.Intelligence.*`, `Nexus.Products.*` |
| `Nexus.Shared.Kernel` | nothing | everything |

Plus two name-level rules:

- No type named `Workspace`, `Project`, `Conversation`, `ConversationMessage`, `Knowledge`, `WorkItem`, `Artifact`, `Branch`, `Snapshot`, `Session` or `Adr` may appear in any `Nexus.Intelligence.*` or `Nexus.Platform.*` assembly.
- `Nexus.Web` has exactly one API base URL and it points at the product API.

Because everything ships in **one host** at this stage (your chosen deployment model), these rules are the only thing keeping the layers real. Treat a failing architecture test as a build break, not a warning.

---

## 3. The contract surfaces

### 3.1 `Nexus.Intelligence.Contracts` — what products consume

This is the entire vocabulary a product needs. Nothing else in Intelligence is public.

**`POST /intelligence/v1/turns`**

```csharp
public sealed record IntelligenceTurnRequest
{
    public required string TenantId { get; init; }
    public required string ProductId { get; init; }          // "nexus.chat"
    public required ScopeRef Scope { get; init; }
    public required ActorRef Actor { get; init; }
    public required TurnInput Input { get; init; }
    public ContextBundle Context { get; init; } = ContextBundle.Empty;
    public IReadOnlyList<ContextSourceRef> ContextSources { get; init; } = [];
    public TurnConstraints Constraints { get; init; } = TurnConstraints.Default;
    public required string IdempotencyKey { get; init; }
    public string? CorrelationId { get; init; }
}

// An opaque product coordinate. Intelligence stores and compares it; it never parses it.
public sealed record ScopeRef(string Kind, string Key, IReadOnlyList<string> Path);
//   Chat sends: Kind="conversation", Key="<guid>",
//               Path=["workspace:<guid>","project:<guid>","conversation:<guid>"]

public sealed record ActorRef(string UserId, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed record TurnInput(TurnInputKind Kind, string Text, IReadOnlyList<AttachmentRef> Attachments);
public enum TurnInputKind { UserMessage, Task, Event, Approval }

// The product's data, flattened into a canonical shape. This is the seam.
public sealed record ContextBundle(IReadOnlyList<ContextItem> Items)
{
    public static ContextBundle Empty { get; } = new([]);
}

public sealed record ContextItem
{
    public required string Id { get; init; }              // product's own id, opaque here
    public required ContextItemKind Kind { get; init; }
    public string? Title { get; init; }
    public required string Body { get; init; }
    public required TrustLevel Trust { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public string? Author { get; init; }                  // "user" | "assistant" | user id
    public double? RelevanceHint { get; init; }           // product's own hint, 0..1
    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>();
}

public enum ContextItemKind
{
    Message, Fact, Document, Decision, Objective,
    Constraint, Artifact, Outcome, Instruction
}

public enum TrustLevel { Unverified, Reported, Curated, Approved, Authoritative }

public sealed record TurnConstraints
{
    public decimal? MaxCost { get; init; }
    public TimeSpan? LatencyBudget { get; init; }
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    public bool RequireApprovalForWrites { get; init; } = true;
    public string? ModelHint { get; init; }
    public static TurnConstraints Default { get; } = new();
}
```

**Response:**

```csharp
public sealed record IntelligenceTurnResponse
{
    public required string TurnId { get; init; }
    public required TurnOutcomeKind Outcome { get; init; }
    public ReplyPayload? Reply { get; init; }
    public PlanPayload? Plan { get; init; }
    public IReadOnlyList<ProposedAction> Actions { get; init; } = [];
    public IReadOnlyList<Citation> Citations { get; init; } = [];
    public IReadOnlyList<DecisionTrace> Decisions { get; init; } = [];
    public IReadOnlyList<PersistenceHint> PersistenceHints { get; init; } = [];
    public UsageSummary Usage { get; init; } = UsageSummary.Zero;
    public IReadOnlyList<TurnError> Errors { get; init; } = [];
}

public enum TurnOutcomeKind { Reply, Plan, Actions, Clarification, Refusal, Failed }

public sealed record Citation(string ContextItemId, string? Span);
public sealed record DecisionTrace(string What, string Why, IReadOnlyList<string> Alternatives);

// Intelligence proposes; the product decides whether to write it to its own store.
public sealed record PersistenceHint(PersistenceHintKind Kind, string Title, string Body, TrustLevel SuggestedTrust);
public enum PersistenceHintKind { KnowledgeCandidate, DecisionCandidate, MemoryNote, WorkItemCandidate, Summary }

public sealed record UsageSummary(int TokensIn, int TokensOut, decimal EstimatedCost, string ModelUsed)
{
    public static UsageSummary Zero { get; } = new(0, 0, 0m, string.Empty);
}
```

`PersistenceHint` is the piece that keeps the boundary honest. Intelligence never writes to the product's database. It says *"this looks like durable knowledge"* and the product decides whether to create a `Knowledge` record. Same for decisions, summaries and work items.

**Other endpoints:**

```
POST /intelligence/v1/results               → report a real-world outcome (the Result Loop)
GET  /intelligence/v1/turns/{id}/explanation → why this answer
POST /intelligence/v1/plans                 → decompose an objective
GET  /intelligence/v1/capabilities          → what this Intelligence instance can do today
```

### 3.2 `Nexus.Platform.Contracts` — what Intelligence consumes

In-process C# interfaces (your chosen comms model). Same shape as an HTTP API so it can be lifted out later without changing callers.

```csharp
public interface IModelCatalog
{
    Task<IReadOnlyList<ModelDescriptor>> ListAsync(ModelQuery query, CancellationToken ct = default);
}

public sealed record ModelDescriptor(
    string ModelId,            // "openai:gpt-4.1", "anthropic:claude-opus-5"
    string Vendor,
    ModelCapabilities Capabilities,
    int ContextWindow,
    decimal CostPer1kIn,
    decimal CostPer1kOut,
    LatencyClass Latency);

[Flags]
public enum ModelCapabilities
{
    None = 0, Chat = 1, Reasoning = 2, ToolUse = 4,
    Vision = 8, Streaming = 16, StructuredOutput = 32, LongContext = 64
}

public interface IModelGateway
{
    Task<ModelInvocationResult> InvokeAsync(ModelInvocation invocation, CancellationToken ct = default);
    IAsyncEnumerable<ModelStreamChunk> StreamAsync(ModelInvocation invocation, CancellationToken ct = default);
}

public sealed record ModelInvocation
{
    public required string ModelId { get; init; }
    public required IReadOnlyList<ModelMessage> Messages { get; init; }
    public IReadOnlyList<ToolDescriptor> Tools { get; init; } = [];
    public decimal? MaxCost { get; init; }
    public required InvocationIdentity Identity { get; init; }  // tenant, product, turn — for metering
}

public interface IToolCatalog { Task<IReadOnlyList<ToolDescriptor>> ListAsync(string tenantId, CancellationToken ct = default); }
public interface IToolGateway  { Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken ct = default); }
public interface IUsageMeter   { Task RecordAsync(UsageRecord record, CancellationToken ct = default); }
public interface IQuotaPolicy  { Task<QuotaVerdict> CheckAsync(InvocationIdentity identity, decimal estimatedCost, CancellationToken ct = default); }
public interface IAuditLog     { Task AppendAsync(AuditEntry entry, CancellationToken ct = default); }
public interface IIdentityService { Task<ResolvedIdentity?> ResolveAsync(string token, CancellationToken ct = default); }
```

Note what is *not* in `ModelInvocation`: no conversation id, no project id, no knowledge. Just messages and an identity for metering. Platform cannot learn product structure even by accident.

---

## 4. A chat turn, end to end

```
 1. Browser        POST /api/v1/chat { conversationId, prompt }
                        │
 2. Chat.Api ────▶ Chat.Application: SendChatHandler
 3.                     ├─ persist user message         → Dataverse (product store)
 4.                     ├─ load history, knowledge, project brief, active milestone
 5.                     └─ map them into ContextBundle  ← the seam: product schema dies here
                        │
 6.                     POST /intelligence/v1/turns  (HTTP, versioned)
                        ▼
 7. Intelligence   ├─ classify intent
 8.                ├─ policy gate against Actor.Permissions + Constraints
 9.                ├─ rank + trim ContextBundle by relevance × trust
10.                ├─ select agent from registry
11.                ├─ IModelCatalog.ListAsync → choose model for capability/cost/latency
12.                ├─ build prompt
                        │
13.                └─ IModelGateway.InvokeAsync   (in-process)
                        ▼
14. Platform       ├─ IQuotaPolicy.CheckAsync
15.                ├─ resolve credential, call vendor SDK, retry/timeout
16.                ├─ IUsageMeter.RecordAsync + IAuditLog.AppendAsync
17.                └─ return normalised result
                        │
18. Intelligence   ├─ (optional tool loop → IToolGateway, approval-gated)
19.                ├─ write turn trace + memory  → Intelligence store (opaque scope key)
20.                └─ return reply + citations + decisions + persistenceHints
                        │
21. Chat.Application ├─ persist assistant message      → Dataverse
22.                  ├─ apply persistenceHints (e.g. propose a Knowledge record)
23.                  └─ map to product DTO
24. Browser        ← { reply, citations, usage }

  … later …
25. Chat           POST /intelligence/v1/results { turnId, outcome, evidence }
26. Intelligence   score the recommendation, update agent + routing statistics
```

Steps 5 and 20 are the entire boundary. If a future change makes Intelligence want to read a Dataverse table directly, that is the signal the `ContextBundle` shape is wrong — fix the shape, not the boundary.

---

## 5. Target solution structure

```
Nexus/
├── Nexus.slnx
├── Directory.Build.props            # nullable, langversion, + boundary MSBuild guards
├── global.json                      # .NET 10.0.302  (unchanged)
│
├── src/
│   ├── shared/
│   │   └── Nexus.Shared.Kernel/            # Result<T>, IClock, ProblemDetails, guards. No business meaning.
│   │
│   ├── platform/
│   │   ├── Nexus.Platform.Contracts/       # IModelCatalog, IModelGateway, ITool*, IIdentity*, records
│   │   ├── Nexus.Platform.Core/            # gateway impl, routing execution, quota, metering, audit
│   │   ├── Nexus.Platform.Providers.OpenAI/        # ← NexusAI.Infrastructure/OpenAI
│   │   ├── Nexus.Platform.Providers.Anthropic/     # new, empty scaffold
│   │   ├── Nexus.Platform.Tools/           # tool registry + governed execution
│   │   ├── Nexus.Platform.Identity/        # tenants, users, entitlements   [see D-1]
│   │   └── Nexus.Platform.Persistence/     # platform-only store. NOT product data.
│   │
│   ├── intelligence/
│   │   ├── Nexus.Intelligence.Contracts/   # the ONLY assembly products may reference
│   │   ├── Nexus.Intelligence.Core/        # intent, policy, planning, model+tool selection
│   │   ├── Nexus.Intelligence.Context/     # ContextBundle ranking, prompt assembly
│   │   ├── Nexus.Intelligence.Agents/      # registry, runtime, dispatcher, built-in agents
│   │   ├── Nexus.Intelligence.Memory/      # memory, traces, results, evaluation store
│   │   └── Nexus.Intelligence.Api/         # /intelligence/v1/*
│   │
│   └── products/
│       └── chat/
│           ├── Nexus.Products.Chat.Domain/         # ← NexusAI.Domain (all of it)
│           ├── Nexus.Products.Chat.Application/    # ← NexusAI.Application (product use-cases)
│           ├── Nexus.Products.Chat.Infrastructure/ # ← NexusAI.Infrastructure/Dataverse
│           └── Nexus.Products.Chat.Api/            # ← NexusAI.Api  → /api/v1/*
│
├── host/
│   └── Nexus.Host/                  # single deployable; mounts Chat.Api + Intelligence.Api
│
└── tests/
    ├── Nexus.Architecture.Tests/    # the boundary rules, build-breaking
    ├── Nexus.Platform.Tests/
    ├── Nexus.Intelligence.Tests/
    └── Nexus.Products.Chat.Tests/
```

---

## 6. File-by-file migration map

### 6.1 Domain — all of it goes to the product

| From | To |
|---|---|
| `NexusAI.Domain/Workspace/*` | `Nexus.Products.Chat.Domain/Workspace/*` |
| `NexusAI.Domain/Project/*` | `Nexus.Products.Chat.Domain/Project/*` |
| `NexusAI.Domain/Conversation/*` | `Nexus.Products.Chat.Domain/Conversation/*` |
| `NexusAI.Domain/ConversationMessage/*` | `Nexus.Products.Chat.Domain/ConversationMessage/*` |
| `NexusAI.Domain/Knowledge/*` | `Nexus.Products.Chat.Domain/Knowledge/*` |
| `NexusAI.Domain/WorkItem/*` | `Nexus.Products.Chat.Domain/WorkItem/*` |
| `NexusAI.Domain/Artifact/*` | `Nexus.Products.Chat.Domain/Artifact/*` |
| `NexusAI.Domain/Branch/*` | `Nexus.Products.Chat.Domain/Branch/*` |
| `NexusAI.Domain/Snapshot/*` | `Nexus.Products.Chat.Domain/Snapshot/*` |
| `NexusAI.Domain/Session/*` | `Nexus.Products.Chat.Domain/Session/*` |
| `NexusAI.Domain/Adr/*` | `Nexus.Products.Chat.Domain/Adr/*` |
| `NexusAI.Domain/Common/{AggregateRoot,Entity,IRepository}.cs` | `Nexus.Shared.Kernel/Domain/*` |
| `NexusAI.Domain/Common/Identifiers/WorkspaceId.cs` | `Nexus.Products.Chat.Domain/Workspace/WorkspaceId.cs` |
| `NexusAI.Domain/Memory/*` | **Split.** The product keeps nothing; memory is an Intelligence concern → rewrite as `Nexus.Intelligence.Memory/MemoryRecord.cs` keyed by `ScopeRef`. Delete the Dataverse memory table from the product solution once migrated. |

### 6.2 Application — split by concern

| From | To | Note |
|---|---|---|
| `Application/{Workspaces,Projects,Conversations,ConversationMessages,WorkItem,Knowledge,Branch,Snapshot,Session,Artifact,Adr}/**` | `Nexus.Products.Chat.Application/**` | Straight move. |
| `Application/Chat/ChatService.cs`, `IChatService.cs` | `Nexus.Products.Chat.Application/Chat/` | Keep; it becomes the turn orchestrator on the product side. |
| `Application/Chat/Commands/SendChat/*` | `Nexus.Products.Chat.Application/Chat/Commands/SendChat/` | **Rewrite** — replace the `ILLMProvider` call with `IIntelligenceClient.SendTurnAsync`. |
| `Application/Chat/{ConversationContext,IConversationContextProvider}.cs` | `Nexus.Products.Chat.Application/Chat/Context/` | Becomes the `ContextBundle` mapper. |
| `Application/Chat/Prompting/{PromptBuilder,IPromptBuilder,PromptContext}.cs` | `Nexus.Intelligence.Context/Prompting/` | **Moves layer.** Prompt assembly is an intelligence decision. |
| `Application/Knowledge/Services/{IKnowledgeRanker,KeywordKnowledgeRanker}.cs` | `Nexus.Intelligence.Context/Ranking/` | Generalise from `Knowledge` to `ContextItem`. |
| `Application/Knowledge/Services/{KnowledgeRetrievalService,IKnowledgeRetrievalService,KnowledgeContextProvider,IKnowledgeContextProvider}.cs` | `Nexus.Products.Chat.Application/Knowledge/Retrieval/` | Fetching from the product's own store stays with the product. |
| `Application/Planning/{Planner,IPlanner}.cs` | `Nexus.Intelligence.Core/Planning/` | Currently returns 4 hard-coded work items — rewrite against the model. |
| `Application/Planning/Commands/*` | `Nexus.Intelligence.Core/Planning/` | Becomes the `/plans` endpoint handler. |
| `Application/Execution/{ExecutionEngine,IExecutionEngine,ExecutionContext,ExecutionResult,IAgentDispatcher}.cs` | `Nexus.Intelligence.Core/Execution/` | Currently always dispatches `AgentType.Developer` — rewrite as real selection. |
| `Application/Execution/Commands/*` | `Nexus.Intelligence.Core/Execution/` | |
| `Application/Providers/{ILLMProvider,ChatRequest,ChatResponse,ChatMessage}.cs` | `Nexus.Platform.Contracts/Models/` | **Renamed:** `IModelGateway`, `ModelInvocation`, `ModelInvocationResult`, `ModelMessage`. |
| `Application/Abstractions/{ICommandHandler,IQueryHandler}.cs` | `Nexus.Shared.Kernel/Abstractions/` | |
| `Application/DependencyInjection/*`, `Workspaces/WorkspaceServiceCollectionExtensions.cs` | `Nexus.Products.Chat.Application/DependencyInjection/` | |

### 6.3 Infrastructure — split by vendor

| From | To |
|---|---|
| `Infrastructure/Dataverse/**` (client, context, entities, mappers, repositories) | `Nexus.Products.Chat.Infrastructure/Dataverse/**` |
| `Infrastructure/OpenAI/{OpenAIProvider,OpenAIOptions}.cs` | `Nexus.Platform.Providers.OpenAI/` — rewritten to implement `IModelGateway` + contribute to `IModelCatalog` |
| `Infrastructure/Services/{AgentDispatcher,AgentRegistry}.cs` | `Nexus.Intelligence.Agents/` |
| `Infrastructure/Services/ConversationContextProvider.cs` | `Nexus.Products.Chat.Infrastructure/Context/` |
| `Infrastructure/Services/SystemClock.cs` | `Nexus.Shared.Kernel/Time/` |
| `Infrastructure/Registration/{CoreModule,ModuleExtensions}.cs`, `ServiceCollectionExtensions.cs` | Split across each layer's own `DependencyInjection/` |

### 6.4 Core / Agents / Api / Host

| From | To |
|---|---|
| `NexusAI.Core/Agents/*` (IAgent, IAgentRegistry, IAgentRuntime, AgentContext, AgentMetadata, AgentResult, AgentRuntime, AgentType) | `Nexus.Intelligence.Agents/Abstractions/` — `AgentContext` loses `ProjectId`/`ConversationId` and takes `ScopeRef` |
| `NexusAI.Core/Modules/INexusModule.cs` | `Nexus.Shared.Kernel/Modules/` |
| `NexusAI.Agents/DeveloperAgent/*` | `Nexus.Intelligence.Agents/BuiltIn/DeveloperAgent.cs` |
| `NexusAI.Api/Endpoints/**` (all feature endpoints) | `Nexus.Products.Chat.Api/Endpoints/**`, rebased to `/api/v1` |
| `NexusAI.Api/Endpoints/PlatformHealthEndpoint.cs` | `Nexus.Host/Endpoints/HealthEndpoint.cs` |
| `NexusAI.Api/Program.cs` | Split: product wiring → `Nexus.Products.Chat.Api/ChatProductModule.cs`; app bootstrap → `Nexus.Host/Program.cs` |

### 6.5 Delete

| Path | Reason |
|---|---|
| `NexusAI.Api/Controllers/WeatherForecastController.cs`, `NexusAI.Api/WeatherForecast.cs` | Template sample — already flagged as debt |
| `NexusAI.Host/Program.cs` (the 300-line demo script) | Replace with integration tests + a `Nexus.Tools.Seed` console if seeding is still wanted |
| `NexusAI.Foundation/` | Empty placeholder; superseded by `Nexus.Shared.Kernel` |
| `NexusAI.Api/libman.json` | Unused client-library manifest |
| `Nexus.Web/src/features/workspaces/WorkspaceContext.tsx` (the stray copy outside `Nexus.Web.Client`) | Duplicate of the real file one level down |
| `NexusAI Documentation/**/*.zip` | Nested doc ZIPs — use git tags |

### 6.6 Frontend changes

Minimal, by design — the frontend is already correctly ignorant.

- `.env.*`: keep exactly one variable, `VITE_NEXUS_API_URL`, pointing at the **product** API. Never add an intelligence URL.
- `src/api/ApiClient.ts`: base path becomes `/api/v1`.
- `src/pages/IntelligencePage.tsx`: rename to a product-facing concept (e.g. `InsightsPage`) — the frontend should not have a page named after an internal layer it cannot see. It renders `citations`, `decisions` and `usage` returned through the product API.
- `src/pages/ProductsPage.tsx`: becomes the product switcher once product #2 exists.
- Add `src/features/chat/` — currently missing; the chatbot has no chat UI yet.

---

## 7. Data ownership

| Store | Owner | Contents | Technology |
|---|---|---|---|
| Chat product store | `Nexus.Products.Chat` | Workspace, Project, Milestone, Conversation, Message, Knowledge, ADR, WorkItem, Artifact, Branch, Snapshot, Session | Dataverse `N_001_Nexus`, prefix `du_` (unchanged) |
| Platform store | `Nexus.Platform` | Tenants, users, products, entitlements, provider config, usage ledger, audit log | **Not Dataverse.** Relational (SQL/Postgres) or, for Stage 1, a single JSON-configured in-memory + append-only file, behind `IPlatformStore` |
| Intelligence store | `Nexus.Intelligence` | Turn traces, memories, results, evaluations, routing statistics | Append-heavy; same physical database as Platform initially, separate schema |

**Rules:**

1. No cross-store foreign keys. Ever. Intelligence references product rows only by opaque `ScopeRef` strings.
2. No layer reads another layer's tables. Only contracts.
3. Product #2 picks its own technology. That is the whole point of keeping Platform structureless.
4. Per doc rule #14 ("avoid premature infrastructure"), Platform and Intelligence share one physical database until there is a measured reason to split. They never share a *schema* with a product.

---

## 8. Migration stages

Each stage ends with a green build. Do not start the next until the previous compiles and existing behaviour is unchanged.

| Stage | Work | Done when |
|---|---|---|
| **0. Baseline** | Commit current state, tag `pre-v2`, branch `arch/v2`. Restore + build on .NET 10. Delete WeatherForecast, `libman.json`, `NexusAI.Foundation`, the Host demo script, nested doc ZIPs. | `dotnet build` clean on the branch |
| **1. Skeleton** | Create the new folder tree, `Nexus.Shared.Kernel`, all empty csproj files with correct references, new `Nexus.slnx`. Add `Nexus.Architecture.Tests` (rules written, referencing empty assemblies). | Solution loads in VS; empty projects build |
| **2. Platform** | Author `Nexus.Platform.Contracts`. Move OpenAI provider → `Providers.OpenAI`, rewrite to `IModelGateway` + `IModelCatalog`. Implement `Platform.Core` (quota, meter, audit as no-op-then-real). Stand up `Platform.Identity` scaffold. | Platform builds with zero references to Intelligence/Products; a console test can invoke a model through the gateway |
| **3. Intelligence** | Author `Nexus.Intelligence.Contracts` (the records in §3.1). Move Planner, ExecutionEngine, agent registry/runtime/dispatcher, PromptBuilder, ranker. Build `Intelligence.Core` turn pipeline. Stand up `Intelligence.Api` at `/intelligence/v1`. | `POST /intelligence/v1/turns` returns a real model reply given a hand-written `ContextBundle` |
| **4. Product rename** | `NexusAI.Domain/Application/Infrastructure/Api` → `Nexus.Products.Chat.*`. Namespace rewrite. Rebase routes to `/api/v1`. | Product builds; all existing endpoints behave identically |
| **5. Rewire the turn** | `SendChatHandler` stops calling `ILLMProvider`. Add `ContextBundle` mapper. Add typed `IIntelligenceClient` (HTTP). Apply `PersistenceHint` handling. | A chat turn works end to end through Intelligence; product has zero provider references |
| **6. Host** | Single `Nexus.Host` mounts Chat.Api + Intelligence.Api. Consolidate configuration, user secrets, DI. Delete `NexusAI.Host` and `NexusAI.Api` shells. | One `dotnet run` serves both surfaces; Swagger shows two tagged groups |
| **7. Enforce** | Turn architecture tests build-breaking. Update frontend base URL + rename Intelligence page. Rewrite canonical docs 02, 04, 11, 12 to V2. Tag `v2-arch`. | Architecture tests pass and fail correctly when deliberately violated |

---

## 9. What this buys you

- **Product #2 costs a folder, not a refactor.** Vault gets `src/products/vault/` with its own domain, its own store, its own API — and consumes the same `IIntelligenceClient` on day one.
- **Swapping OpenAI for Anthropic is one project.** Nothing outside `src/platform/providers/` knows.
- **Intelligence improvements are free across products.** Better ranking, better routing, better agents — every product gets them without a code change, because every product speaks `ContextBundle`.
- **The Result Loop becomes possible.** Turns, decisions and outcomes live in one place with a stable key, across every product.
- **Your existing docs' rules 1, 2, 6, 7, 8 stop being aspirations** and become compiler errors.

## 10. Open decisions

| ID | Decision | Default taken here |
|---|---|---|
| D-1 | Does identity live in Platform or per-product? | Platform. Flip before Stage 2 if you disagree. |
| D-2 | Does the Chat product keep the `Memory` table, or does memory belong wholly to Intelligence? | Wholly to Intelligence. The product's Dataverse `Memory` table is retired. |
| D-3 | Physical store for Platform + Intelligence. | One non-Dataverse relational DB, two schemas. Revisit at Stage 2. |
| D-4 | Streaming chat responses. | Contract includes `StreamAsync`; not implemented until after Stage 6. |
| D-5 | Do agents live in Intelligence only, or can products register agents? | Intelligence only for now; product-registered agents are a Horizon 3 concern. |
