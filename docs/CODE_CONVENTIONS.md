# Code Conventions

> **SUPERSEDED NUMBERING NOTICE (2026-09-05):** This document's own header
> (`**Owner:** Layer 07 DEVELOPER (definition) / Layer 09 ASSURANCE
> (conformance)`) reflects the v2.1 twelve-layer model, in which 07 DEVELOPER
> was a numbered Platform layer. Per the approved v2.2 renumbering
> (`LAYER_MODEL.md` §2.2, §4a), Nexus Forge and Nexus Developer (the product)
> now sit OUTSIDE the ten numbered Platform layers, and ASSURANCE is renumbered
> 08 (from 09). The coding conventions themselves remain valid. Re-deriving
> this document's own ownership header against the v2.2 model is
> Wave-D-adjacent decision work and is explicitly NOT done in this batch.

> **Status:** TRANSITION — the rules hold across all languages; several categories have no implementation in Nexus yet and are marked TARGET with the milestone that closes them
> **Owner:** Layer 07 DEVELOPER (definition) / Layer 09 ASSURANCE (conformance)
> **Last updated:** 2026-08-21
> **Layer:** Cross-cutting
> **Authoritative for:** rules that must mean the same thing in every Nexus language — C#, TypeScript, React and SQL — and the per-language form each takes

**Scope.** This document owns the *concept*. Language-specific mechanics live in **CSHARP_STANDARDS.md** and **TYPESCRIPT_REACT_STANDARDS.md**; database mechanics live in **DATABASE_STANDARDS.md**; names live in **NAMING_STANDARDS.md**. Where a rule is stated here, those documents do not restate it — they show how it is written.

**Python is marked *future* throughout.** No Python exists in any Nexus repository and none is selected — TECHNOLOGY_STACK.md §7. The Python column exists so that when a workload arrives it inherits these rules rather than importing a second philosophy. Nothing in the Python column is binding on anyone today.

---

## 1. Language equivalence table

The same concept, four spellings. This table is the index for the rest of the document.

| Concept | C# | TypeScript / React | SQL | Python (future) |
|---|---|---|---|---|
| Contract | `interface IWorkspaceRepository` | `interface Workspace` in `Workspace.ts` | table + constraints | `Protocol` |
| Immutable data | `record ContextBundle` | `type ChatMessage` (readonly) | row | frozen `dataclass` |
| Absence | `Workspace?` with nullable enabled | `T \| null` / `T \| undefined` | `NULL` | `T \| None` |
| Async unit | `Task<T>` / `ValueTask<T>` | `Promise<T>` | — (set-based) | `Awaitable[T]` |
| Cancellation | `CancellationToken` | `AbortSignal` | command timeout | `asyncio.CancelledError` |
| Failure | exception, or a result type | thrown `ApiError` | error / rollback | exception |
| Composition | constructor injection | hooks and props | — | constructor injection |
| Identity | `WorkspaceId` (strongly-typed) | `string` (branded) | `uniqueidentifier Id` | `NewType` |
| Instant | `DateTimeOffset` | ISO 8601 `string` | `datetimeoffset` | `datetime` (tz-aware) |
| Enumeration | `enum TrustLevel` | union of string literals | value converted to `int`/`string` | `StrEnum` |
| Disposal | `IAsyncDisposable` / `using` | explicit teardown in `useEffect` | connection scope | `async with` |

---

## 2. Function length

| Language | Guideline | Hard limit | Notes |
|---|---|---|---|
| C# | ≤ 30 lines | 50 | `Map<Name>Endpoints` may exceed it when it is purely a list of route registrations — a flat registration list is not complexity. |
| TypeScript | ≤ 30 lines | 50 | An API-client function in `workspacesApi.ts` that exceeds this is doing transport *and* mapping; split it. |
| React component | ≤ 150 lines | 200 | Above this, extract a hook or a child component. `MessageThread.tsx` rendering a list is legitimately long; `ChatPanel.tsx` holding four responsibilities is not. |
| Hook | ≤ 50 lines | 80 | A hook past this is orchestrating several concerns — `useSendChat.ts` should send, not also derive citations, which is why `useCitationTarget.ts` is separate. |
| SQL (in a migration) | — | — | Length is generated. Hand-written SQL inside a migration is capped by readability, not lines. |
| Python (future) | ≤ 30 lines | 50 | — |

