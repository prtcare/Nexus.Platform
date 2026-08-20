# Nexus Architecture V2.1 — Three Solutions

**Status:** Proposed — supersedes V2.0 (single-repo) and the layer definitions in
`02_ARCHITECTURE_AND_MODULES.md` and `11_FUTURE_OF_NEXUS_AI.md`
**Date:** 2026-08-17
**Author:** Durai + Claude

---

## 0. What changed from V2.0

V2.0 assumed one repository and one host. You've since decided on **three separate
solutions, one per layer**. That is a bigger structural change and it makes some V2.0
decisions impossible, so this document replaces it entirely.

| | V2.0 | V2.1 (this document) |
|---|---|---|
| Layout | one repo, one host | three repos, three solutions |
| Platform | project group | `Nexus.AI` solution, shipped as NuGet libraries |
| Intelligence | project group | `Nexus.Int` solution, deployable API |
| Product | project group | `Nexus.Web` solution, deployable API + React client |
| Product → Intelligence | HTTP | HTTP (unchanged) |
| Intelligence → Platform | project reference | **NuGet package reference, still in-process** |
| Shared kernel | one shared project | **none** — see §4 |

**Any script or runbook dated before this document is void.** Do not run
`nexus-restructure.ps1` or `run-migration.ps1` as they currently exist on disk.

---

## 1. The three solutions

| Solution | Repo | Contains | Deployed? |
|---|---|---|---|
| **Nexus.AI** | `C:\Personal\NexusAI` | Nexus Platform — the backbone between products and AI | No. Class libraries, packaged to NuGet. |
| **Nexus.Int** | `C:\Personal\Nexus.Int` *(new)* | Nexus Intelligence — the deciding layer | Yes. HTTP API at `/intelligence/v1`. |
| **Nexus.Web** | `C:\Personal\Nexus.Web` | The Chatbot product — React client, .NET API, domain, Dataverse | Yes. HTTP API at `/api/v1` + static client. |

**The rule, unchanged:**

> **Intelligence decides. Platform executes. Products own the data and the experience.**

### 1.1 Nexus.AI — the Platform

The only code in the entire system that holds a vendor SDK or a vendor credential.

| Capability | Contract |
|---|---|
| Model catalog | `IModelCatalog` — what models exist, their capabilities, context window, cost, latency class |
| Model gateway | `IModelGateway` — executes an invocation, handles retries/timeouts/streaming, returns normalised result + usage |
| Tool catalog / gateway | `IToolCatalog`, `IToolGateway` — governed tool execution with side-effect classes |
| Identity & tenancy | `IIdentityService`, `ITenantResolver`, `IProductRegistry` |
| Metering & governance | `IUsageMeter`, `IQuotaPolicy`, `IAuditLog` |
| Secrets | `ISecretResolver` |

Platform's store holds tenants, users, products, entitlements, provider configuration, the
usage ledger and the audit log. **No Workspace. No Project. No Conversation.** It is
deliberately small and deliberately not Dataverse.

### 1.2 Nexus.Int — the Intelligence

The layer that decides *what to do, where, and how*. Schema-agnostic by construction: it
references no product assembly, so it cannot compile against a product type.

Intent classification · policy and permission gating · context ranking and prompt assembly ·
planning and decomposition · agent selection and orchestration · model routing · tool
selection · memory formation · result evaluation · explanation.

Its store holds turn traces, memories, results, evaluations and routing statistics — every
row keyed by `(TenantId, ProductId, ScopeRef)` where `ScopeRef` is an **opaque string the
product supplies**. Intelligence can hold memory *about* a conversation without knowing
what a conversation is.

### 1.3 Nexus.Web — the Chatbot product

The first product, end to end: its own UI, its own API, its own domain, its own database.

Owns Workspace, Project, Milestone, Conversation, ConversationMessage, Knowledge, ADR,
WorkItem, Artifact, Branch, Snapshot, Session — persisted to Dataverse solution
`N_001_Nexus`, publisher prefix `du_`.

