# Naming Standards

> **SUPERSEDED NUMBERING NOTICE (2026-09-05):** This document's repository/layer
> table and milestone-ID worked examples (e.g. §1-3, the repository table, the
> schema table, and the `M-08-1.1` reading example) are built on the v2.1 twelve-
> layer model, in which 07 DEVELOPER and 12 PRODUCTS were numbered Platform
> layers. Per the approved v2.2 renumbering (`LAYER_MODEL.md` §2.2, §4a), Nexus
> Forge, Nexus Developer (the product), and Products all now sit OUTSIDE the ten
> numbered Platform layers, and DELIVERY/ASSURANCE/OPERATIONS/EXPERIENCE are
> renumbered 07/08/09/10. The naming rules themselves (casing, "no layer numbers
> in code", the ID patterns) remain valid and are not being discarded. Re-deriving
> the repository table, schema table, and worked examples against the v2.2
> numbering is Wave-D-adjacent decision work and is explicitly NOT done in this
> batch.

> **Status:** CURRENT for everything that exists; TRANSITION or TARGET is marked per category
> **Owner:** Layer 07 DEVELOPER (definition) / Layer 09 ASSURANCE (conformance)
> **Last updated:** 2026-08-21
> **Layer:** Cross-cutting
> **Authoritative for:** the name of every artefact in Nexus — repositories, solutions, projects, namespaces, folders, types, members, database objects, routes, frontend files, tests, branches, tags, builds, work-graph identifiers and documents

Every example below is a real name taken from the Nexus repositories or the roadmap. Where a category has no real example, the section says so rather than inventing one.

How rules differ per language is **CODE_CONVENTIONS.md**. C#-specific construction rules are **CSHARP_STANDARDS.md**; frontend ones are **TYPESCRIPT_REACT_STANDARDS.md**; database structure is **DATABASE_STANDARDS.md**. This document names things; those documents shape them.

---

## Principles

1. **A name states what a thing is, not how it was built.** `SqlWorkspaceRepository` says both, and that is correct, because the storage technology is the distinguishing fact between two implementations of `IWorkspaceRepository`.
2. **The dominant form wins.** Where the codebase is inconsistent, this document names the majority form as the standard and marks the minority as drift to be corrected.
3. **Mirror structure.** Namespace mirrors folder. Table name mirrors class name. Endpoint file mirrors route. A reader who knows one location knows the other.
4. **No abbreviations except established ones.** `Id`, `Api`, `Sql`, `Adr`, `Ref`, `Seq`, `Dto` are established. Nothing new joins that list without an ADR.
5. **No layer numbers in code.** The 12 layers are numbered in documentation (`01 CORE` … `12 PRODUCTS`). Code uses names: `Nexus.Platform.Core`, schema `core`. The Dataverse-era `T_nnn_` table prefixes are gone and never return.
6. **No product name in shared code.** `Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never reference product types. This is enforced by `PlatformBoundaryTests.cs` and `BoundaryRuleTests.cs`, and a name that violates it is the earliest visible symptom.

## Casing at a glance

| Artefact | Case | Real example |
|---|---|---|
| Repository, solution, project, namespace | `PascalCase.Dotted` | `Nexus.Products.Chat.Infrastructure` |
| C# type, member, constant | `PascalCase` | `RoutingModelGateway`, `Restore` |
| C# local, parameter, field | `camelCase` (fields `_camelCase`) | `workspaceId` |
| C# interface | `I` + `PascalCase` | `IModelGateway` |
| Folder (C#) | `PascalCase` | `Nexus.Intelligence.Core/Turns/` |
| Folder (frontend) | `lowercase` | `src/features/workspaces/` |
| React component file | `PascalCase.tsx` | `ChatPanel.tsx` |
| Hook file | `use` + `PascalCase.ts` | `useConversationMessages.ts` |
| Other TypeScript module | `camelCase.ts` | `workspacesApi.ts` |
| Database schema | `lower_snake_case` | `product_core` |
| Table, column | `PascalCase` (matches the C# name) | `[org].[Workspace]`, `Ref` |
| Route segment | `lowercase-plural` | `/api/v1/workspaces` |
| Environment variable | `SCREAMING_SNAKE_CASE` | `VITE_API_BASE_URL` |
| Git branch | `lowercase/slashed` | `work/WI-02-1.2.1-a` |
| Document file | `SCREAMING_SNAKE_CASE.md` | `NAMING_STANDARDS.md` |
| Roadmap identifier | `<letter>-<layer>-<path>` | `M-07-2.2` |

---

## 1. Repositories

| Aspect | Rule |
|---|---|
| Pattern | `Nexus.<Capability>` for a platform repository, `Nexus.Products.<Name>` for a product |
| Remote name | Must equal the local directory name |
| Case | PascalCase, dot-separated. No hyphens, no lowercase. |

**CURRENT — three repositories exist; their naming was inconsistent until the 2026-08-24 rename.**

| Local path | Remote | Solution |
|---|---|---|
| `C:\Personal\NexusAI` | `github.com/prtcare/NexusAI` | `Nexus.AI.slnx` |
| `C:\Personal\Nexus.Int` | `github.com/prtcare/Nexus-Int` | `Nexus.Int.slnx` |
| `C:\Personal\Nexus.Web` | `github.com/prtcare/Nexus-web` | `Nexus.Web.slnx` |
| `C:\Personal\LocalNuGet` | — (not a git repository) | — |

Three different conventions in three repositories: `NexusAI` (no separator), `Nexus-Int` (hyphen, title case), `Nexus-web` (hyphen, lowercase). None matches the pattern above.

**DONE — the names the repositories moved to on 2026-08-24.**

| Target repository | Becomes what | Layers it houses |
|---|---|---|
| `Nexus.Platform` | NexusAI renamed | 01 CORE, 02 DATA, 03 GOVERNANCE, 05 AUTOMATION, 06 PRODUCT CORE, 08 DELIVERY, 09 ASSURANCE, 10 OPERATIONS |
| `Nexus.Intelligence` | Nexus.Int renamed | 04 AI |
| `Nexus.Experience` | Nexus.Web renamed | 11 EXPERIENCE |
| `Nexus.Developer` | new | 07 DEVELOPER |
| `Nexus.Products.<Name>` | new, one per product | 12 PRODUCTS |

**Rule for the transition.** A rename touches remotes, `nuget.config`, package identities and every consumer. It happens once, as its own work item, and both the local directory and the remote change in the same change. Given that all three repositories lost `.git\objects` on 2026-08-20 and `.git-broken\` still sits in each of them, push before starting and verify the remote after finishing.

---

## 2. Solutions

| Aspect | Rule |
|---|---|
| Pattern | `<RepositoryName>.slnx`, at the repository root, one per repository |
| Format | `.slnx` — the XML solution format. Do not add `.sln` files. |

**CURRENT:** `Nexus.Platform.slnx`, `Nexus.Intelligence.slnx`, `Nexus.Experience.slnx`.

`Nexus.AI.slnx` sat in `NexusAI` and contained `Nexus.Platform.*` projects — the solution name matched neither the directory nor its contents. It was renamed with the repository (2026-08-24).

---

## 3. Projects

| Aspect | Rule |
|---|---|
| Pattern | `Nexus.<Product-or-capability>.<Role>` |
| Location | Always under `src/`; test projects under `tests/` |
| Assembly and root namespace | Equal the project name. Never overridden. |

**Role suffixes, and what each means.**

| Suffix | Contains | Real example |
|---|---|---|
| `.Contracts` | Interfaces and DTOs only. No implementation, no dependency on any sibling. | `Nexus.Platform.Contracts`, `Nexus.Intelligence.Contracts` |
| `.Core` | Default implementations of its own Contracts. | `Nexus.Platform.Core`, `Nexus.Intelligence.Core` |
| `.Domain` | Aggregates, entities, value objects, repository interfaces. No infrastructure. | `Nexus.Products.Chat.Domain` |
| `.Application` | Use-case orchestration over Domain. | `Nexus.Products.Chat.Application` |
| `.Infrastructure` | Persistence and external-system adapters. | `Nexus.Products.Chat.Infrastructure` |
| `.Api` | HTTP host. `Program.cs` lives here. | `Nexus.Products.Chat.Api`, `Nexus.Intelligence.Api` |
| `.Providers.<Vendor>` | One external vendor adapter. | `Nexus.Platform.Providers.OpenAI`, `Nexus.Platform.Providers.Anthropic` |
| `.<Capability>` | A focused capability library. | `Nexus.Intelligence.Context`, `.Memory`, `.Agents`, `Nexus.Platform.Identity`, `.Persistence`, `.Tools` |
| `.Client` | A frontend or client application. | `Nexus.Experience.Client` |
| `.Tests` | Behaviour tests. | `Nexus.Intelligence.Tests` |
| `.Architecture.Tests` | NetArchTest boundary rules. | `Nexus.Platform.Architecture.Tests` |

**Two naming defects to be aware of.**

`Nexus.Web.Client` did not match its siblings — everything else in that repository is `Nexus.Products.Chat.*`. It became `Nexus.Experience.Client` when the repository was renamed (2026-08-24).

Eight empty gitignored husks exist in NexusAI: `NexusAI.Agents`, `NexusAI.Api`, `NexusAI.Application`, `NexusAI.Core`, `NexusAI.Domain`, `NexusAI.Foundation`, `NexusAI.Host`, `NexusAI.Infrastructure`. They use a dead `NexusAI.*` prefix. They are not projects, they are residue. Never add a file to one.

---

## 4. Namespaces

**Rule: the namespace equals the project name plus the folder path, exactly.** No exceptions anywhere in the codebase.

| File | Namespace |
|---|---|
| `Nexus.Products.Chat.Domain/Workspace/Workspace.cs` | `Nexus.Products.Chat.Domain.Workspace` |
| `Nexus.Intelligence.Core/Turns/TurnPipeline.cs` | `Nexus.Intelligence.Core.Turns` |
| `Nexus.Platform.Contracts/Models/IModelGateway.cs` | `Nexus.Platform.Contracts.Models` |
| `Nexus.Intelligence.Context/Ranking/KeywordContextRanker.cs` | `Nexus.Intelligence.Context.Ranking` |
| `Nexus.Products.Chat.Infrastructure/Sql/Repositories/SqlWorkspaceRepository.cs` | `Nexus.Products.Chat.Infrastructure.Sql.Repositories` |

File-scoped namespace declarations — see CSHARP_STANDARDS.md.

**Note the collision this convention creates and accepts:** `Nexus.Products.Chat.Domain.Workspace` is both a namespace and a type. That is a consequence of one folder per aggregate and it is accepted, not worked around. Do not rename the folder to `Workspaces` to dodge it — the folder-per-aggregate rule is stronger.

---

## 5. Folders

| Context | Case | Rule |
|---|---|---|
| C# project | `PascalCase` | Becomes a namespace segment, so it obeys type-name rules. |
| Frontend `src/` | `lowercase` | Never becomes an identifier. |

**Backend folder vocabulary in use.**

| Folder | Role | Where |
|---|---|---|
| `<AggregateName>/` | One folder per aggregate | `Nexus.Products.Chat.Domain/Workspace/` |
| `Common/` | Shared base types | `Nexus.Products.Chat.Domain/Common/` — `AggregateRoot`, `Entity`, `IRepository` |
| `Endpoints/` | HTTP endpoint files | both API projects |
| `Sql/`, `Configurations/`, `Conventions/`, `Repositories/`, `Migrations/` | EF Core layout | `Nexus.Products.Chat.Infrastructure/Sql/` |
| `Governance/`, `Identity/`, `Models/`, `Secrets/`, `Tools/` | Contract families | `Nexus.Platform.Contracts/` |
| `Turns/`, `Context/`, `Results/`, `Client/` | Contract families | `Nexus.Intelligence.Contracts/` |
| `Planning/`, `Execution/`, `Ranking/`, `Prompting/` | Capability groupings | `Nexus.Intelligence.Core/`, `.Context/` |
| `Abstractions/`, `BuiltIn/` | Interfaces vs supplied implementations | `Nexus.Intelligence.Agents/` |
| `Tooling/`, `ResultReports/`, `DependencyInjection/` | Host wiring | `Nexus.Intelligence.Api/` |

**The aggregate folder is a fixed shape.** Every one of the 11 aggregates in `Nexus.Products.Chat.Domain` contains exactly:

```
<Name>/
  <Name>.cs               aggregate root
  <Name>Id.cs             strongly-typed identifier
  <Name>Status.cs         status enum
  I<Name>Repository.cs    repository interface
```

Real instances: `Adr`, `Artifact`, `Branch`, `Conversation`, `ConversationMessage`, `Knowledge`, `Project`, `Session`, `Snapshot`, `WorkItem`, `Workspace`.

**Frontend folder vocabulary.**

| Folder | Role |
|---|---|
| `api/` | Transport layer — `ApiClient.ts`, `ApiError.ts` |
| `app/` | Application composition — `AppProviders.tsx`, `queryClient.ts` |
| `components/` | Cross-feature reusable components |
| `config/` | `environment.ts` |
| `features/<feature>/` | One folder per feature: `chat`, `projects`, `workspaces`, `system` |
| `layouts/`, `pages/`, `routes/`, `types/` | Shell, screens, routing table, shared types |

---

## 6. Classes

| Aspect | Rule |
|---|---|
| Case | `PascalCase`, noun or noun phrase |
| Suffix | States the role; the prefix states the variant |
| Forbidden | `Manager`, `Helper`, `Util`, `Service` as a bare suffix, and any `Base` prefix |

**Suffix vocabulary observed in the codebase.** Reuse these; do not invent parallel ones.

| Suffix | Meaning | Real examples |
|---|---|---|
| `Repository` | Persists one aggregate | `SqlWorkspaceRepository` |
| `Gateway` | Calls an external system | `RoutingModelGateway`, `AnthropicModelGateway` (stub) |
| `Catalog` | Enumerates what is available | `AggregatingModelCatalog`, `EmptyToolCatalog` |
| `Store` | Holds records for retrieval | `InMemoryMemoryStore`, `InMemoryTurnTraceStore`, `InMemoryResultReportStore`, `PlatformStore` (stub) |
| `Registry` | Holds registrations | `AgentRegistry` |
| `Dispatcher` | Routes to a handler | `AgentDispatcher` |
| `Selector` | Chooses one from many | `ContextSelector`, `AgentSelector`, `ModelSelector` |
| `Classifier` | Assigns a category | `IntentClassifier` |
| `Ranker` | Orders by score | `KeywordContextRanker` |
| `Assembler` / `Composer` | Builds a composite | `PromptAssembler`, `ResponseComposer` |
| `Pipeline` / `Engine` / `Loop` / `Step` | Execution machinery | `TurnPipeline`, `ExecutionEngine`, `ToolLoop`, `PromptStep`, `ModelStep` |
| `Gate` | Permits or denies | `PolicyGate` |
| `Configuration` | EF Core `IEntityTypeConfiguration<T>` | `WorkspaceConfiguration` |
| `DbContext` / `DbContextFactory` | EF Core context and design-time factory | `NexusChatDbContext`, `NexusChatDbContextFactory` |
| `Converters` | A static class of value converters | `StronglyTypedIdConverters` |
| `Endpoint` | HTTP endpoint file | `HealthEndpoint` |
| `Module` | Host composition root for a product | `ChatProductModule` |
| `Log` / `Meter` / `Policy` | Governance primitives | `ConsoleAuditLog`, `InMemoryUsageMeter`, `PermissiveQuotaPolicy` |
| `Agent` | An agent implementation | `DeveloperAgent` (stub) |
| `Planner` | Produces a plan | `Planner` |

**Prefix vocabulary — the prefix names the variant, and this is load-bearing.**

| Prefix | Meaning | Real examples |
|---|---|---|
| `Sql` | Backed by Azure SQL via EF Core | `SqlWorkspaceRepository` |
| `InMemory` | Non-durable; a placeholder with a durability milestone | `InMemoryUsageMeter`, `InMemoryMemoryStore`, `InMemoryTurnTraceStore`, `InMemoryResultReportStore` |
| `Console` | Writes to console; development only | `ConsoleAuditLog` |
| `Empty` | Deliberate no-op | `EmptyToolCatalog`, `EmptyToolGateway` |
| `Permissive` | Allows everything; not a real policy | `PermissiveQuotaPolicy` |
| `Routing` | Delegates to a chosen implementation | `RoutingModelGateway` |
| `Aggregating` | Composes several sources | `AggregatingModelCatalog` |
| `Keyword` | The algorithm used | `KeywordContextRanker` |
| `<Vendor>` | Vendor adapter | `AnthropicModelGateway` |

`InMemory`, `Console`, `Empty` and `Permissive` are **honest names for temporary things**, and that is why they must never be dropped. `InMemoryUsageMeter` becomes `SqlUsageMeter` at **M-01-4.2 Durable usage metering**; it does not become `UsageMeter`. A name that hides its own impermanence is the defect these prefixes prevent.

---

## 7. Interfaces

| Aspect | Rule |
|---|---|
| Pattern | `I` + the noun the implementations are variants of |
| Never | `IWorkspaceRepositoryInterface`, `IWorkspaceRepositoryBase`, or an `I`-prefixed abstract class |
| Placement | The interface lives with the abstraction, not the implementation |

| Interface | Where it lives | Why there |
|---|---|---|
| `IModelGateway`, `IModelCatalog` | `Nexus.Platform.Contracts/Models/` | Cross-repository contract |
| `IAuditLog`, `IQuotaPolicy`, `IUsageMeter` | `Nexus.Platform.Contracts/Governance/` | Cross-repository contract |
| `IIdentityService`, `IProductRegistry`, `ITenantResolver` | `Nexus.Platform.Contracts/Identity/` | Cross-repository contract |
| `ISecretResolver` | `Nexus.Platform.Contracts/Secrets/` | Cross-repository contract |
| `IToolCatalog`, `IToolGateway` | `Nexus.Platform.Contracts/Tools/` | Cross-repository contract |
| `IModelCatalogSource`, `INamedModelGateway` | `Nexus.Platform.Core/Models/` | Extension points *of* Core, not of the platform |
| `IIntelligenceClient` | `Nexus.Intelligence.Contracts/Client/` | The consumer-facing contract |
| `IContextRanker`, `IPromptAssembler` | `Nexus.Intelligence.Context/` | Internal to that capability |
| `IMemoryStore` | `Nexus.Intelligence.Memory/` | Internal to that capability |
| `IAgent`, `IAgentRegistry`, `IAgentDispatcher`, `IAgentRuntime` | `Nexus.Intelligence.Agents/Abstractions/` | Internal contract family |
| `IResultReportStore` | `Nexus.Intelligence.Api/ResultReports/` | Host-local |
| `IRepository`, `I<Name>Repository` | `Nexus.Products.Chat.Domain/Common/` and each aggregate folder | The domain owns its persistence contract |

Two prefix conventions carry meaning: `INamed<X>` means an `X` resolved by name (`INamedModelGateway`), and `I<X>Source` means a contributor to an aggregating `X` (`IModelCatalogSource`).

---

## 8. Records

| Aspect | Rule |
|---|---|
| Case | `PascalCase`, noun phrase, no `Record` or `Dto` suffix |
| When | Data with no identity — see CSHARP_STANDARDS.md for the class-vs-record decision |

**Naming families observed.**

| Family | Grammar | Real examples |
|---|---|---|
| Request / response pair | `<Verb><Name>Request` / `<Verb><Name>Response` | `CreateWorkspaceRequest` / `CreateWorkspaceResponse`, `UpdateProjectRequest`, `GetKnowledgeResponse`, `ListConversationsResponse` |
| Turn protocol | `<Domain><Concept>` | `IntelligenceTurnRequest`, `IntelligenceTurnResponse`, `TurnConstraints`, `TurnInput`, `TurnError` |
| Reference to something elsewhere | `<Thing>Ref` | `ScopeRef`, `ActorRef` |
| Descriptor of a capability | `<Thing>Descriptor` | `ModelDescriptor`, `ToolDescriptor` |
| A single act | `<Thing>Invocation` | `ModelInvocation`, `ToolInvocation` |
| Outcome of an act | `<Thing>Result` / `<Thing>Report` / `<Thing>Verdict` | `ToolResult`, `AgentResult`, `ResultReport`, `QuotaVerdict` |
| Measurement | `<Thing>Usage` / `<Thing>Summary` / `<Thing>Record` | `ModelUsage`, `UsageSummary`, `UsageRecord`, `MemoryRecord`, `AuditEntry` |
| Context payload | `Context<Thing>` / `<Thing>Bundle` / `<Thing>Item` | `ContextBundle`, `ContextItem`, `RankedContextItem` |
| Query object | `<Thing>Query` | `MemoryQuery` |
| Options | `<Thing>Options` | `RankingOptions` |
| Assembled output | `Assembled<Thing>` | `AssembledPrompt` |
| Trace and plan | `<Thing>Trace` / `<Thing>Step` / `Proposed<Thing>` | `DecisionTrace`, `PlanStep`, `ProposedAction` |
| Resolved value | `Resolved<Thing>` | `ResolvedIdentity` |
| Hint | `<Thing>Hint` | `PersistenceHint` |
| Payload | `<Thing>Payload` | `ReplyPayload` |
| Strongly-typed id | `<Name>Id` | `WorkspaceId`, `ConversationMessageId`, `WorkItemId` |

`Ref` in `ScopeRef`/`ActorRef` (a reference to an entity outside the caller's knowledge) is unrelated to the database `Ref` column in §18. Both are established; keep them apart in prose.

---

## 9. Enums

| Aspect | Rule |
|---|---|
| Type name | `PascalCase` singular. Never a plural, never an `Enum` suffix. |
| Member names | `PascalCase`, no type-name prefix — `TrustLevel.Verified`, never `TrustLevel.TrustLevelVerified` |
| Suffix vocabulary | `Status` = lifecycle state of an aggregate. `Kind` = discriminator of a shape. `Type` = category of a thing. `Level` = ordered scale. `Class` = classification with consequences. |

| Enum | Suffix | Where |
|---|---|---|
| `<Name>Status` — one per aggregate, 11 of them | `Status` | each aggregate folder in `Nexus.Products.Chat.Domain` |
| `ContextItemKind`, `PersistenceHintKind`, `MemoryKind` | `Kind` | Intelligence Contracts / Memory |
| `AgentType` | `Type` | `Nexus.Intelligence.Agents/Abstractions/` |
| `TrustLevel` | `Level` | `Nexus.Intelligence.Contracts/Context/` |
| `SideEffectClass` | `Class` | `Nexus.Platform.Contracts/Tools/` |
| `ResultOutcome` | — | `Nexus.Intelligence.Contracts/Results/` |

Enum-to-database mapping is a value converter in `StronglyTypedIdConverters.cs` and lives only in Infrastructure — see DATABASE_STANDARDS.md.

---

## 10. Methods (C#)

| Aspect | Rule |
|---|---|
| Case | `PascalCase`, verb or verb phrase |
| Async | `Async` suffix on every method returning `Task`/`ValueTask` — `GetByIdAsync`, `SaveAsync` |
| Booleans | `Is`, `Has`, `Can`, `Should` prefix |
| Factory on a type | `Create` for new, `Restore` for rehydration, `From`/`To` for conversion |

**Named patterns that exist in the code and must be followed exactly.**

| Pattern | Signature | Where |
|---|---|---|
| Endpoint registration | `Map<Name>Endpoints(this IEndpointRouteBuilder app)` | every file in both `Endpoints/` folders |
| Domain rehydration | `public static <Name> Restore(...)`, paired with a private constructor | every aggregate root, e.g. `Workspace.Restore(...)` |
| DI registration | `Add<Area>(this IServiceCollection services)` | `IntelligenceServiceCollectionExtensions` |

`Create` and `Restore` are distinct and the distinction matters: `Create` applies invariants and produces a new aggregate; `Restore` reconstructs one that already exists and applies none. Never merge them.

---

## 11. Functions (TypeScript)

| Aspect | Rule |
|---|---|
| Case | `camelCase`, verb phrase |
| Async | **No `Async` suffix** — the C# rule does not cross over. `await sendChat(...)`, not `sendChatAsync(...)`. |
| API-client functions | `<verb><Noun>` inside `<feature>Api.ts` — `fetchConversations`, `createWorkspace`, `updateProject` |
| Predicates | `is`/`has`/`can` prefix |
| React components | Exempt — components are `PascalCase` functions, §29 |
| Hooks | Exempt — hooks are `use`-prefixed, §30 |

The functions in `citationTargets.ts` and `chatApi.ts` follow this; the file itself is `camelCase.ts` per §31.

---

## 12. Variables

| Language | Local / parameter | Private field | Notes |
|---|---|---|---|
| C# | `camelCase` | `_camelCase` | No Hungarian notation. No `var` for a name that would otherwise be obvious. |
| TypeScript | `camelCase` | `camelCase` | `#private` only where genuine encapsulation is needed. |
| React state | `camelCase` + `set<Name>` | — | `const [conversationId, setConversationId] = useState(...)` |

**Rules that apply everywhere.**

- The name says what the value *is*, not its type: `workspaceId`, not `guidValue`.
- A strongly-typed id variable takes the aggregate's name: `workspaceId` is a `WorkspaceId`, and where a raw `Guid` is genuinely required the name says so — `workspaceIdValue`.
- Single letters only for loop indices and lambda parameters in a one-line expression.
- Collections are plural: `conversations`, `rankedItems`. A singular name holding a collection is a defect.
- Booleans read as an assertion: `hasCitations`, `isStreaming`.
- No counter-example to any of the above was found in the codebase, so these are recorded as the standard rather than as a correction.

## 13. Constants

| Language | Rule | Notes |
|---|---|---|
| C# | `PascalCase` for `const` and `static readonly`, public or private alike | `SCREAMING_SNAKE_CASE` is not C# style and appears nowhere |
| TypeScript | `camelCase` for module-scoped values; `SCREAMING_SNAKE_CASE` only for a true compile-time literal constant | `queryClient` in `app/queryClient.ts` is a module value, not a constant |

Route strings, query keys and header names are declared as constants, never repeated as literals. Query-key grammar is TYPESCRIPT_REACT_STANDARDS.md.

---

## 14. Configuration keys

| Aspect | Rule |
|---|---|
| Shape | Hierarchical `<Section>:<Key>`, PascalCase both sides, bound to a `<Name>Options` record |
| Section name | The capability, not the technology: `Intelligence`, `Models`, `Persistence` — not `EntityFramework` |
| Binding | Section name is a `const` on the options type. Never a string literal at two call sites. |
| Secrets | Never a configuration key. `set-openai-key.ps1` handles the OpenAI key today; **TARGET: `ISecretResolver` — M-01-5.1 Real secret resolver.** |

**Not verified.** No specific configuration key was read from an `appsettings` file. The rules above are the standard; the actual key inventory is unknown and must be catalogued. `ConnectionStrings` is the ASP.NET Core framework convention and is used as framework-defined, not as a Nexus choice.

**TARGET:** **M-03-6.1 Configuration registry** in GOVERNANCE becomes the register of what keys exist.

## 15. Environment variables

| Scope | Pattern | Real example |
|---|---|---|
| Frontend | `VITE_<AREA>_<NAME>`, SCREAMING_SNAKE_CASE | `VITE_API_BASE_URL` — the `VITE_` prefix is verified in `config/environment.ts` |
| Backend | `<AREA>__<KEY>` — double underscore is the framework's section separator | `ASPNETCORE_ENVIRONMENT` is framework-defined |

**The `VITE_` prefix is a security boundary, not a style choice.** Only variables carrying it are inlined into the client bundle. Anything without it is invisible to the browser — which means anything secret must not carry it. Every frontend environment variable is read in exactly one place, `config/environment.ts`, and nowhere else reads `import.meta.env`. See TYPESCRIPT_REACT_STANDARDS.md.

---

## 16. Database schemas

| Aspect | Rule |
|---|---|
| Case | `lower_snake_case` |
| Name | The layer's short name, lowercased |
| Purpose | **Schemas replace prefixes.** The Dataverse-era `T_nnn_` table numbering is gone permanently. |

| Layer | Schema | Layer | Schema |
|---|---|---|---|
| 01 CORE | `core` | 07 DEVELOPER | `developer` |
| 02 DATA | `data` | 08 DELIVERY | `delivery` |
| 03 GOVERNANCE | `governance` | 09 ASSURANCE | `assurance` |
| 04 AI | `ai` | 10 OPERATIONS | `operations` |
| 05 AUTOMATION | `automation` | 11 EXPERIENCE | `experience` |
| 06 PRODUCT CORE | `product_core` | 12 PRODUCTS | own database per product |

`product_core` is the only two-word schema and shows the separator.

**CURRENT vs TARGET.** The shipped migration `20260820180802_InitialSqlSchema.cs` created its tables in schema **`org`**, which is not in the table above. The layer-schema convention is **M-02-1.5 Layer schema convention**. Until then, `org` is what runs and `[org].[Workspace]` is a correct reference. New work should not add further tables to `org` without knowing they will move.

Physical strategy: one `NexusPlatform` database holding a schema per layer, plus one database per product. Naming a schema per layer inside one database is what allows a layer to be split into its own database later without renaming anything.

## 17. Tables

| Aspect | Rule |
|---|---|
| Name | **The C# class name, verbatim.** `Workspace` → `[Workspace]`. Singular, PascalCase. |
| No pluralisation | The EF Core pluralising convention is not used. |
| No prefix | No `tbl`, no `T_nnn_`. The schema carries the grouping. |
| Join tables | `<Left><Right>` in the owning aggregate's schema, e.g. a workspace-to-project link table would be `WorkspaceProject`. No such table is verified to exist. |

Verified in production SQL: `INSERT INTO [org].[Workspace] (...)`.

Eleven table names follow from the eleven aggregates: `Adr`, `Artifact`, `Branch`, `Conversation`, `ConversationMessage`, `Knowledge`, `Project`, `Session`, `Snapshot`, `WorkItem`, `Workspace`. Only `Workspace` has an EF configuration and a migration today.

## 18. Columns

| Aspect | Rule |
|---|---|
| Name | The C# property name, verbatim. PascalCase. |
| No prefixes | No table-name prefix, no type prefix. |
| Foreign keys | `<ReferencedAggregate>Id` — `WorkspaceId` on `Project`. |
| Booleans | `Is<X>` / `Has<X>`. |
| Timestamps | `<Verb>edAt` for instants (`CreatedAt`, `UpdatedAt`), `<Verb>edBy` for actors. `DateTimeOffset` — see CODE_CONVENTIONS.md. |

**The three reserved column names.** Every aggregate table has these, and no column anywhere else may use these names for anything else.

| Column | Type | Role |
|---|---|---|
| `Id` | `uniqueidentifier`, primary key | The identity. Generated in C# as a strongly-typed id. |
| `Seq` | `int IDENTITY(1,1)`, EF Core shadow property | Allocation only. Never a key, never exposed, never used for ordering with business meaning. |
| `Ref` | computed, **PERSISTED**, unique | The human-readable reference. |

`Ref` is computed **in the database**, and this is the whole point: only the database guarantees uniqueness under concurrent insert. The proven expression is

```sql
('WKS-' + RIGHT('00000000' + CAST([Seq] AS varchar(8)), 8))
```

producing `WKS-00000001`. Verified in `api_run.log` on 2026-08-20 at 18:09 UTC: two successive `INSERT ... OUTPUT INSERTED.[Ref], INSERTED.[Seq]` statements each returned server-generated values.

**Ref prefix registry.** Three uppercase letters, unique across the whole system, chosen from the aggregate name. `WKS` for `Workspace` is the only one that exists and the only one proven. Every other aggregate needs a prefix allocated when it is migrated, and allocation must be central — two aggregates sharing a prefix cannot be undone once references are in circulation. Prefixes for `Conversation` and `ConversationMessage` in particular must be decided together. The registry belongs in DATABASE_STANDARDS.md and, later, in GOVERNANCE.

## 19. Indexes

| Aspect | Rule |
|---|---|
| Pattern | `IX_<Table>_<Column>[_<Column>]` — EF Core's default convention, not overridden anywhere observed |
| Uniqueness | A unique index is still `IX_`; uniqueness lives in the definition, not the name |
| Column order | Names the columns in index order, because the order determines what the index can serve |

Example following the convention: `IX_Workspace_Ref` for the unique index over the computed `Ref` column.

**Not verified.** No index name was read from the migration. These are the EF Core defaults and the migration is not known to override them. Confirm against `20260820180802_InitialSqlSchema.cs` before relying on a specific name.

## 20. Constraints

| Constraint | Pattern | Example |
|---|---|---|
| Primary key | `PK_<Table>` | `PK_Workspace` |
| Foreign key | `FK_<DependentTable>_<PrincipalTable>_<ForeignKeyColumn>` | `FK_Project_Workspace_WorkspaceId` |
| Unique / alternate key | `AK_<Table>_<Column>` | `AK_Workspace_Ref` |
| Check | `CK_<Table>_<Rule>` | `CK_Workspace_Seq_Positive` |
| Default | `DF_<Table>_<Column>` | `DF_Workspace_CreatedAt` |

`PK_`, `FK_` and `AK_` are EF Core defaults. `CK_` and `DF_` are not generated by EF Core and must be named explicitly in the configuration — an unnamed check or default constraint gets a server-generated name that differs between environments and makes a later migration undroppable.

## 21. Foreign keys

Column name: `<ReferencedAggregate>Id`. Constraint name: §20.

**Delete behaviour is part of the design and is constrained by SQL Server error 1785 — multiple cascade paths.** The rule proven during Stage 1b:

| Relationship | Behaviour |
|---|---|
| Owning parent → owned child | `Cascade` — and only here |
| Any reference FK | `Restrict` |
| Self-reference | `NoAction` |

Rationale and worked examples are in DATABASE_STANDARDS.md.

## 22. Primary keys

| Aspect | Rule |
|---|---|
| Column | Always `Id`, always `uniqueidentifier`, always single-column |
| No composite keys | A join table gets its own `Id` plus a unique index over the pair |
| C# side | A strongly-typed id — `WorkspaceId`, `ConversationMessageId` — converted by `StronglyTypedIdConverters.cs` in Infrastructure only |
| Not the key | `Seq` is an allocation counter. `Ref` is a display reference. Neither is ever the primary key, and neither ever appears in a foreign key. |

---

## 23. APIs

| API | Base path | Host project | Local port |
|---|---|---|---|
| Chat product API | `/api/v1` | `Nexus.Products.Chat.Api` | `http://localhost:5299` |
| Intelligence API | `/intelligence/v1` | `Nexus.Intelligence.Api` | has its own `launchSettings.json`; **port not verified — do not state one** |

Two shapes, deliberately different. `/api/v1` is a product's own API. `/intelligence/v1` is a named platform capability addressed by name — the pattern that generalises to `/experience/v1` and `/developer/v1`. A product never publishes under `/<capability>/v1`, and a platform capability never publishes under `/api/v1`.

**Versioning:** `v<major>` in the path, integer, no minor. A breaking change to a response contract requires `v2` alongside `v1`.

## 24. Routes

| Aspect | Rule | Example |
|---|---|---|
| Case | lowercase, hyphenated if multi-word | `/api/v1/conversation-messages` |
| Number | **Plural** collections | `/api/v1/workspaces` |
| Identity | `{id:guid}` — the route constraint is required, not optional | `/api/v1/workspaces/{id:guid}` |
| Nesting | Only where the child cannot exist without the parent | `/api/v1/conversations/{id:guid}/messages` |
| Verbs | Never in the path. `POST /api/v1/workspaces`, not `/api/v1/createWorkspace`. |
| Query parameters | camelCase | `?pageSize=50` |

Endpoint files sit in `Endpoints/` and are named `<Name>Endpoint.cs` with a `Map<Name>Endpoints` extension method. The Chat API's `Endpoints/` folder holds Artifacts, Branches, Chat, ConversationMessage, Conversations, Knowledge, Projects, Sessions, Snapshots, WorkItems, WorkSpaces and HealthEndpoint. Three of those names are drift: `WorkSpaces` has an interior capital that contradicts the `Workspace` aggregate everywhere else, `ConversationMessage` is singular where its siblings are plural, and only `HealthEndpoint` carries the `Endpoint` suffix in the observed listing. The standard is the majority form — plural, `Workspace` capitalisation, `<Name>Endpoint.cs`. Correct these when the file is next touched.

Route strings live in the endpoint file and nowhere else. The frontend's copy of a path lives in that feature's `<feature>Api.ts` and nowhere else.

## 25. DTOs

| Aspect | Rule |
|---|---|
| Type | `record`, `PascalCase`, **no `Dto` suffix** |
| Grammar | `<Verb><Aggregate>Request` / `<Verb><Aggregate>Response` |
| Verbs | `Create`, `Get`, `List`, `Update` — the four observed. `Delete`, `Archive` and similar follow the same grammar. |
| Placement | Next to the endpoint that uses them. A DTO shared by two endpoints is a signal that one of them is wrong. |
| Never | A domain type on the wire. `Workspace` is never serialised; `GetWorkspaceResponse` is. |
| Never | A DTO in a `.Contracts` project unless it is genuinely a cross-repository contract — `IntelligenceTurnRequest` qualifies; `CreateWorkspaceRequest` does not. |

Frontend counterparts are TypeScript types in the feature folder — `Workspace.ts`, `Project.ts`, `chat.types.ts`. Contract-drift rules are in TYPESCRIPT_REACT_STANDARDS.md.

---

## 26. Events

**TARGET — no event type exists in any repository.** There is no event bus, no publisher, no handler. **M-01-8.1 In-process event bus** is the milestone that introduces them, and **M-05-3.2 Event handlers and trigger bindings** binds them to automation.

Grammar to use when they arrive, so the first one is right:

| Element | Pattern | Example |
|---|---|---|
| Event type | `<Aggregate><PastTenseVerb>` — past tense, always | `WorkspaceCreated`, `ConversationMessageAppended`, `WorkItemBlocked` |
| Handler | `<Event>Handler` | `WorkspaceCreatedHandler` |
| Placement | Domain events beside the aggregate; integration events in a Contracts project |

Past tense is the whole rule: an event is a fact that has already happened. `CreateWorkspace` as an event name is a command wearing the wrong clothes.

## 27. Commands

**TARGET — no command type exists.** No CQRS dispatcher is present; API endpoints call application services directly.

| Element | Pattern | Example |
|---|---|---|
| Command | `<ImperativeVerb><Aggregate>` — imperative, no `Command` suffix | `CreateWorkspace`, `ArchiveConversation` |
| Handler | `<Command>Handler` | `CreateWorkspaceHandler` |

Commands may be refused; events may not. That is why the tense differs.

**Do not introduce a command bus to satisfy this section.** The current direct-call style is acceptable; the grammar exists so that if a dispatcher is ever introduced it is introduced consistently.

## 28. Queries

**TARGET as a type family; CURRENT as a naming style.** One query object exists: `MemoryQuery` in `Nexus.Intelligence.Memory`.

| Element | Pattern | Example |
|---|---|---|
| Query object | `<Aggregate>Query` | `MemoryQuery` |
| Query method | `Get<Name>Async` for one, `List<Name>Async` for many, `Find<Name>Async` where absence is expected | `GetWorkspaceAsync`, `ListConversationsAsync` |
| Result | `<Verb><Name>Response` per §25 | `ListConversationsResponse` |

`Get` versus `Find` is a contract: `Get` throws or returns a not-found result for a missing entity, `Find` returns null and expects the caller to handle it. See CODE_CONVENTIONS.md.

---

## 29. React components

| Aspect | Rule |
|---|---|
| File | `PascalCase.tsx`, one component per file, file name equals component name |
| Export | Named export matching the file name |
| Location | `components/` if used by two or more features; `features/<feature>/` otherwise |

**Suffix vocabulary in use.**

| Suffix | Meaning | Real examples |
|---|---|---|
| `Page` | A routed screen in `pages/` | `ChatPage`, `DashboardPage`, `WorkspacesPage`, `ProjectDetailsPage`, `KnowledgeItemPage`, `WorkItemPage`, `SettingsPage`, `WorkspaceSettingsPage`, `InsightsPage`, `CreateWorkspacePage`, `NotFoundPage` |
| `Panel` | A bounded region within a page | `ChatPanel`, `CitationsPanel` |
| `List` | Renders a collection | `ConversationList` |
| `Thread` | An ordered conversation view | `MessageThread` |
| `Form` | Collects input and submits | `CreateConversationForm`, `CreateProjectForm`, `CreateWorkspaceForm`, `UpdateWorkspaceForm` |
| `Selector` | Chooses among existing entities | `WorkspaceSelector` |
| `Card` | A bounded display unit | `Card`, `MetricCard` |
| `Context` | A React context provider module | `WorkspaceContext`, `ChatTelemetryContext` |
| `ErrorBoundary` | An error boundary | `RouteErrorBoundary` |
| `Layout` | Application shell | `AppLayout` |
| `Providers` | Composition of providers | `AppProviders` |
| `Routes` | The routing table | `AppRoutes` |

`WorkspaceProjects.tsx` shows the composite form `<Parent><Children>` — the projects belonging to a workspace. Read `<Scope><Thing>` and it is unambiguous.

`Create<Name>Form` and `Update<Name>Form` mirror the `Create<Name>Request` / `Update<Name>Request` DTOs. Keeping the verb identical on both sides of the wire is deliberate.

## 30. Hooks

| Aspect | Rule |
|---|---|
| File | `use<Thing>.ts` — camelCase `use`, PascalCase noun, `.ts` not `.tsx` |
| Location | The feature folder that owns the data |
| Export | Named export matching the file name |

| Hook | Reads | Kind |
|---|---|---|
| `useConversations.ts`, `useConversation.ts`, `useConversationMessages.ts` | chat | query |
| `useCreateConversation.ts`, `useSendChat.ts` | chat | mutation |
| `useCitationTarget.ts` | chat | derived state |
| `useProjects.ts`, `useProject.ts` | projects | query |
| `useCreateProject.ts`, `useUpdateProject.ts` | projects | mutation |
| `useWorkspaces.ts`, `useWorkspace.ts` | workspaces | query |
| `useCreateWorkspace.ts`, `useUpdateWorkspace.ts` | workspaces | mutation |
| `useSystemHealth.ts` | system | query |

**Singular versus plural is the collection/single distinction and is strictly observed:** `useConversations` returns many, `useConversation` returns one. Mutations are `use<Verb><Thing>` with the verb matching the API verb — `useCreateWorkspace` calls the endpoint behind `CreateWorkspaceRequest`.

A hook file never exports a component; a component file never exports a hook.

## 31. TypeScript files

| Content | Case | Real examples |
|---|---|---|
| React component | `PascalCase.tsx` | `ChatPanel.tsx`, `MetricCard.tsx` |
| Hook | `use<Thing>.ts` | `useSendChat.ts` |
| API client for a feature | `<feature>Api.ts`, camelCase | `chatApi.ts`, `projectsApi.ts`, `workspacesApi.ts`, `systemApi.ts` |
| Types for a feature | `<feature>.types.ts` or `<Entity>.ts` | `chat.types.ts`; `Workspace.ts`, `Project.ts`, `SystemHealth.ts` |
| Shared infrastructure class module | `PascalCase.ts` | `ApiClient.ts`, `ApiError.ts` |
| Configuration or singleton module | `camelCase.ts` | `environment.ts`, `queryClient.ts` |
| Helper module | `camelCase.ts` | `citationTargets.ts` |

**The rule underneath the table:** a file is `PascalCase` when its default subject is a single named type or class (`ApiClient`, `Workspace`, `SystemHealth`), and `camelCase` when it is a bag of functions or values (`workspacesApi`, `citationTargets`, `queryClient`). Two conventions coexisting looks like inconsistency until that rule is stated; state it rather than mass-renaming.

`chat.types.ts` is the only `.types.ts` file. For a feature with several exported types, prefer it over one file per type.

## 32. CSS

**CURRENT: there is no CSS naming convention, because there is nothing to name it in.** Styling is a single `index.css`. No CSS-in-JS, no CSS modules, no Tailwind, no component library — see TECHNOLOGY_STACK.md §7.

**TARGET — M-11-6.1 Design tokens and primitives.** Until that milestone chooses an approach, do not introduce a second styling mechanism. Two conventions in one client is worse than one imperfect one. Interim rules that hold regardless of what is chosen:

| Rule | Detail |
|---|---|
| Class names | `kebab-case`, never `camelCase` |
| Scope prefix | The component the class belongs to — `chat-panel__message` |
| Token names | `--nexus-<category>-<name>` for CSS custom properties |
| No element selectors | Except in a documented reset block |

---

## 33. Tests

| Aspect | Rule |
|---|---|
| Project | `<Subject>.Tests` for behaviour, `<Subject>.Architecture.Tests` for boundaries |
| File | `<TypeUnderTest>Tests.cs` |
| Folder | Mirrors the folder of the type under test |
| Test method | `<Method>_<Condition>_<ExpectedResult>` |

**Everything that exists, in full.**

| Repository | Project | Files |
|---|---|---|
| Nexus.Platform | `Nexus.Platform.Architecture.Tests` | `PlatformBoundaryTests.cs` |
| Nexus.Platform | `Nexus.Platform.Tests` | **none — a `.csproj` with zero `.cs` files** |
| Nexus.Intelligence | `Nexus.Intelligence.Architecture.Tests` | `BoundaryRuleTests.cs` |
| Nexus.Intelligence | `Nexus.Intelligence.Tests` | `Ranking/KeywordContextRankerTests.cs` |
| Nexus.Experience | `Nexus.Products.Chat.Architecture.Tests` | `BoundaryTests.cs` |
| Nexus.Experience | `Nexus.Products.Chat.Tests` | `Chat/ChatContextBundleMapperTests.cs` |

**Exactly two behaviour tests exist in the entire system.** `Ranking/KeywordContextRankerTests.cs` mirrors `Nexus.Intelligence.Context/Ranking/` correctly. `Chat/ChatContextBundleMapperTests.cs` names a type, `ChatContextBundleMapper`, whose location is not recorded — locate it and confirm the folder mirrors it.

The three architecture-test files carry three different names for the same job — `PlatformBoundaryTests`, `BoundaryRuleTests`, `BoundaryTests`. Standardise on `<Scope>BoundaryTests`: `PlatformBoundaryTests`, `IntelligenceBoundaryTests`, `ChatBoundaryTests`.

**There are zero frontend tests.** No test file, no test framework, no configuration. That is a gap, recorded in TYPESCRIPT_REACT_STANDARDS.md, not a convention.

---

## 34. Git branches

**TARGET structure:**

```
main                    protected, green-build-required, no direct commits
└── integration/<ms>    per-milestone integration branch
    ├── work/<id>-a     worker A, own worktree (sibling directory)
    ├── work/<id>-b     worker B, own worktree
    └── work/<id>-c     worker C, own worktree
```

| Branch | Pattern | Example |
|---|---|---|
| Trunk | `main` | `main` |
| Integration | `integration/<milestone-id>` | `integration/M-02-1.2` |
| Work | `work/<work-item-id>-<worker-letter>` | `work/WI-02-1.2.1-a` |

The worker letter is the collision boundary: three workers on one milestone get `-a`, `-b`, `-c` and three separate worktrees. **Worktrees go in a sibling directory** — a git worktree nested inside a folder an agent has as its working directory cannot be renamed on Windows while that agent runs.

**CURRENT:** `Nexus.Web` is on `feat/azure-sql` at `29ac2f4`. That name predates the convention. SQL Stage 1b is complete, proven and **uncommitted** on it.

**TRANSITION:** existing `feat/*` branches are finished as they are; new branches use `work/<id>-<letter>`. `main` is not protected today — protection arrives at **M-08-1.4 Branch protection and architecture gate**, and "green-build-required" cannot mean anything before **M-08-1.2 Pipelines on every repository**.

The operating rule that came out of 2026-08-20: **push at every stage boundary, not every milestone.** A branch name is worth nothing if the objects are gone.

## 35. Git tags

**TARGET — no tag convention is in use.**

| Tag | Pattern | Example |
|---|---|---|
| Release | `v<major>.<minor>.<patch>` | `v1.0.0` |
| Milestone completion | `milestone/<id>` | `milestone/M-02-1.4` |
| Recovery / incident point | `recovery/<yyyy-MM-dd>` | `recovery/2026-08-20` |

Tags are annotated, never lightweight, and never moved once pushed. Release tagging becomes real at **M-08-5.2 Release promotion** and **M-07-7.2 Releases and maturity**.

A `recovery/2026-08-20` tag on each of the three repositories would mark the post-recovery state and is worth creating retrospectively — the `.git-broken\` directories still present in all three are not a substitute for a named point in history.

## 36. Builds

**TARGET — there is no CI. `NexusAI\.github\workflows\` exists and is empty; `Nexus.Web` and `Nexus.Int` have no `.github` directory at all.**

| Element | Pattern | Example |
|---|---|---|
| Workflow file | `<verb>-<subject>.yml`, kebab-case, in `.github/workflows/` | `build-platform.yml`, `verify-architecture.yml` |
| Workflow name | Sentence case, what it proves | `Build and verify Nexus.Platform` |
| Job id | `snake_case` | `architecture_tests` |
| Step name | Imperative | `Restore packages` |
| Build number | `<yyyyMMdd>.<n>` | `20260821.3` — the same shape as a migration timestamp |

Introduced at **M-08-1.2 Pipelines on every repository**; results become machine-readable at **M-08-1.3**; blocking at **M-08-1.4**.

**Any pipeline written before M-08-1.1 will fail**, because `nuget.config` points at `C:\Personal\LocalNuGet`, which is not reachable from a build agent and is not a git repository.

## 37. Artifacts

**The word `Artifact` means two unrelated things in Nexus. Never use it unqualified.**

| Meaning | What it is | Naming |
|---|---|---|
| **Domain `Artifact`** | One of the 11 aggregates in `Nexus.Products.Chat.Domain` — `Artifact.cs`, `ArtifactId.cs`, `ArtifactStatus.cs`, `IArtifactRepository.cs`, endpoint `Artifacts`, table `Artifact` | §§6–9, 17 |
| **Build artifact** | A file produced by a build — a NuGet package, a published output, a client bundle | Below |

In prose, write "the `Artifact` aggregate" or "a build artifact". A sentence containing both without qualification is a defect.

**Build artifact naming (TARGET — nothing is published to a shared feed yet).**

| Artifact | Pattern | Example |
|---|---|---|
| NuGet package | Package id equals assembly name equals project name | `Nexus.Platform.Contracts` |
| Package version | `<major>.<minor>.<patch>` released; `<major>.<minor>.<patch>-preview.<n>` prerelease | prerelease never merges to `main` — STACK_VERSION_POLICY.md §3 |
| Published API output | `<ProjectName>/<buildNumber>/` | `Nexus.Products.Chat.Api/20260821.3/` |
| Client bundle | `<ProjectName>/<buildNumber>/` | `Nexus.Experience.Client/20260821.3/` |
| Migration script | `<timestamp>_<PascalCaseName>.sql`, matching its migration | `20260820180802_InitialSqlSchema.sql` |

CURRENT: packages are produced by `pack-local.ps1` in NexusAI and Nexus.Int into `C:\Personal\LocalNuGet`. TARGET: GitHub Packages — **M-08-1.1**. Retention: **M-08-3.1 Artifact publication and retention**.

---

## 38. Milestones

The work graph uses one identifier grammar, defined in `nexus-roadmap.yaml`. Every level is derived from its parent, so any identifier locates itself.

| Level | Pattern | Real example | Meaning |
|---|---|---|---|
| Layer | `<nn>` | `07` | DEVELOPER |
| Feature | `F-<layer>-<n>` | `F-10-1` | Structured Logging and Correlation |
| Milestone | `M-<layer>-<feature>.<n>` | `M-07-2.2` | Parallel-safety rules |
| Work item | `WI-<layer>-<feature>.<milestone>.<n>` | `WI-10-1.1.1` | Logging foundation |
| Task | `T-<work-item>.<n>` | `T-10-1.1.1.2` | Redaction policy for sensitive fields |
| Subtask | `S-<task>.<n>` | `S-10-1.1.1.1.1` | Correlation middleware in every host |

Milestones referenced throughout this document, as a worked reading: `M-02-1.4` is layer 02 DATA, feature 1, milestone 4 — *Delete Dataverse*. `M-08-1.1` is layer 08 DELIVERY, feature 1, milestone 1 — *Package feed reachable from CI*.

| Rule | Detail |
|---|---|
| Milestone **name** | A noun phrase naming the outcome, sentence case: *Layer schema convention*, *Real secret resolver*, *Correlation across hosts*. Never a task list, never a verb phrase. |
| Identifiers are permanent | A milestone is never renumbered. Branch names, commit messages and this documentation all reference it. |
| Every gap cites one | Any CURRENT/TARGET mark in any document names the milestone that closes it. A TARGET with no milestone is a wish. |

## 39. Work items

Two distinct things again, and again they must be qualified.

| Meaning | What it is |
|---|---|
| **Roadmap work item** | `WI-<layer>-<feature>.<milestone>.<n>` — a unit of work in `nexus-roadmap.yaml`, e.g. `WI-10-1.1.1 Logging foundation` |
| **`WorkItem` aggregate** | A domain type in `Nexus.Products.Chat.Domain` — `WorkItem.cs`, `WorkItemId.cs`, `WorkItemStatus.cs`, `IWorkItemRepository.cs`, endpoint `WorkItems`, page `WorkItemPage.tsx` |

They converge deliberately: **M-07-1.1 Work graph aggregates** is where DEVELOPER makes the roadmap's work items into real records. When it does, the roadmap identifier becomes the `Ref` of the record and the two meanings become one — which is why the `Ref` prefix for `WorkItem` must be allocated deliberately (§18).

Each work item carries a scope — projects, schemas, contracts — and that scope is what **M-07-2.2 Parallel-safety rules** evaluates. Two work items may run simultaneously only if all five hold: no transitive dependency path, no file or project scope overlap, **no shared schema mutation**, no contract mutation on a shared boundary, and not both high risk. The third is the one most often got wrong: two EF migrations on one `DbContext` conflict on the model snapshot even when they touch different tables. Classification names are fixed and are themselves a naming standard — *Can run now*, *Can run together*, *Blocked*, *Waiting for dependency*, *High conflict risk*, *Must be sequential*.

## 40. Tasks

| Level | Pattern | Example | Naming |
|---|---|---|---|
| Task | `T-<work-item>.<n>` | `T-10-1.1.1.1` | Imperative or noun phrase naming the deliverable — *Structured logging with a correlation enricher* |
| Subtask | `S-<task>.<n>` | `S-10-1.1.1.1.2` | The narrowest verifiable step — *Outbound HTTP propagates the header* |

A task name states what will be true when it is done. *Fix logging* is not a task name; *Redaction policy for sensitive fields* is.

## 41. Workers

| Element | Pattern | Example |
|---|---|---|
| Worker slot on a milestone | single lowercase letter, `a`–`c` | `a` |
| Worker branch | `work/<work-item-id>-<letter>` | `work/WI-02-1.2.1-a` |
| Worker worktree directory | `<repository>-<letter>`, **sibling** to the repository | `Nexus.Experience-a` beside `Nexus.Experience` |
| Agent worker record | `<AgentType>Agent` | `DeveloperAgent` (currently a 974-byte stub) |

The letter is scoped to the milestone, not global — `-a` on two different milestones is two different workers and that is fine. What must never happen is two workers sharing a letter within one milestone, because the letter is what keeps the branches and worktrees apart.

Worker naming becomes a real record at **M-07-3.1 Worker, assignment and run**; **M-07-3.3 Model assignment and run cost** attaches a model and a cost to each run.

---

## 42. Documentation

| Kind | Pattern | Real examples |
|---|---|---|
| Canonical numbered set | `<nn>_<SCREAMING_SNAKE>.md`, 00–12 mirroring the layers | `09_ROADMAP_AND_MILESTONES.md`, `07_DEVELOPMENT_GUIDE.md` |
| Standards and references | `SCREAMING_SNAKE.md` | `NAMING_STANDARDS.md`, `CODE_CONVENTIONS.md`, `TECHNOLOGY_STACK.md`, `DATABASE_STANDARDS.md` |
| ADR | `ADR-<nnn>_<SCREAMING_SNAKE>.md` | `ADR-014_AZURE_SQL_MIGRATION.md`, `ADR-015_PROJECT_BRIEF.md` |
| Dated incident record | `<SUBJECT>_<yyyy-MM-dd>.md` | `GIT_RECOVERY_2026-08-20.md` |
| Runbook | `<SUBJECT>_RUNBOOK.md` | `NEXUS_MIGRATION_RUNBOOK.md` |
| State snapshot | `<SUBJECT>_STATE.md` | `MIGRATION_STATE.md` |
| Machine-readable roadmap | `nexus-roadmap.yaml`, kebab-case | — |
| Script | `<verb>-<subject>.ps1`, kebab-case | `pack-local.ps1`, `set-openai-key.ps1`, `run-migration.ps1`, `nexus-v2-restructure.ps1` |

**ADRs use one global sequence across the whole system.** ADR-014 and ADR-015 exist; the next is **ADR-016**. There is no per-layer or per-repository ADR numbering, and introducing one would break the single sequence permanently.

**Front matter on every document:** `Status`, `Owner`, `Last updated`, `Layer` where relevant, `Authoritative for`.

**One subject, one document.** If another document owns a subject, link to it by filename and stop. This document names things; it does not explain EF Core configuration (DATABASE_STANDARDS.md), C# construction (CSHARP_STANDARDS.md), frontend structure (TYPESCRIPT_REACT_STANDARDS.md), cross-language rules (CODE_CONVENTIONS.md), or which technologies are approved (TECHNOLOGY_STACK.md).