The limit is a smell detector, not a rule to be gamed. Two things override it: a `switch` over an enum where every arm is one line, and a registration list.

**Nesting** is the stronger signal in every language: **maximum depth 3**. Beyond that, return early or extract. The `TurnPipeline` step decomposition — `IntentClassifier`, `PolicyGate`, `ContextSelector`, `AgentSelector`, `ModelSelector`, `PromptStep`, `ModelStep`, `ToolLoop`, `ResponseComposer` — is what this rule produces when it is followed properly: each step is shallow because the composition holds the depth.

## 3. Parameters

| Language | Maximum | Beyond that | Real form |
|---|---|---|---|
| C# | 4, excluding `CancellationToken` | Introduce a `record` — `MemoryQuery`, `PromptRequest`, `RankingOptions` all exist for exactly this reason | `ListAsync(WorkspaceId id, MemoryQuery query, CancellationToken cancellationToken)` |
| TypeScript | 3 | A single object parameter | `createWorkspace(request, signal)` |
| React component | — | Props are already an object; more than ~7 props means the component has more than one job | `<MetricCard title value />` |
| SQL | — | Migrations are generated | — |
| Python (future) | 4 | keyword-only arguments, then a dataclass | — |

**Rules that hold in every language.**

- **No boolean parameters.** `Get(id, true)` is unreadable at the call site. Use an enum, or two methods. `SideEffectClass` exists because "does this tool have side effects" is a classification, not a flag.
- **Order:** subject, then inputs, then options, then `CancellationToken`/`AbortSignal` last. Always last, with no default value in C#.
- **No `out` parameters** in new C# code. Return a tuple or a record.
- **No optional parameter that changes behaviour** — only ones that supply a value.

## 4. Return types

| Situation | C# | TypeScript | Rationale |
|---|---|---|---|
| One thing that must exist | `Task<Workspace>` | `Promise<Workspace>` | Absence is an error the caller cannot handle locally |
| One thing that may not exist | `Task<Workspace?>` | `Promise<Workspace \| null>` | Absence is normal; the caller decides |
| Many things | `Task<IReadOnlyList<Conversation>>` | `Promise<Conversation[]>` | Never `IEnumerable<T>` across an async or repository boundary — the query must be complete before the connection closes |
| Nothing | `Task` | `Promise<void>` | — |
| An outcome with reasons | `QuotaVerdict`, `AgentResult`, `ToolResult`, `ResultReport` | a discriminated union | An expected negative outcome is data, not an exception |
| Over HTTP | `<Verb><Name>Response` record | the feature's type from `Workspace.ts` / `chat.types.ts` | Never a domain type on the wire |

**`Get` versus `Find` is a contract, not a preference**, and it is identical in C# and TypeScript:

| Prefix | Missing entity | Return |
|---|---|---|
| `Get` | is an error | non-nullable, or throws/returns not-found |
| `Find` | is expected | nullable |
| `List` | empty is normal | empty collection, **never null** |

**Never return null for a collection.** `ListConversationsAsync` returning null instead of an empty list forces a null check at every call site forever.

**Never return an anonymous shape from a public boundary.** `QuotaVerdict` and `ToolResult` exist as named records so the outcome has a name to discuss.

## 5. Async

| Language | Rule |
|---|---|
| **C#** | Async all the way. `Async` suffix on every `Task`-returning method. Never `.Result`, never `.Wait()`, never `.GetAwaiter().GetResult()` — a synchronous block on an async call is how ASP.NET Core hosts deadlock under load. `ConfigureAwait` is not needed in ASP.NET Core and is not used. |
| **TypeScript** | `async`/`await`, never raw `.then()` chains. **No `Async` suffix on names** — `sendChat`, not `sendChatAsync`. |
| **React** | A component is never `async`. Async work belongs to a hook, and the hook is a TanStack Query hook — `useConversations.ts`, `useSendChat.ts`. A bare `useEffect` that fetches is a defect. |
| **SQL** | Asynchrony belongs to the client. Do not build asynchrony into the database. |
| **Python (future)** | `async def` / `await`; no suffix. |

**The rules that cross languages:**

- **Do not fire and forget.** Every `Task` and every `Promise` is awaited or explicitly handed to something that owns it. An unawaited promise loses its rejection.
- **Parallelise deliberately, never accidentally.** Independent calls may run concurrently (`Task.WhenAll`, `Promise.all`); calls sharing a `DbContext` may not — a `DbContext` is not thread-safe and two concurrent operations on one throw.
- **Async has to earn itself.** `KeywordContextRanker` ranks in memory; making it async would add a state machine and buy nothing.