Future products (Vault, ERP, Nexus Build) get their own solution, their own store — Dataverse,
SQL, document, whatever fits — and their own UI. **This is precisely why Platform holds no
product structure: each product's structure will be different.**

---

## 2. Dependency edges and packaging

```
   Browser
      │  HTTPS
      ▼
┌─────────────────────────────────────────┐
│  Nexus.Web            (deployable)      │
│  ├── Nexus.Web.Client        React      │
│  ├── Nexus.Products.Chat.Api    /api/v1 │
│  ├── ...Application  ...Domain          │
│  └── ...Infrastructure  →  Dataverse    │
└──────────────────┬──────────────────────┘
                   │  HTTP  /intelligence/v1
                   │  📦 Nexus.Intelligence.Contracts
                   ▼
┌─────────────────────────────────────────┐
│  Nexus.Int            (deployable)      │
│  ├── Nexus.Intelligence.Api             │
│  ├── ...Core  ...Context                │
│  └── ...Agents  ...Memory               │
└──────────────────┬──────────────────────┘
                   │  in-process
                   │  📦 Nexus.Platform.*
                   ▼
┌─────────────────────────────────────────┐
│  Nexus.AI             (libraries only)  │
│  ├── Nexus.Platform.Contracts           │
│  ├── Nexus.Platform.Core                │
│  ├── Nexus.Platform.Providers.OpenAI    │
│  └── ...Tools  ...Identity  ...Persistence
└──────────────────┬──────────────────────┘
                   ▼
        OpenAI · Anthropic · connectors
```

### 2.1 Why Platform is a package, not a service

You asked me to choose. Platform ships as NuGet libraries running **in-process inside the
Nexus.Int host**.

**Reasoning.** Platform has exactly one consumer and always will by design — products are
forbidden from touching it. Making it a network service adds a hop to every chat turn's hot
path in exchange for isolating it from a single caller that already trusts it. Your own
architecture rule #14 says don't introduce services until a verified requirement needs one.
The secret-isolation argument doesn't favour a service either: the boundary that matters is
*products never see provider keys*, and that holds in both designs.

**The escape hatch.** Platform contracts are shaped exactly as if they were HTTP — async,
DTO in / DTO out, no shared mutable state, no `IQueryable` leaking across. The day you need
Platform as a service, you wrap `Nexus.Platform.Core` in an API host and swap the DI
registration. Contracts don't change; callers don't change.

**Flip to HTTP when:** a second Intelligence instance appears, or a second trusted consumer
needs Platform, or you need a central usage ledger across regions.

### 2.2 The two packages

| Package | Produced by | Consumed by | Why |
|---|---|---|---|
| `Nexus.Platform.*` | Nexus.AI | Nexus.Int | Intelligence needs the model/tool/governance contracts and implementations |
| `Nexus.Intelligence.Contracts` | Nexus.Int | Nexus.Web | The product needs the typed turn request/response and `IIntelligenceClient` |

Both publish to a **local file-system feed** during development:

```
C:\Personal\LocalNuGet\
```

Each producing solution gets a `pack-local.ps1`. Each consuming solution gets a
`nuget.config` pointing at that folder. When you move to CI, swap the feed URL — nothing
else changes.

> **Note on friction.** During the migration, Platform and Intelligence are built at the
> same time, so you'll re-pack often. If that becomes annoying, the standard fix is a
> conditional in `Directory.Build.props` that uses a `ProjectReference` when the sibling
> repo exists on disk and a `PackageReference` otherwise. Start simple; add it only if the
> pack cycle actually slows you down.

### 2.3 Reference rules

| Solution | May reference | Must never reference |
|---|---|---|
| `Nexus.Web` | `Nexus.Intelligence.Contracts` (package) | anything `Nexus.Platform.*`, any provider SDK, any Intelligence internals |
| `Nexus.Int` | `Nexus.Platform.*` (packages) | anything `Nexus.Products.*` |
| `Nexus.AI` | vendor SDKs only | anything `Nexus.Intelligence.*` or `Nexus.Products.*` |

Plus two name-level rules, enforced by architecture tests in each solution:

