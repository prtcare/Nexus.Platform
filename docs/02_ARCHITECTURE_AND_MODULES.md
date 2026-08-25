# Architecture and Modules

*Rewritten for V2.1. Source of truth: `NEXUS_ARCHITECTURE_V2.md`, which supersedes the
single-repo layer description this document previously contained — see ADR-011, ADR-012 and
ADR-013 in `08_DECISIONS_AND_TECHNICAL_DEBT.md`. Do not reintroduce a single-solution
description here; if V2.1 changes, edit the source document first and this one to match.*

## Architectural style — three solutions

Nexus is no longer one repository and one host. It is **three separate solutions, one per
layer**, each in its own repo:

| Solution | Repo | Contains | Deployed? |
|---|---|---|---|
| **Nexus.Platform** | `C:\Personal\Nexus.Platform` | Nexus Platform — the backbone between products and AI | No — class libraries, packaged to NuGet |
| **Nexus.Intelligence** | `C:\Personal\Nexus.Intelligence` | Nexus Intelligence — the deciding layer | Yes — HTTP API at `/intelligence/v1` |
| **Nexus.Experience** | `C:\Personal\Nexus.Experience` | The Chatbot product — React client, .NET API, domain, Dataverse | Yes — HTTP API at `/api/v1` + static client |

The rule, unchanged since V2.0: **Intelligence decides. Platform executes. Products own the
data and the experience.**

Within each solution, Clean Architecture still applies — domain-centric modeling,
command/query handlers, repository abstractions, dependency injection, thin transport — but
the boundary between solutions is now a package or network boundary, not a folder.

### Dependency edges

```
   Browser
      │  HTTPS
      ▼
┌─────────────────────────────────────────┐
│  Nexus.Experience          (deployable) │
│  ├── Nexus.Experience.Client        React │
│  ├── Nexus.Products.Chat.Api    /api/v1 │
│  ├── ...Application  ...Domain          │
│  └── ...Infrastructure  →  Dataverse    │
└──────────────────┬──────────────────────┘
                   │  HTTP  /intelligence/v1
                   │  📦 Nexus.Intelligence.Contracts
                   ▼
┌─────────────────────────────────────────┐
│  Nexus.Intelligence          (deployable) │
│  ├── Nexus.Intelligence.Api             │
│  ├── ...Core  ...Context                │
│  └── ...Agents  ...Memory               │
└──────────────────┬──────────────────────┘
                   │  in-process
                   │  📦 Nexus.Platform.*
                   ▼
┌─────────────────────────────────────────┐
│  Nexus.Platform        (libraries only) │
│  ├── Nexus.Platform.Contracts           │
│  ├── Nexus.Platform.Core                │
│  ├── Nexus.Platform.Providers.OpenAI    │
│  └── ...Tools  ...Identity  ...Persistence
└──────────────────┬──────────────────────┘
                   ▼
        OpenAI · Anthropic · connectors
```

Nexus.Experience reaches Nexus.Intelligence over HTTP, through the `Nexus.Intelligence.Contracts` package.
Nexus.Intelligence reaches Nexus.Platform **in-process**, as a `Nexus.Platform.*` NuGet package reference —
not a network call. Platform has exactly one consumer by design, so a network hop there buys
nothing; see ADR-013. The Platform contracts are shaped as if they were HTTP regardless, so
this can flip to a service later without changing callers.

### Reference rules (§2.3)

| Solution | May reference | Must never reference |
|---|---|---|
| `Nexus.Experience` | `Nexus.Intelligence.Contracts` (package) | anything `Nexus.Platform.*`, any provider SDK, any Intelligence internals |
| `Nexus.Intelligence` | `Nexus.Platform.*` (packages) | anything `Nexus.Products.*` |
| `Nexus.Platform` | vendor SDKs only | anything `Nexus.Intelligence.*` or `Nexus.Products.*` |

Plus two name-level rules:

- No type named `Workspace`, `Project`, `Conversation`, `ConversationMessage`, `Knowledge`,
  `WorkItem`, `Artifact`, `Branch`, `Snapshot`, `Session` or `Adr` may appear anywhere in
  `Nexus.Platform` or `Nexus.Intelligence`.
- `Nexus.Experience.Client` has exactly one API base URL and it points at the product API.

Because the solutions are physically separate, most of this is enforced by the package graph
— a product simply cannot reference a Platform type it hasn't installed. **The remaining case
— someone adding the wrong package — is caught by architecture tests in each solution**
(`Nexus.Platform.Architecture.Tests`, `Nexus.Intelligence.Architecture.Tests`,
`Nexus.Products.Chat.Architecture.Tests`), which fail the build if a forbidden reference or a
forbidden type name appears.

## Layer responsibilities per solution

### Nexus.Platform — the Platform