## 6. Error handling

Three categories, and the category decides the mechanism in every language.

| Category | Example in Nexus | Mechanism |
|---|---|---|
| **Expected outcome** | quota exceeded (`QuotaVerdict`), tool refused (`ToolResult`), turn could not proceed (`TurnError`) | A value. Never an exception. |
| **Caller error** | malformed request reaching `TurnRequestValidation` | Reject at the boundary with a 400-class response |
| **Genuine fault** | database unreachable, provider returned nonsense | Exception; caught only at the boundary that can decide |

| Language | Form |
|---|---|
| **C#** | Throw a specific exception type. Catch only what you can act on. Never `catch (Exception)` outside a host boundary. Never swallow — a caught exception is logged with context or rethrown with `throw;`, never `throw ex;` (which erases the stack trace). |
| **TypeScript** | `ApiError` in `api/ApiError.ts` is the single typed error for a failed HTTP call. Every non-2xx response from `ApiClient.ts` becomes an `ApiError`; nothing else throws raw fetch errors upward. |
| **React** | `RouteErrorBoundary.tsx` is the last resort for a render-time fault. A *query* failure is not an error boundary's job — it is the `isError` branch of the hook, rendered inline. |
| **SQL** | Constraint violations are errors and are meant to be. Error 1785 (multiple cascade paths) is a *design* error caught at migration time; see §22 and DATABASE_STANDARDS.md. |
| **Python (future)** | Specific exception types; no bare `except`. |

**Universal rules.**

- An error message names what failed and what was being attempted. `"Workspace 3f2a… not found"` — never `"Error"`, never `"Something went wrong"`.
- **No secret, token or full prompt body ever appears in an error message or a log line.** This is a stated acceptance criterion of **M-10-1.1 Correlation across hosts** and applies now, before the milestone.
- An error crossing a repository boundary carries the correlation identifier once one exists (**M-10-1.1**).

## 7. Nullability

| Language | Mechanism | Rule |
|---|---|---|
| **C#** | Nullable reference types | Enabled repository-wide via `Directory.Build.props`. A nullable annotation is a statement about the domain: `Workspace?` means "may not exist", not "I have not thought about it". No `!` null-forgiving operator except immediately after a check the compiler cannot see, with a comment. |
| **TypeScript** | `strict` | `null` for a value the server said is absent; `undefined` for a value not yet loaded. TanStack Query's `data` is `undefined` before the first successful fetch — that is the *loading* state, not the *empty* state, and conflating them is the most common bug in this codebase's shape. |
| **SQL** | `NOT NULL` by default | Every column is `NOT NULL` unless absence is a modelled fact. `Id`, `Seq` and `Ref` are never nullable. |
| **Python (future)** | `T \| None`, checked | — |

**Validate at the boundary, then stop.** A value that entered through `TurnRequestValidation` or an endpoint's validation is non-null from there inward. Re-checking the same value at every layer is noise that hides the one check that matters.

## 8. Side effects and pure functions

| Rule | Applies to |
|---|---|
| A function that returns a value should not also mutate observable state | all languages |
| A function that mutates state should return `void`/`Task`, not a value | all languages |
| Pure by default; effectful by declaration | all languages |

**Pure in Nexus today:** `KeywordContextRanker` (items in, ranked items out), `PromptAssembler` (`PromptRequest` in, `AssembledPrompt` out), `IntentClassifier`, `ContextSelector`, `ModelSelector`. These are testable precisely because they are pure — `KeywordContextRankerTests.cs` is one of only two behaviour tests in the system, and it exists for that reason.

**Effectful by design:** repositories (`SqlWorkspaceRepository`), gateways (`RoutingModelGateway`, `AnthropicModelGateway`), stores (`InMemoryMemoryStore`), `ConsoleAuditLog`, `InMemoryUsageMeter`.

The `TurnPipeline` composition is the pattern to follow: pure decision steps, with effects pushed to the edges where `ModelStep` and `ToolLoop` sit.

**React:** a component's render is pure. Effects live in `useEffect` with a correct dependency array and a cleanup function, or — for anything touching the server — in a TanStack Query hook, not `useEffect` at all.