- No type named `Workspace`, `Project`, `Conversation`, `ConversationMessage`, `Knowledge`,
  `WorkItem`, `Artifact`, `Branch`, `Snapshot`, `Session` or `Adr` may appear anywhere in
  `Nexus.AI` or `Nexus.Int`.
- `Nexus.Web.Client` has exactly one API base URL and it points at the product API.

Because the solutions are now physically separate, most of these rules are enforced by the
package graph rather than by discipline — a product simply cannot reference a Platform type
it hasn't installed. The architecture tests catch the remaining case: someone adding the
wrong package.

---

## 3. Contracts

### 3.1 `Nexus.Intelligence.Contracts` — what the product consumes

```csharp
public sealed record IntelligenceTurnRequest
{
    public required string TenantId  { get; init; }
    public required string ProductId { get; init; }        // "nexus.chat"
    public required ScopeRef Scope   { get; init; }
    public required ActorRef Actor   { get; init; }
    public required TurnInput Input  { get; init; }
    public ContextBundle Context { get; init; } = ContextBundle.Empty;
    public TurnConstraints Constraints { get; init; } = TurnConstraints.Default;
    public required string IdempotencyKey { get; init; }
    public string? CorrelationId { get; init; }
}

// An opaque product coordinate. Intelligence stores and compares it; it never parses it.
public sealed record ScopeRef(string Kind, string Key, IReadOnlyList<string> Path);

public sealed record ActorRef(string UserId, IReadOnlyList<string> Roles,
                              IReadOnlyList<string> Permissions);

public sealed record TurnInput(TurnInputKind Kind, string Text);
public enum TurnInputKind { UserMessage, Task, Event, Approval }

// The product's data, flattened into a canonical shape. THIS IS THE SEAM.
public sealed record ContextBundle(IReadOnlyList<ContextItem> Items)
{
    public static ContextBundle Empty { get; } = new([]);
}

public sealed record ContextItem
{
    public required string Id { get; init; }          // the product's own id, opaque here
    public required ContextItemKind Kind { get; init; }
    public string? Title { get; init; }
    public required string Body { get; init; }
    public required TrustLevel Trust { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public string? Author { get; init; }
    public double? RelevanceHint { get; init; }
}

public enum ContextItemKind
{
    Message, Fact, Document, Decision, Objective, Constraint, Artifact, Outcome, Instruction
}

public enum TrustLevel { Unverified, Reported, Curated, Approved, Authoritative }

public sealed record IntelligenceTurnResponse
{
    public required string TurnId { get; init; }
    public required TurnOutcomeKind Outcome { get; init; }
    public ReplyPayload? Reply { get; init; }
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
public sealed record PersistenceHint(PersistenceHintKind Kind, string Title, string Body,
                                     TrustLevel SuggestedTrust);
public enum PersistenceHintKind
{
    KnowledgeCandidate, DecisionCandidate, MemoryNote, WorkItemCandidate, Summary
}

public sealed record UsageSummary(int TokensIn, int TokensOut, decimal EstimatedCost,
                                  string ModelUsed)
{
    public static UsageSummary Zero { get; } = new(0, 0, 0m, string.Empty);
}

public interface IIntelligenceClient
{
    Task<IntelligenceTurnResponse> SendTurnAsync(IntelligenceTurnRequest request,
                                                 CancellationToken ct = default);
    Task ReportResultAsync(ResultReport report, CancellationToken ct = default);
}
```

`PersistenceHint` is what keeps the boundary honest. Intelligence never writes to the
product's database. It says *"this looks like durable knowledge"* and the product decides.

**HTTP surface (`/intelligence/v1`):**

```
POST /turns                    send a turn
POST /results                  report a real-world outcome (the Result Loop)
GET  /turns/{id}/explanation   why this answer
POST /plans                    decompose an objective
GET  /capabilities             what this instance can do today
```

### 3.2 `Nexus.Platform.Contracts` — what Intelligence consumes