The only code in the entire system that holds a vendor SDK or a vendor credential. Small by
design; it owns no product concept.

| Project | Responsibility |
|---|---|
| `Nexus.Platform.Contracts` | `IModelCatalog`, `IModelGateway`, `IToolCatalog`/`IToolGateway`, `IIdentityService`/`ITenantResolver`/`IProductRegistry`, `IUsageMeter`/`IQuotaPolicy`/`IAuditLog`, `ISecretResolver` |
| `Nexus.Platform.Core` | Routing model gateway, aggregating model catalog, quota, metering, audit implementations |
| `Nexus.Platform.Providers.OpenAI` | The OpenAI `IModelGateway` implementation |
| `Nexus.Platform.Providers.Anthropic` | Scaffold — not yet implemented; see `08_DECISIONS_AND_TECHNICAL_DEBT.md` |
| `Nexus.Platform.Tools` | Tool registry and governed execution — scaffold |
| `Nexus.Platform.Identity` | Tenants, users, entitlements — scaffold |
| `Nexus.Platform.Persistence` | Platform-only store: tenants, usage ledger, audit log. **Not product data.** |

Platform's store holds tenants, users, products, entitlements, provider configuration, the
usage ledger and the audit log. No Workspace. No Project. No Conversation. It cannot compile
against a product type even by accident, because it never references one.

### Nexus.Intelligence — the Intelligence

Decides *what to do, where, and how*. Schema-agnostic by construction — it references no
product assembly.

| Project | Responsibility |
|---|---|
| `Nexus.Intelligence.Contracts` | The only thing products may reference: `IIntelligenceClient`, `IntelligenceTurnRequest`/`Response`, `ContextBundle`, `ScopeRef` |
| `Nexus.Intelligence.Core` | Intent classification, policy/permission gating, planning and decomposition, model/tool selection |
| `Nexus.Intelligence.Context` | `ContextBundle` ranking and prompt assembly |
| `Nexus.Intelligence.Agents` | Registry, runtime, dispatcher, built-in agents |
| `Nexus.Intelligence.Memory` | Turn traces, memories, results, evaluations — keyed by `(TenantId, ProductId, ScopeRef)`, where `ScopeRef` is an opaque string the product supplies |
| `Nexus.Intelligence.Api` | Host; exposes `/intelligence/v1` |

Intelligence can hold memory *about* a conversation without knowing what a conversation is —
`ScopeRef` is opaque to it.

### Nexus.Experience — the Chatbot product

The first product, end to end: its own UI, its own API, its own domain, its own database.

| Project | Responsibility |
|---|---|
| `Nexus.Experience.Client` | React 19 + Vite frontend; exactly one API base URL, pointed at `/api/v1` |
| `Nexus.Products.Chat.Domain` | Business concepts and invariants — aggregates, entities, strongly typed IDs, statuses, repository interfaces. Must not know Dataverse, OpenAI, Swagger, or ASP.NET Core exists. |
| `Nexus.Products.Chat.Application` | Command/query/result records, handlers, `ContextBundle` mapping (the seam to Intelligence), transaction/use-case validation. Depends on `Nexus.Intelligence.Contracts` for `IIntelligenceClient`; never on a model or vendor type. |
| `Nexus.Products.Chat.Infrastructure` | `IDataverseClient` and context, Dataverse entities, mappers, repository implementations, service registration |
| `Nexus.Products.Chat.Api` | Route definitions at `/api/v1`, request/response DTOs, HTTP status mapping, boundary validation, Swagger. Thin — calls application handlers. |

Owns Workspace, Project, Milestone, Conversation, ConversationMessage, Knowledge, ADR,
WorkItem, Artifact, Branch, Snapshot, Session — persisted to Dataverse solution `N_001_Nexus`,
publisher prefix `du_`. See `12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md` for the full model,
including which of these tables have no C# aggregate yet, and the forward note on the
Azure SQL migration (ADR-014).

Future products (Vault, ERP, Nexus Build) get their own solution, their own store — Dataverse,
SQL, document, whatever fits — and their own UI. This is precisely why Platform holds no
product structure: each product's structure will be different.

## Primary runtime flows

### Standard command / query (within Nexus.Experience)

`HTTP request → Endpoint → Command/Query handler → Domain/repository → Dataverse → HTTP response`

Unchanged from before V2.1 — this flow never leaves the Chat product.

### A chat turn, end to end (crosses all three solutions)