**SQL:** the `Ref` computed column is deterministic and PERSISTED. A computed column must be deterministic or it cannot be persisted or indexed.

## 9. Validation

Validation happens **once, at the boundary**, and the boundary is the outermost layer that can reject.

| Layer | Validates | Real example |
|---|---|---|
| Frontend form | Shape and obvious mistakes — a courtesy, never a control | `CreateWorkspaceForm.tsx`, `CreateProjectForm.tsx`, `CreateConversationForm.tsx` |
| API endpoint | Request contract: required fields, formats, ranges | `TurnRequestValidation` in `Nexus.Intelligence.Api/Endpoints/` |
| Domain | Invariants that must be true of the aggregate at all times | the aggregate's `Create` method |
| Database | Structural truth: `NOT NULL`, uniqueness, foreign keys | `WorkspaceConfiguration.cs` → migration |

**The distinction that matters:** an API validates the *request*; the domain validates the *invariant*. `WorkspaceName must not be empty` is both, and that duplication is correct — one produces a 400, the other guarantees no code path can construct an invalid `Workspace`.

**`Create` applies invariants. `Restore` does not.** `public static Workspace Restore(...)` rehydrates a row that already exists; re-validating it would make a schema change unable to load old data. This is why the two are separate methods on every aggregate root.

**Policy is not validation.** `PolicyGate` and `IQuotaPolicy` decide whether an operation is *permitted*; validation decides whether it is *well-formed*. A `QuotaVerdict` is a policy outcome and returns as a value; a malformed request is a validation failure and rejects at the edge.

## 10. Dependency injection

| Language | Mechanism |
|---|---|
| **C#** | Constructor injection, ASP.NET Core's built-in container. No service locator, no static access to the container, no `IServiceProvider` injected into a domain or application type. |
| **React** | Props for the near, context for the ambient. `AppProviders.tsx` composes; `WorkspaceContext.tsx` and `ChatTelemetryContext.tsx` are the two ambient concerns that earned a context. |
| **SQL** | n/a |
| **Python (future)** | Constructor injection. |

**Registration is grouped and named per area** — `IntelligenceServiceCollectionExtensions` with `Add<Area>(this IServiceCollection services)`, and `ChatProductModule` composing the Chat product's registrations. A `Program.cs` that registers services one by one has no seam for a product to be composed at.

**Lifetimes:**

| Lifetime | Use for | Example |
|---|---|---|
| Singleton | Stateless, thread-safe | `KeywordContextRanker`, `PromptAssembler`, `AgentRegistry` |
| Scoped | Per request, holds request state | `NexusChatDbContext`, `SqlWorkspaceRepository` |
| Transient | Cheap, stateful | rare |

**Never inject a scoped service into a singleton.** A singleton capturing a `NexusChatDbContext` produces a context that outlives its request and corrupts change tracking — the failure appears far from the cause.

**React context is not a dependency container.** Context carries ambient values; server data belongs to TanStack Query. Putting API data in a React context reimplements a cache that already exists.

## 11. Logging

**No logging library exists in any Nexus repository.** TECHNOLOGY_STACK.md §7 records it as NOT SELECTED; **M-10-1.1 Correlation across hosts** selects it. Until then, these rules say *what* to log, not *with what*.

| Rule | Detail |
|---|---|
| **Structured, not interpolated** | Log a message template plus named values, so a value can be queried later. Never build a sentence by string concatenation. |
| **Correlation identifier on every line** | One request must be retrievable end to end across the Experience API, the Intelligence turn and the model invocation by that identifier alone. Generated at the edge, or accepted from the caller. **TARGET — M-10-1.1.** |
| **Never log a secret, a token or a full prompt body** | Stated acceptance criterion of M-10-1.1. Binding now. |
| **Never log personal data without a classification** | **M-02-5.1 Classification and retention** |
| **Log at boundaries, not inside loops** | A per-item log line in `ToolLoop` produces volume without information. |
| **An audit record is not a log line** | `IAuditLog` / `AuditEntry` is a durable governance record; a log line is diagnostic and disposable. `ConsoleAuditLog` currently blurs this — it is a development placeholder, and the durable implementation arrives at **M-01-4.1 Durable audit log**. |
| **Frontend** | The browser console is not telemetry. `ChatTelemetryContext.tsx` is the deliberate telemetry seam — see TYPESCRIPT_REACT_STANDARDS.md. |