```csharp
public interface IModelCatalog
{
    Task<IReadOnlyList<ModelDescriptor>> ListAsync(ModelQuery q, CancellationToken ct = default);
}

public sealed record ModelDescriptor(
    string ModelId,                 // "openai:gpt-4.1"
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
    Task<ModelInvocationResult> InvokeAsync(ModelInvocation i, CancellationToken ct = default);
    IAsyncEnumerable<ModelStreamChunk> StreamAsync(ModelInvocation i, CancellationToken ct = default);
}

public sealed record ModelInvocation
{
    public required string ModelId { get; init; }
    public required IReadOnlyList<ModelMessage> Messages { get; init; }
    public IReadOnlyList<ToolDescriptor> Tools { get; init; } = [];
    public decimal? MaxCost { get; init; }
    public required InvocationIdentity Identity { get; init; }
}

// The metering key - and deliberately the ONLY identity Platform ever sees.
public sealed record InvocationIdentity(string TenantId, string ProductId,
                                        string TurnId, string UserId);

public interface IToolCatalog { /* ... */ }
public interface IToolGateway { /* ... */ }
public interface IUsageMeter  { /* ... */ }
public interface IQuotaPolicy { /* ... */ }
public interface IAuditLog    { /* ... */ }
public interface IIdentityService { /* ... */ }
public interface ISecretResolver  { /* ... */ }
```

Note what is *not* in `ModelInvocation`: no conversation id, no project id, no knowledge.
Platform cannot learn product structure even by accident.

---

## 4. No shared kernel

V2.0 had a `Nexus.Shared.Kernel`. V2.1 deletes the idea. Across three repos a shared kernel
becomes a coupling point and a third package to version, and everything that would go in it
is either product-specific or trivially small:

| Candidate | Verdict |
|---|---|
| `Entity`, `AggregateRoot`, `IRepository` | Product-only → `Nexus.Products.Chat.Domain` |
| `ICommandHandler`, `IQueryHandler` | Five lines each. Duplicate per solution; do not couple three repos over an interface. |
| `IClock`, `SystemClock` | **Delete.** Use .NET's built-in `TimeProvider`. |
| `INexusModule` | Unused placeholder. Delete. |
| Problem-details helpers | Duplicate per API host; they differ anyway. |

Each solution gets a small `Common/` folder for its own primitives.

---

## 5. A chat turn, end to end

```
 1. Browser              POST /api/v1/chat { conversationId, prompt }
                              │
 2. Nexus.Web  Chat.Api ─▶ Chat.Application: SendChatHandler
 3.                       ├─ persist user message           → Dataverse
 4.                       ├─ load history, knowledge, ADRs, project objective
 5.                       └─ map to ContextBundle           ← THE SEAM: schema dies here
                              │
 6.                       POST /intelligence/v1/turns   (HTTP)
                              ▼
 7. Nexus.Int             ├─ classify intent
 8.                       ├─ policy gate on Actor.Permissions + Constraints
 9.                       ├─ rank + trim ContextBundle by relevance x trust
10.                       ├─ select agent
11.                       ├─ IModelCatalog.ListAsync → choose model
12.                       ├─ assemble prompt to fit the chosen context window
                              │
13.                       └─ IModelGateway.InvokeAsync    (in-process, Platform package)
                              ▼
14. Nexus.AI              ├─ IQuotaPolicy.CheckAsync
15.                       ├─ resolve credential, call vendor SDK, retry/timeout
16.                       ├─ IUsageMeter.Record + IAuditLog.Append
17.                       └─ normalised result
                              │
18. Nexus.Int             ├─ optional tool loop (approval-gated)
19.                       ├─ write turn trace + memory   → Intelligence store
20.                       └─ reply + citations + decisions + persistenceHints
                              │
21. Nexus.Web             ├─ persist assistant message     → Dataverse
22.                       ├─ apply persistenceHints (Knowledge candidate, pending approval)
23.                       └─ map to product DTO
24. Browser               ← { reply, citations, usage }

    … later …
25. Nexus.Web             POST /intelligence/v1/results { turnId, outcome, evidence }
26. Nexus.Int             score the recommendation, update agent + routing statistics
```