```
 1. Browser              POST /api/v1/chat { conversationId, prompt }
 2. Nexus.Experience  Chat.Api → Chat.Application: SendChatHandler
 3.                      persist user message              → Dataverse
 4.                      load history, knowledge, ADRs, project objective
 5.                      map to ContextBundle                ← THE SEAM: product schema dies here
 6.                      POST /intelligence/v1/turns   (HTTP, via IIntelligenceClient)
 7. Nexus.Intelligence    classify intent
 8.                      policy gate on Actor.Permissions + Constraints
 9.                      rank + trim ContextBundle by relevance × trust
10.                      select agent
11.                      IModelCatalog.ListAsync → choose model
12.                      assemble prompt to fit the chosen context window
13.                      IModelGateway.InvokeAsync    (in-process, Platform package)
14. Nexus.Platform      IQuotaPolicy.CheckAsync
15.                      resolve credential, call vendor SDK, retry/timeout
16.                      IUsageMeter.Record + IAuditLog.Append
17.                      return normalised result
18. Nexus.Intelligence    optional tool loop (approval-gated)
19.                      write turn trace + memory   → Intelligence store
20.                      reply + citations + decisions + persistenceHints
21. Nexus.Experience     persist assistant message     → Dataverse
22.                      apply persistenceHints (e.g. Knowledge candidate, pending approval)
23.                      map to product DTO
24. Browser              ← { reply, citations, usage }
```

Steps 5 and 20 are the entire boundary. If a future change makes Intelligence want to read
Dataverse directly, that's a signal `ContextBundle` is the wrong shape — fix the shape, never
the boundary. Full detail: `NEXUS_ARCHITECTURE_V2.md` §5.

### Agent execution (within Nexus.Intelligence)

`Turn → Planner → Agent registry/dispatcher → Selected agent → Governed tools (via Nexus.Platform.Tools) → Result → memory + trace`

## Repository and mapper pattern (Nexus.Experience / Dataverse)

- Domain repositories expose domain concepts, not Dataverse query syntax.
- A Dataverse entity class holds logical column mapping.
- A mapper performs both directions and handles missing optional fields safely.
- List repositories filter server-side wherever possible.
- Unknown Dataverse choice values must not crash the entire query; define a deliberate fallback or validation policy.
- Strongly typed IDs are converted only at boundaries.

This pattern is specific to `Nexus.Products.Chat.Infrastructure`. Neither Nexus.Platform nor
Nexus.Intelligence touches Dataverse — Platform's store is its own small `Nexus.Platform.Persistence`,
and Intelligence's store is `Nexus.Intelligence.Memory`.

## Current feature coverage (Nexus.Experience / Chat product)

The feature set is unchanged by the V2.1 restructure — only its location moved, from
`NexusAI.*` to `Nexus.Products.Chat.*`.

| Feature | Current coverage |
|---|---|
| Workspace | Domain, application, Dataverse, create/get/list/update API |
| Project | Domain, application, Dataverse, create/get/list/update API |
| Conversation | Domain, application, Dataverse, create/get/list/update API |
| Conversation Message | Domain, Dataverse, list API; creation through Chat |
| Work Item | Domain, application, Dataverse, create/get/list/update API |
| Knowledge | Domain, application, Dataverse, create/get/list API |
| Branch | Domain, application, Dataverse, create/get/list/update API |
| Snapshot | Domain, application, Dataverse, create/get/list/update API |
| Session | Domain, application, Dataverse, create/get/list/update API |
| Artifact | Domain, application, Dataverse, create/get/list/update API |
| ADR | Domain/repository and create application path; no public API found |
| Project Milestone | Planned; no current implementation found — see `12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md` for the full list of unmodelled tables |

Memory is no longer part of this table — it moved to `Nexus.Intelligence.Memory` and is not a
Chat product feature under V2.1.

## Composition — resolved

*Previously "Composition decision still required": the repository held both `NexusAI.Api` and
`NexusAI.Host`, with no chosen canonical entry point.*

This is resolved by the V2.1 split itself, not by a later decision. `NexusAI.Host` (the
300-line demo script) was deleted — replaced by integration tests — and `NexusAI.Foundation`
(an empty placeholder) was deleted with it. Each deployable solution now has exactly one host:

- `Nexus.Products.Chat.Api` is the canonical entry point for Nexus.Experience.
- `Nexus.Intelligence.Api` is the canonical entry point for Nexus.Intelligence.
- Nexus.AI has no host — it is a library set, consumed in-process.

## Testing architecture

Add automated projects aligned to behavior, not merely layers, per solution:

- Domain unit tests for invariants and enum/state transitions.
- Application handler tests with repository/provider fakes.
- Mapper tests for every Dataverse column and missing optional values (Nexus.Experience only).
- Repository integration tests against the development/test environment.
- API contract tests for routes, status codes, validation, and JSON.
- End-to-end tests for the first frontend journey.
- **Architecture tests**, build-breaking, enforcing the reference rules and name-level rules
  in §2.3 above: `Nexus.Platform.Architecture.Tests`, `Nexus.Intelligence.Architecture.Tests`,
  `Nexus.Products.Chat.Architecture.Tests`. These exist specifically so a forbidden package or
  a forbidden type name fails CI rather than surviving to review.