| Level | Means |
|---|---|
| Error | An operation failed and someone must know |
| Warning | Degraded but continuing — a fallback fired, a retry succeeded |
| Information | A significant state change: turn started, workspace created |
| Debug | Development only; never enabled in a deployed environment |

## 12. Cancellation

**CURRENT — and this one is already right.** `CancellationToken` is threaded through the real repository interfaces in `Nexus.Products.Chat.Domain`. Do not regress it.

| Language | Mechanism | Rule |
|---|---|---|
| **C#** | `CancellationToken` | Last parameter, no default value on an interface method, named `cancellationToken`. Pass it onward to every call that accepts one — a token that stops being passed halfway down is worse than no token, because it looks like cancellation works. |
| **TypeScript** | `AbortSignal` | Accepted by `ApiClient.ts` and passed to `fetch`. TanStack Query supplies a signal to its query function; use it, and a navigation away from `ChatPage` aborts the in-flight request instead of resolving into an unmounted component. |
| **React** | cleanup | Every `useEffect` that starts something returns a function that stops it. |
| **SQL** | command timeout | Cancellation propagates as a cancelled command. Long-running work is not held open across a cancellation boundary. |
| **Python (future)** | `asyncio` cancellation | — |

Cancellation is **cooperative**. A cancelled long-running operation must leave state consistent — see §15 (transactions) and §16 (idempotency).

## 13. Timeouts

**TARGET — no timeout policy exists in the codebase.**

| Boundary | Rule | State |
|---|---|---|
| Outbound HTTP to a model provider | An explicit timeout, set per call, longer than a database timeout and shorter than the request timeout of the caller | **TARGET** |
| Database command | The provider default is accepted only until measured; a command that needs longer needs an index, not a longer timeout | **TARGET** |
| Frontend fetch | Bounded by an `AbortSignal`, so the UI can always recover | **TARGET** |
| Every timeout | Configured, never hardcoded; expressed as a `TimeSpan`/`Duration`, never a bare number of milliseconds | binding when introduced |

The ordering rule is the one that prevents the common failure: **an inner timeout must be shorter than its outer timeout.** When the model-provider timeout exceeds the API request timeout, the client gives up while the server keeps burning provider cost, and both the log and the bill make it look like a success.

## 14. Retries

**TARGET — no retry logic exists.** **M-05-1.2 Dispatch loop with retry and backoff** is the milestone.

| Rule | Detail |
|---|---|
| Retry only what is transient | A network fault, a throttle response, a deadlock. Never a validation failure, never a 4xx, never a policy refusal. |
| Retry only what is idempotent | See §16. Retrying a non-idempotent operation is how duplicates are created. |
| Exponential backoff with jitter | Fixed-interval retry from many callers synchronises them into a thundering herd. |
| Bounded attempts, then dead-letter | **M-05-1.3 Escalation and dead-letter** |
| Never retry silently | A retry that succeeded is a Warning; the fact that it was needed is the signal. |
| Never nest retries | Retry at one layer. A retry at three layers is 3ⁿ attempts and an outage amplifier. |
| Frontend | TanStack Query retries by default. Configure it deliberately in `app/queryClient.ts` — retrying a failed mutation such as `useCreateWorkspace` is rarely what is wanted. |

## 15. Transactions

| Rule | Detail |
|---|---|
| **One transaction per aggregate per request** | The aggregate is the consistency boundary. `SaveChangesAsync` on `NexusChatDbContext` is one transaction. |
| **Never span a transaction across an external call** | A transaction held open while a model provider is called holds database locks for the provider's latency. If two things must both happen, use an outbox (**M-01-8.1 In-process event bus**), not a longer transaction. |
| **Never span a transaction across a user interaction** | — |
| **Explicit transactions only for genuine multi-aggregate work**, and that is a signal the aggregate boundary may be wrong | — |
| **Read-only work needs no transaction** | — |
| **Concurrent inserts are the database's problem, and it solves them** | `Seq` is `IDENTITY(1,1)` and `Ref` is computed PERSISTED precisely because only the database guarantees uniqueness under concurrent insert. Do not add application-level locking to reproduce a guarantee you already have. |

Migrations are a separate transactional concern with their own rules — DATABASE_STANDARDS.md.

## 16. Idempotency

**TARGET — no idempotency mechanism exists.**