Steps 5 and 20 are the entire boundary. If a future change makes Intelligence want to read
Dataverse directly, that's the signal `ContextBundle` is the wrong shape — fix the shape,
never the boundary.

---

## 6. Target structure per solution

### 6.1 Nexus.AI — `C:\Personal\NexusAI`

```
NexusAI/
├── Nexus.AI.slnx
├── Directory.Build.props
├── global.json                          .NET 10.0.302 (unchanged)
├── pack-local.ps1                       build + pack → C:\Personal\LocalNuGet
├── src/
│   ├── Nexus.Platform.Contracts/        IModelCatalog, IModelGateway, ITool*, IIdentity*
│   ├── Nexus.Platform.Core/             routing gateway, aggregating catalog, quota, meter, audit
│   ├── Nexus.Platform.Providers.OpenAI/ ← NexusAI.Infrastructure/OpenAI
│   ├── Nexus.Platform.Providers.Anthropic/   scaffold
│   ├── Nexus.Platform.Tools/            tool registry + governed execution
│   ├── Nexus.Platform.Identity/         tenants, users, entitlements
│   └── Nexus.Platform.Persistence/      platform-only store. NOT product data.
└── tests/
    ├── Nexus.Platform.Tests/
    └── Nexus.Platform.Architecture.Tests/
```

### 6.2 Nexus.Int — `C:\Personal\Nexus.Int` (new repo)

```
Nexus.Int/
├── Nexus.Int.slnx
├── Directory.Build.props
├── global.json
├── nuget.config                         → C:\Personal\LocalNuGet
├── pack-local.ps1                       packs Nexus.Intelligence.Contracts
├── src/
│   ├── Nexus.Intelligence.Contracts/    the ONLY thing products may reference
│   ├── Nexus.Intelligence.Core/         intent, policy, planning, model+tool selection
│   ├── Nexus.Intelligence.Context/      ContextBundle ranking + prompt assembly
│   ├── Nexus.Intelligence.Agents/       registry, runtime, dispatcher, built-in agents
│   ├── Nexus.Intelligence.Memory/       memory, traces, results, evaluation
│   └── Nexus.Intelligence.Api/          host, /intelligence/v1
└── tests/
    ├── Nexus.Intelligence.Tests/
    └── Nexus.Intelligence.Architecture.Tests/
```

### 6.3 Nexus.Web — `C:\Personal\Nexus.Web`

```
Nexus.Web/
├── Nexus.Web.slnx
├── Directory.Build.props
├── global.json
├── nuget.config                         → C:\Personal\LocalNuGet
├── src/
│   ├── Nexus.Web.Client/                React 19 + Vite  (existing, reworked)
│   ├── Nexus.Products.Chat.Domain/      ← NexusAI.Domain (all 11 aggregates)
│   ├── Nexus.Products.Chat.Application/ ← NexusAI.Application (product use-cases)
│   ├── Nexus.Products.Chat.Infrastructure/ ← NexusAI.Infrastructure/Dataverse
│   └── Nexus.Products.Chat.Api/         ← NexusAI.Api, rebased to /api/v1
└── tests/
    ├── Nexus.Products.Chat.Tests/
    └── Nexus.Products.Chat.Architecture.Tests/
```

---

## 7. File-by-file map

### 7.1 Stays in Nexus.AI, becomes Platform

| From (`NexusAI/src/...`) | To |
|---|---|
| `NexusAI.Application/Providers/ILLMProvider.cs` | `Nexus.Platform.Contracts/Models/IModelGateway.cs` — **rewrite**, add `ModelId`, streaming |
| `NexusAI.Application/Providers/ChatRequest.cs` | `…/Models/ModelInvocation.cs` — **rewrite** |
| `NexusAI.Application/Providers/ChatResponse.cs` | `…/Models/ModelInvocationResult.cs` — **rewrite**, add usage + model used |
| `NexusAI.Application/Providers/ChatMessage.cs` | `…/Models/ModelMessage.cs` |
| `NexusAI.Infrastructure/OpenAI/OpenAIProvider.cs` | `Nexus.Platform.Providers.OpenAI/OpenAIModelGateway.cs` — **rewrite** |
| `NexusAI.Infrastructure/OpenAI/OpenAIOptions.cs` | `Nexus.Platform.Providers.OpenAI/OpenAIOptions.cs` |
| — | Everything else in Platform is **new**: catalog, routing, quota, meter, audit, identity |

### 7.2 Moves to Nexus.Int (cross-repo copy)

| From (`NexusAI/src/...`) | To (`Nexus.Int/src/...`) |
|---|---|
| `NexusAI.Application/Chat/Prompting/*` | `Nexus.Intelligence.Context/Prompting/` |
| `NexusAI.Application/Knowledge/Services/IKnowledgeRanker.cs` | `Nexus.Intelligence.Context/Ranking/` — generalise to `IContextRanker` |
| `NexusAI.Application/Knowledge/Services/KeywordKnowledgeRanker.cs` | `…/Ranking/KeywordContextRanker.cs` — operate on `ContextItem` |
| `NexusAI.Application/Planning/*` | `Nexus.Intelligence.Core/Planning/` — **rewrite** `Planner` (currently 4 hardcoded items) |
| `NexusAI.Application/Execution/*` | `Nexus.Intelligence.Core/Execution/` — **rewrite** `ExecutionEngine` (always dispatches Developer) |
| `NexusAI.Core/Agents/*` | `Nexus.Intelligence.Agents/Abstractions/` — `AgentContext` loses ProjectId/ConversationId, takes `ScopeRef` |
| `NexusAI.Infrastructure/Services/AgentRegistry.cs` | `Nexus.Intelligence.Agents/` |
| `NexusAI.Infrastructure/Services/AgentDispatcher.cs` | `Nexus.Intelligence.Agents/` |
| `NexusAI.Agents/DeveloperAgent/*` | `Nexus.Intelligence.Agents/BuiltIn/` |
| `NexusAI.Domain/Memory/*` | `Nexus.Intelligence.Memory/` — **rewrite** as `MemoryRecord` keyed by `ScopeRef` |

### 7.3 Moves to Nexus.Web (cross-repo copy)

| From (`NexusAI/src/...`) | To (`Nexus.Web/src/...`) |
|---|---|
| `NexusAI.Domain/{Workspace,Project,Conversation,ConversationMessage,Knowledge,WorkItem,Artifact,Branch,Snapshot,Session,Adr}/**` | `Nexus.Products.Chat.Domain/**` |
| `NexusAI.Domain/Common/{AggregateRoot,Entity,IRepository}.cs` | `Nexus.Products.Chat.Domain/Common/` |
| `NexusAI.Domain/Common/Identifiers/WorkspaceId.cs` | `Nexus.Products.Chat.Domain/Workspace/` |
| `NexusAI.Application/{Workspaces,Projects,Conversations,ConversationMessages,WorkItem,Knowledge,Branch,Snapshot,Session,Artifact,Adr}/**` | `Nexus.Products.Chat.Application/**` |
| `NexusAI.Application/Chat/{ChatService,IChatService}.cs` | `Nexus.Products.Chat.Application/Chat/` |
| `NexusAI.Application/Chat/Commands/SendChat/*` | `…/Chat/Commands/SendChat/` — **rewrite** to call `IIntelligenceClient` |
| `NexusAI.Application/Chat/{ConversationContext,IConversationContextProvider}.cs` | `…/Chat/Context/` — becomes the `ContextBundle` mapper |
| `NexusAI.Application/Knowledge/Services/{KnowledgeRetrievalService,IKnowledgeRetrievalService,KnowledgeContextProvider,IKnowledgeContextProvider}.cs` | `…/Knowledge/Retrieval/` — fetching from the product's own store stays with the product |
| `NexusAI.Application/Abstractions/{ICommandHandler,IQueryHandler}.cs` | `…/Application/Abstractions/` |
| `NexusAI.Infrastructure/Dataverse/**` | `Nexus.Products.Chat.Infrastructure/Dataverse/**` |
| `NexusAI.Infrastructure/Services/ConversationContextProvider.cs` | `…/Infrastructure/Context/` |
| `NexusAI.Infrastructure/{Registration,ServiceCollectionExtensions.cs}` | `…/Infrastructure/Registration/` |
| `NexusAI.Api/Endpoints/**` (all 11 groups) | `Nexus.Products.Chat.Api/Endpoints/**`, rebased to `/api/v1` |
| `NexusAI.Api/Program.cs`, `appsettings*.json`, `Properties/` | `Nexus.Products.Chat.Api/` |