| Operation | Idempotent by nature? | Rule |
|---|---|---|
| `GET /api/v1/workspaces/{id:guid}` | yes | — |
| `PUT` / update | yes if the full state is supplied | Prefer full-state update over an incremental one |
| `DELETE` | yes | Deleting an absent entity is success, not an error |
| `POST /api/v1/workspaces` | **no** | Needs an idempotency key before retries exist |
| Model invocation | **no** — it costs money | `ModelInvocation` must be attributable and de-duplicable; **M-04-4.1 Per-turn cost attribution** |
| Automation job dispatch | **no** | **M-05-1.2 Dispatch loop with retry and backoff** |

**The rule to apply before §14 is implemented:** retries must not be introduced until the operations they wrap are idempotent. Retry without idempotency does not improve reliability, it multiplies the failure.

The mechanism, when it lands: a client-supplied key on the request; the server stores the key with the outcome; a repeat of the same key returns the stored outcome rather than acting again.

## 17. Resource disposal

| Language | Rule |
|---|---|
| **C#** | `using` or `await using` for anything `IDisposable`/`IAsyncDisposable`. A type holding a disposable field implements disposal itself. `NexusChatDbContext` is disposed by the DI container per scope — never dispose it manually. `NexusChatDbContextFactory` produces contexts for design-time tooling, and those *are* the caller's to dispose. |
| **C# — HttpClient** | Never `new HttpClient()` per call. It is registered once and reused; a per-call instance exhausts sockets under load. This applies to the provider adapters in `Nexus.Platform.Providers.OpenAI`. |
| **React** | Every `useEffect` that subscribes, opens or times returns a cleanup function. `ChatTelemetryContext.tsx` and `WorkspaceContext.tsx` must not leave listeners behind. |
| **TypeScript** | An `AbortController` is released when its request settles. |
| **SQL** | Connections are pooled and owned by the provider. Do not hold one across a request. |
| **Python (future)** | `async with`. |

## 18. Date and time

**CURRENT and settled: `DateTimeOffset`.**

| Language | Type | Rule |
|---|---|---|
| **C#** | `DateTimeOffset` | Never `DateTime`. A `DateTime` carries an ambiguous kind and the ambiguity always surfaces later, in the wrong timezone, in production. |
| **SQL** | `datetimeoffset` | Store the offset. Do not store local time. |
| **TypeScript** | ISO 8601 `string` on the wire | Parse at the edge, format at render, never compare formatted strings. |
| **React** | formatted at render | Formatting is a display concern and never round-trips into state. |
| **Python (future)** | timezone-aware `datetime` | — |

| Rule | Detail |
|---|---|
| **UTC everywhere except display** | Store UTC, transmit UTC, compute in UTC. The user's timezone is applied at render. |
| **Time comes from a clock abstraction, not `DateTimeOffset.UtcNow` scattered through code** | Otherwise nothing time-dependent is testable. Not yet present; introduce it with the first time-dependent behaviour test. |
| **`CreatedAt` / `UpdatedAt` are set once, by the layer that owns the write** | Never by the client. |
| **A duration is a `TimeSpan`, never an `int`** | An `int` named `timeout` is a defect waiting for someone to guess seconds when it meant milliseconds. |
| **Compare instants, never formatted strings** | — |

## 19. Money

**TARGET — no monetary type exists in Nexus today. The concept arrives with model cost.**

| Rule | Detail |
|---|---|
| **Never `float` or `double`** | Binary floating point cannot represent decimal currency exactly. |
| **C#: `decimal`, and it never travels alone** | Amount and currency code together, as a record. |
| **SQL: `decimal(19,4)`**, with the currency in its own column | Never `float`, never `money` |
| **TypeScript: an integer of minor units plus a currency code** | JavaScript has one number type and it is a double. Sending `12.34` as a number and doing arithmetic on it in the client is how rounding errors reach an invoice. |
| **ISO 4217 currency codes** | `USD`, `GBP` — never a symbol |
| **Round once, at the point of presentation or settlement** | Never in intermediate arithmetic |

Where this lands: `ModelUsage`, `UsageRecord`, `UsageSummary` and `IUsageMeter` currently carry usage, not cost. Cost attribution is **M-04-4.1 Per-turn cost attribution**; run cost is **M-07-3.3 Model assignment and run cost**; monitoring is **M-10-4.1 Cost monitoring**. All three depend on this rule being right the first time, because a money type is not retrofittable once values are stored.