### 7.4 Deleted everywhere

| Path | Reason |
|---|---|
| `NexusAI.Api/Controllers/WeatherForecastController.cs`, `WeatherForecast.cs` | Template sample, already flagged as debt |
| `NexusAI.Host/**` (the 300-line demo script) | Replaced by integration tests |
| `NexusAI.Foundation/**` | Empty placeholder |
| `NexusAI.Api/libman.json` | Unused |
| `NexusAI.Core/Abstractions/IClock.cs`, `Infrastructure/Services/SystemClock.cs` | Replaced by `TimeProvider` |
| `NexusAI.Core/Modules/INexusModule.cs` | Unused placeholder |
| `NexusAI.slnLaunch.user` | Machine-local |
| `Nexus.Web/src/features/workspaces/WorkspaceContext.tsx` | **0 bytes**, stray duplicate outside the client folder |
| `NexusAI Documentation/**/*.zip` | Nested doc ZIPs — use git tags |

---

## 8. Frontend rework — `Nexus.Web.Client`

Current state, read from disk on 2026-08-17 (branch `feature/dashboard-api-integration`):
Dashboard, Workspaces, CreateWorkspace, WorkspaceSettings, ProjectDetails, Settings and
NotFound pages are real. Products and Intelligence pages are stubs. There is **no chat UI**.

### 8.1 Defects to fix first

| Issue | Detail | Action |
|---|---|---|
| **Two HTTP paths** | `workspacesApi.ts` uses `nexusApi` (error handling, auth header support). `projectsApi.ts` uses raw `fetch` with `import.meta.env` directly — no `ApiError`, no auth, duplicated error strings. | Route everything through `ApiClient`. Convert `projectsApi.ts` to the `workspacesApi.ts` shape. |
| **Four 0-byte files** | `features/products/{Product.ts,ProductCard.tsx,productsApi.ts,useProducts.ts}` | Implement against `/api/v1/products`, or delete the feature until product #2 exists. Recommend: implement minimally, since `ProductsPage` links to it. |
| **Stray empty file** | `src/features/workspaces/WorkspaceContext.tsx` (0 bytes, outside `Nexus.Web.Client`) | Delete. The real one is at `src/Nexus.Web.Client/src/features/workspaces/`. |
| **Empty folders** | `src/hooks`, `src/styles`, `src/utils` | Populate during the rework or remove. |
| **No `node_modules`** | Dependencies not installed | `npm install` before anything |
| **Health endpoint** | `platformApi.getHealth()` calls `/health` | Under V2 that's the host's, not the product API's. Keep the path, rename the feature folder to `system` — "platform" now means something specific and this isn't it. |

### 8.2 Structural changes

| Change | Why |
|---|---|
| `ApiClient` base path → `/api/v1` | Product API is versioned |
| `.env.*` keeps exactly one variable, `VITE_NEXUS_API_URL` | The frontend must never be given an Intelligence or Platform URL. Add a comment saying so. |
| `IntelligencePage.tsx` → `InsightsPage.tsx` | The frontend must not have a page named after an internal layer it cannot see. It renders `citations`, `decisions` and `usage` that arrive *through* the product API. |
| `features/platform/` → `features/system/` | Same reason |
| `AppLayout` nav: "Intelligence" → "Insights"; subtitle "AI Platform" → "Chat" | The user is in a chatbot, not a platform |
| Add `features/chat/` | The product has no chat UI. `chatApi.ts`, `useSendChat.ts`, `ConversationList.tsx`, `MessageThread.tsx`, `ChatPanel.tsx`, `CitationsPanel.tsx` |
| Add `pages/ChatPage.tsx` + route `/projects/:projectId/conversations/:conversationId` | Where the chat lives |