## 20. Units

| Rule | Detail |
|---|---|
| **The unit is in the name or in the type — never in a comment** | `timeoutMilliseconds`, or a `TimeSpan`. Never `timeout` with a comment saying "in ms". |
| **Prefer the type** | `TimeSpan` beats `delayMs`. A strongly-typed unit cannot be passed to the wrong parameter. |
| **One canonical unit per quantity, converted only at the edges** | Bytes for size, milliseconds for latency, tokens for model usage. |
| **Tokens are the model unit and are never conflated with cost** | `ModelUsage` counts tokens; cost is derived from tokens by a rate that changes independently. Storing cost as if it were a property of usage makes historical recalculation impossible. |
| **A percentage is a ratio `0..1` in code and a percentage only at render** | Mixing the two is a silent factor-of-100 error. |

## 21. Identifiers

| Kind | Type | Rule |
|---|---|---|
| **Primary identity** | `Guid` / `uniqueidentifier`, always column `Id` | Generated in C#, not by the database. Single-column; no composite keys. |
| **Strongly-typed id** | `WorkspaceId`, `ConversationMessageId`, `WorkItemId` | One per aggregate. The whole point is that a `ConversationId` cannot be passed where a `WorkspaceId` is expected — a class of bug the compiler removes entirely. |
| **Conversion to the database** | `StronglyTypedIdConverters.cs` | Lives **only** in `Nexus.Products.Chat.Infrastructure/Sql/Conventions/`. The domain never knows how its ids are stored. |
| **Human-readable reference** | `Ref`, computed PERSISTED in the database | `WKS-00000001` from `('WKS-' + RIGHT('00000000' + CAST([Seq] AS varchar(8)), 8))`. Display and search only. |
| **Allocation counter** | `Seq`, `int IDENTITY(1,1)`, an EF Core shadow property | Feeds `Ref`. Never a key, never a foreign key, never exposed, never used for business ordering. |
| **Opaque cross-boundary reference** | `ScopeRef`, `ActorRef` | AI never sees product structure — it receives a `ContextBundle`, and `ScopeRef` is opaque to it. Do not "helpfully" widen these into typed product references; the opacity is an architectural invariant. |
| **Frontend** | `string` | An id is transported and compared, never parsed or ordered. |

**Never use `Ref` as a key and never use `Id` for display.** They are separate for separate reasons: `Id` is stable and meaningless, `Ref` is meaningful and generated. Collapsing them loses one property or the other.

## 22. Concurrency

| Concern | Rule |
|---|---|
| **`DbContext` is not thread-safe** | One `NexusChatDbContext` serves one logical operation at a time. Two concurrent operations on one context throw, and the exception names the wrong cause. |
| **Optimistic concurrency, not pessimistic** | A concurrency token on the aggregate, a conflict surfaced to the caller. **TARGET** — no concurrency token exists yet. |
| **Uniqueness under concurrent insert belongs to the database** | This is the reasoning behind the whole Id/Seq/Ref pattern, and it is the one architectural decision in the data layer that has been proven against a running server: two successive inserts on 2026-08-20 at 18:09 UTC each returned a server-generated `Ref` and `Seq`. |
| **Singletons must be stateless or genuinely thread-safe** | `AgentRegistry` and `PromptAssembler` are shared across requests. Mutable state in either is a race. |
| **`InMemory*` implementations are not concurrency-safe by assumption** | `InMemoryMemoryStore`, `InMemoryUsageMeter`, `InMemoryTurnTraceStore`, `InMemoryResultReportStore` are placeholders. Do not build concurrency guarantees on them; build the durable implementation. |
| **React has no shared mutable state** | Server state is TanStack Query's; the last write from a mutation wins, and correctness comes from invalidating the query, not from locking. |

**Concurrency at the level of work, not threads** — **M-07-2.2 Parallel-safety rules**. Two work items may run simultaneously only if all five hold: no transitive dependency path, no file or project scope overlap, **no shared schema mutation**, no contract mutation on a shared boundary, and not both high risk.

The third is the one most often got wrong: **two EF migrations on one `DbContext` conflict on the model snapshot even when they touch different tables.** Two workers migrating `Conversation` and `Knowledge` in parallel against `NexusChatDbContext` will both succeed locally and conflict on merge, in a generated file, in a way that is not resolvable by reading the diff. Schema work on one context is sequential. Classification vocabulary is fixed: *Can run now*, *Can run together*, *Blocked*, *Waiting for dependency*, *High conflict risk*, *Must be sequential*.

---

## 23. Pagination

**TARGET — no pagination exists.** Every list endpoint returns everything: `ListConversationsResponse`, the workspaces list behind `useWorkspaces.ts`, the projects list behind `useProjects.ts`. This works at current data volumes and will not.

| Element | Rule |
|---|---|
| Style | Cursor-based, not offset-based. Offsets skip and duplicate rows when the underlying set changes between pages. |
| Request | `?cursor=<opaque>&pageSize=<n>`, camelCase query parameters |
| `pageSize` | Has a default and a hard maximum. An unbounded `pageSize` is an availability risk, not a feature. |
| Cursor | Opaque to the client. It encodes a stable sort key — `Seq` is the natural candidate, being monotonic per table, and this is its only legitimate read use. |
| Response | `items` plus `nextCursor`; `nextCursor` absent means the end. Never a total count unless the caller needs one and the query can produce it cheaply. |
| Frontend | TanStack Query's infinite-query support, in the existing hook. Do not add a parallel fetching mechanism. |

Introduce this before the first list grows, not after. Adding pagination to a live endpoint is a contract break requiring `/api/v2`.

## 24. Sorting

| Element | Rule |
|---|---|
| **Every list has a deterministic order, always** | A query with no `ORDER BY` has no guaranteed order, and an unordered list cannot be paginated correctly. |
| **Default sort is documented per endpoint** | Newest-first on `CreatedAt` for conversational data; alphabetical for reference data. |
| **Tie-break on a unique column** | `ORDER BY CreatedAt DESC, Seq DESC`. Two rows with the same instant otherwise swap places between pages. |
| **Client-requested sort is an allow-list** | `?sort=createdAt&direction=desc`. Never a client-supplied column name reaching SQL. |
| **Every sortable column is indexed** | An unindexed sort is a table scan that appears only under production data volume. |
| **Sorting is the server's job** | The client sorts only what it already holds in full. Sorting a page in the browser produces an order that is wrong across pages. |

## 25. Filtering

| Element | Rule |
|---|---|
| **Filters are query parameters, camelCase** | `?status=Active&workspaceId=<guid>` |
| **The filterable set is an allow-list per endpoint** | No generic query language on the wire. |
| **Filtering happens in the database** | Fetching all rows and filtering in C# or in the browser is the most common cause of an endpoint that is fast in development and unusable in production. |
| **Filter values are always parameters** | EF Core parameterises. Any hand-written SQL must too. String concatenation into SQL is never acceptable, in any context, including migrations. |
| **Scope is not a filter** | Tenant and workspace scoping is a security boundary applied by the query, never an optional parameter the client may omit. |
| **Absent filter means unfiltered within scope** | Never means "no results". |
| **Text search is not `LIKE '%term%'`** | It cannot use an index. Real search is **M-02-4.1 Full-text and structured search**; until then, keep text filters to prefix matching and say so. |

---

## 26. Applying these rules

| Situation | What to do |
|---|---|
| A rule conflicts with existing code | The rule wins for new code. Existing code is corrected when touched, in the same change, and only within the scope of that change. |
| A rule conflicts with a language idiom | The idiom wins, and this document is corrected. The `Async` suffix rule differing between C# and TypeScript is exactly this. |
| No rule covers the situation | Follow the nearest analogue here, and add the rule in the same pull request. |
| A rule is marked TARGET and the code needs it now | Implement it at the named milestone, or move the milestone. Do not implement half of it — half a retry policy, half an idempotency key or half a timeout ordering is worse than none. |
| A rule would be violated to hit a date | Record it as a defect against the milestone. Undeclared debt is the only kind that compounds. |

## 27. Related documents

| Document | Owns |
|---|---|
| CSHARP_STANDARDS.md | How these rules are written in C# |
| TYPESCRIPT_REACT_STANDARDS.md | How these rules are written in TypeScript and React |
| DATABASE_STANDARDS.md | Schema, migrations, Id/Seq/Ref, cascade behaviour |
| NAMING_STANDARDS.md | The name of every artefact |
| TECHNOLOGY_STACK.md | Which technologies are approved and which are not selected |
| STACK_VERSION_POLICY.md | Versions, upgrades, deprecation |