### 8.3 Why the chat UI is not optional

You asked for "only changes to what we have done until now", then allowed completing the
frontend based on existing work. Here's the case for including chat in that:

Stage 5 of this migration rewires the chat turn to cross two new contracts. Without a chat
UI, the only way to verify it is Swagger — which tells you the endpoint returns 200, not
whether the assembled context produced a *good* answer. The `ContextBundle` mapper is the
single highest-risk piece of the whole migration, and a bad one degrades quality silently.
The chat UI is the instrument that measures it.

It's also the only page that makes the product a chatbot. Everything else is admin.

---

## 9. Migration stages — interleaved across three repos

Each stage ends green in the repo it touches. `📦` marks a stage that publishes a package.

| # | Repo | Work |
|---|---|---|
| **0** | all | Baseline: build, tag `pre-v2`, branch `arch/v2` in NexusAI and Nexus.Web. Create `C:\Personal\LocalNuGet`. |
| **1** | Nexus.AI | Strip to Platform: delete Foundation/Host/WeatherForecast, create the 7 Platform projects, move the OpenAI provider and provider contracts in. Everything product/intelligence-shaped is *copied out* in stages 2–3, then deleted. |
| **2** | Nexus.Int | Create the repo and solution. Copy in prompting, ranking, planning, execution, agents, memory. |
| **3** | Nexus.Web | Create the 4 .NET projects. Copy in domain, application, infrastructure, API. |
| **4** | Nexus.AI | Author `Nexus.Platform.Contracts` properly; rewrite `OpenAIProvider` → `IModelGateway` + `IModelCatalog`; implement Core (routing, quota, meter, audit). 📦 `pack-local.ps1` |
| **5** | Nexus.Int | Author `Nexus.Intelligence.Contracts`; build the turn pipeline; rewrite Planner and ExecutionEngine; stand up `/intelligence/v1`. 📦 `pack-local.ps1` |
| **6** | Nexus.Web | Namespace rewrite to `Nexus.Products.Chat.*`; rebase routes to `/api/v1`; build green against Dataverse. |
| **7** | Nexus.Web | **Rewire the chat turn.** `ChatContextBundleMapper`, `HttpIntelligenceClient`, `SendChatHandler` rewrite, `PersistenceHint` handling. Product ends with zero model references. |
| **8** | Nexus.Web | Frontend: fix the two HTTP paths, the four 0-byte files, rename Intelligence→Insights, `/api/v1`, build `features/chat/`. |
| **9** | Nexus.AI | Delete everything that moved out. `Nexus.AI` now contains Platform and nothing else. |
| **10** | all | Architecture tests in each solution, build-breaking. Update canonical docs. Tag `v2-arch` in all three. |

Stages 1–3 are pure file movement and can run back to back. Stage 4 must complete before 5
(package dependency); 5 before 7. Stage 8 can run in parallel with 4–5 if you have the
appetite.

---

## 10. Decisions taken here

| ID | Decision | Rationale |
|---|---|---|
| D-1 | Identity lives in Platform | A user is one user across Chat, Vault and ERP — a backbone concept, not a product structure. Flip before Stage 4 if you disagree. |
| D-2 | Memory belongs wholly to Intelligence | The product's Dataverse `Memory` table is retired. **Check whether it holds data you care about before Stage 2.** |
| D-3 | Platform ships as NuGet, runs in-process | §2.1. Flip to HTTP when a second consumer appears. |
| D-4 | No shared kernel | §4 |
| D-5 | `TimeProvider` replaces `IClock`/`SystemClock` | Built into .NET; one less thing to own |
| D-6 | Local file-system NuGet feed at `C:\Personal\LocalNuGet` | Zero infrastructure; swap the URL for CI later |
| D-7 | Chat UI is in scope | §8.3 — without it Stage 7 is unverifiable |
| D-8 | Cross-repo moves are copy + delete, not history-preserving | `pre-v2` tag in NexusAI retains full history. Preserving it across repos costs more than it's worth here. |
