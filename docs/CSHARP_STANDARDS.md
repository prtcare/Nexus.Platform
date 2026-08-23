# C# Standards

> **Status:** CURRENT — every rule below is derived from code that exists and compiles; TARGET items are marked
> **Owner:** Layer 07 DEVELOPER (definition) / Layer 09 ASSURANCE (conformance)
> **Last updated:** 2026-08-21
> **Layer:** Cross-cutting
> **Authoritative for:** how C# is written in Nexus — type selection, async, nullability, LINQ, exceptions, DI, options, domain modelling, DTOs, API handlers, serialization and identifiers

**Scope.** *What* a rule means across languages is **CODE_CONVENTIONS.md**; *what* things are called is **NAMING_STANDARDS.md**; *how the database is shaped* is **DATABASE_STANDARDS.md**. This document is only the C# form. Nothing here is restated from those.

All C# targets `net10.0`. See TECHNOLOGY_STACK.md.

---

## 1. Namespace structure

**The namespace equals the project name plus the folder path. No exceptions exist in the codebase and none is permitted.**

```csharp
// Nexus.Products.Chat.Infrastructure/Sql/Repositories/SqlWorkspaceRepository.cs
namespace Nexus.Products.Chat.Infrastructure.Sql.Repositories;
```

| Rule | Detail |
|---|---|
| File-scoped declarations | `namespace X;` — never a braced block. |
| One public type per file | The file is named for it. Small private helper types may share the file. |
| `using` placement | Outside the namespace, sorted, `System.*` first. |
| No global usings beyond what `Directory.Build.props` sets | An implicit using that only some readers know about makes a file unreadable in isolation. |
| No `using static` | Except in a test file where it materially improves an assertion. |

**The namespace-equals-type collision is accepted.** `Nexus.Products.Chat.Domain.Workspace` is both the namespace of the aggregate folder and the name of the type inside it. This falls out of one-folder-per-aggregate and is not worked around by pluralising the folder. Where the compiler needs help, qualify fully.

**Layer discipline is a namespace rule before it is anything else.** `Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never reference product types. A `using Nexus.Products.Chat.Domain...` appearing in either is the exact failure `PlatformBoundaryTests.cs` and `BoundaryRuleTests.cs` exist to catch.

---

## 2. Class versus record

| Choose | When | Real examples |
|---|---|---|
| **`record`** | Data with no identity of its own. Value equality is correct. Immutable. | `ContextBundle`, `ContextItem`, `Citation`, `ModelDescriptor`, `ModelInvocation`, `ModelUsage`, `ToolDescriptor`, `ToolResult`, `AuditEntry`, `UsageRecord`, `QuotaVerdict`, `ResolvedIdentity`, `PlanStep`, `DecisionTrace`, `MemoryQuery`, `RankingOptions`, `AssembledPrompt`, and every `<Verb><Name>Request` / `<Verb><Name>Response` |
| **`readonly record struct`** | A small identifier or value wrapping one field | `WorkspaceId`, `ConversationMessageId`, `WorkItemId` — see §17 |
| **`class`** | Something with identity, a lifecycle, or behaviour and dependencies | the 11 aggregate roots (`Workspace`, `Conversation`, `WorkItem`, …), `Entity`, `AggregateRoot`, and every service — `SqlWorkspaceRepository`, `RoutingModelGateway`, `KeywordContextRanker`, `TurnPipeline` |
| **`sealed class`** | The default for any class not designed for inheritance | every service class |
| **`static class`** | Extension methods and pure converters only | `StronglyTypedIdConverters`, `IntelligenceServiceCollectionExtensions`, every `*Endpoint` file |

**The test is identity, not size.** Two `ContextItem` values with the same content are the same context item — record. Two `Workspace` instances with the same name are different workspaces — class. Getting this wrong produces equality behaviour that is silently wrong in a dictionary.

**Records are immutable.** `init` accessors, positional parameters, `with` for derivation. A record with a settable property is a class that has not admitted it.

**`sealed` unless designed for inheritance.** `AggregateRoot` and `Entity` are the base types; nothing else in the codebase is inherited from, and nothing new should be without a written reason. Composition first — `AggregatingModelCatalog` and `RoutingModelGateway` compose rather than inherit, and that is the pattern.

---

## 3. Interfaces

| Rule | Detail |
|---|---|
| An interface exists because there are, or will imminently be, **two implementations** | `IModelGateway` has an OpenAI implementation and an Anthropic one. `IContextRanker` has `KeywordContextRanker` and will have a semantic one at **M-02-4.2**. |
| An interface extracted purely to enable mocking is a smell | Prefer a pure function that needs no mock — `KeywordContextRanker` is testable with no test double at all. |
| Keep them small | `ISecretResolver` resolves a secret. An interface with eight unrelated members is several interfaces. |
| No default interface implementations | They hide behaviour from the implementing type. |
| Interfaces do not carry state, constants or static members | — |
| `CancellationToken` on every async member, **no default value** | A default on the interface lets an implementation quietly ignore the token. |
| Return `IReadOnlyList<T>`, never `IEnumerable<T>`, from a repository | An unmaterialised sequence escaping the `DbContext` scope fails at enumeration, far from the cause. |

**Placement is a design decision, not a convenience.** An interface belongs where the *abstraction* belongs, not where its implementations do — that is why `IWorkspaceRepository` sits in `Nexus.Products.Chat.Domain/Workspace/` while `SqlWorkspaceRepository` sits in Infrastructure. The domain declares what it needs; infrastructure supplies it. Reversing this inverts the dependency direction and the architecture tests fail. See NAMING_STANDARDS.md §7 for the full placement table.

---

## 4. Async and await

| Rule | Detail |
|---|---|
| `Async` suffix on every `Task`/`ValueTask`-returning method | `GetByIdAsync`, `SaveAsync` |
| Never `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` | Blocking on async in an ASP.NET Core host deadlocks under load |
| Never `async void` | Except a genuine event handler; none exists in Nexus. An exception in `async void` cannot be caught and terminates the process. |
| No `ConfigureAwait(false)` | ASP.NET Core has no synchronisation context. Adding it is noise. |
| `Task` for public APIs; `ValueTask` only where a hot path is measured | Unmeasured `ValueTask` is premature and easy to misuse — it may be awaited only once. |
| Do not wrap sync work in `Task.Run` to look async | It moves the work to a thread-pool thread and adds latency |
| A method with no `await` should not be `async` | Return `Task.FromResult` or `Task.CompletedTask` |
| Concurrency by `Task.WhenAll` only for genuinely independent work | Never for work sharing a `NexusChatDbContext` — it is not thread-safe and throws |

**Streaming.** `IAsyncEnumerable<T>` for a genuine stream, not for a bounded list. Model-response streaming is **M-11-7.1 Streaming and realtime presence**; nothing in the current codebase streams.

---

## 5. CancellationToken

**This is already correct in the real code and must not regress.** `CancellationToken` is threaded through the repository interfaces in `Nexus.Products.Chat.Domain` — `IWorkspaceRepository` and its ten siblings.

```csharp
Task<Workspace?> GetByIdAsync(WorkspaceId id, CancellationToken cancellationToken);
```

| Rule | Detail |
|---|---|
| Always the last parameter, always named `cancellationToken` | — |
| No default value on an interface member | — |
| Passed onward to every call that accepts one | EF Core, `HttpClient`, the OpenAI SDK — all accept one. A token that stops halfway down is worse than none, because cancellation appears to work. |
| Minimal API handlers accept it as a parameter | ASP.NET Core supplies the request-aborted token by model binding |
| Do not create a token where one was given | Link with `CreateLinkedTokenSource` when adding a timeout (**TARGET** — see CODE_CONVENTIONS.md §13) |
| Check `ThrowIfCancellationRequested()` only in a long CPU-bound loop | `ToolLoop` is the shape where this applies |
| Cancellation leaves state consistent | A cancelled operation does not half-commit; the transaction rules in CODE_CONVENTIONS.md §15 do the work |

---

## 6. LINQ

| Rule | Detail |
|---|---|
| Method syntax, not query syntax | The codebase uses method syntax throughout. |
| One statement, one thought | A chain of more than four operators becomes a named local or a method. |
| **Know whether you are in `IQueryable` or `IEnumerable`** | The single most consequential LINQ rule in this codebase. An `AsEnumerable()` or a `ToList()` too early moves filtering from SQL Server into process memory, and the endpoint gets slower in exact proportion to the data. |
| Materialise once, deliberately, with `ToListAsync(cancellationToken)` | Never `ToList()` on an EF query. |
| No side effects in a LINQ expression | `Select` projects; it does not mutate or log. |
| No `.Result` inside a lambda | See §4. |
| `Any()` not `Count() > 0`; `FirstOrDefault` with an explicit null check, never `First` on a query that can be empty | — |
| Prefer projection to the shape you need | Loading whole aggregates to read one property fetches every column. |
| A custom expression a query provider cannot translate throws at runtime, not compile time | Keep query-side expressions simple. `KeywordContextRanker` ranks in memory and is free of this constraint precisely because it is not a query. |

---

## 7. Nullability

Nullable reference types are enabled repository-wide via `Directory.Build.props`.

| Rule | Detail |
|---|---|
| An annotation is a domain statement | `Workspace?` means "may not exist". `Task<Workspace?> FindByIdAsync` and `Task<Workspace> GetByIdAsync` say different things and both are legitimate — CODE_CONVENTIONS.md §4. |
| `!` (null-forgiving) only immediately after a check the compiler cannot see, with a comment saying which | A `!` that silences a warning is a null reference exception with a delay |
| Never `#nullable disable` | If a file cannot be annotated, that is the work |
| Nullable warnings are errors | Via `TreatWarningsAsErrors` in `Directory.Build.props` |
| EF Core navigation properties | Required references are non-nullable and configured as required; optional ones are nullable. The C# annotation and the `IEntityTypeConfiguration` must agree — DATABASE_STANDARDS.md |
| `ArgumentNullException.ThrowIfNull` at a public boundary that a non-annotated caller can reach | Not on every internal method — CODE_CONVENTIONS.md §7 |

---

## 8. Exceptions

| Rule | Detail |
|---|---|
| Exceptions are for faults, not for expected outcomes | `QuotaVerdict`, `ToolResult`, `TurnError` and `ResultOutcome` exist so that refusal, failure-to-act and error-with-reason are **values**. Never throw to signal one. |
| Throw the most specific type available | `ArgumentException`, `InvalidOperationException`, or a domain exception. Never bare `Exception`. |
| A domain exception is `sealed`, ends in `Exception`, and carries the identifiers needed to diagnose it | — |
| `catch` only what you can act on | `catch (Exception)` is permitted only at a host boundary that converts it to a response |
| `throw;` to rethrow — never `throw ex;` | `throw ex;` resets the stack trace and destroys the diagnosis |
| Never swallow | A caught exception is handled, logged with context, or rethrown |
| Never use exceptions for flow control | — |
| Message names what failed and what was attempted, and contains no secret, token or prompt body | CODE_CONVENTIONS.md §§6, 11 |
| No exception filter with a side effect | `when (…)` is a predicate |

---

## 9. Dependency injection

Constructor injection, ASP.NET Core's built-in container. Concept and lifetime rules are CODE_CONVENTIONS.md §10; the C# form:

```csharp
public sealed class SqlWorkspaceRepository : IWorkspaceRepository
{
    private readonly NexusChatDbContext _context;

    public SqlWorkspaceRepository(NexusChatDbContext context) => _context = context;
}
```

| Rule | Detail |
|---|---|
| Dependencies are `private readonly` fields assigned only in the constructor | — |
| More than four dependencies means the type has more than one job | — |
| Never inject `IServiceProvider` into a domain or application type | It hides the dependency graph and defeats every architecture test |
| No service locator, no static container access | — |
| Registration is grouped per area | `IntelligenceServiceCollectionExtensions.Add<Area>(this IServiceCollection services)`; `ChatProductModule` composes the Chat product |
| `Program.cs` composes modules; it does not register services one at a time | The module seam is what lets a product be added without editing the host |
| Register against the interface, resolve the interface | `services.AddScoped<IWorkspaceRepository, SqlWorkspaceRepository>()` |
| Never inject a scoped service into a singleton | A captured `NexusChatDbContext` outlives its request and corrupts change tracking |

---

## 10. Options and configuration

| Rule | Detail |
|---|---|
| Bind a section to a `record`, never read `IConfiguration` in business code | — |
| The section name is a `const` on the options type | One literal, one place |
| Inject `IOptions<T>` for static configuration; `IOptionsMonitor<T>` only where reload is genuinely needed | — |
| Validate on start, not on first use | A missing configuration value must fail the host at startup, not the first request an hour later |
| Never a secret in configuration | `set-openai-key.ps1` handles the OpenAI key today; **TARGET: `ISecretResolver` — M-01-5.1 Real secret resolver** |
| Never `Environment.GetEnvironmentVariable` directly | It bypasses the configuration pipeline and the options type |

Key naming is NAMING_STANDARDS.md §14. The GOVERNANCE register of keys is **M-03-6.1 Configuration registry**.

---

## 11. Logging

**No logging library is selected — TECHNOLOGY_STACK.md §7, closed by M-10-1.1 Correlation across hosts.** What to log is CODE_CONVENTIONS.md §11. The two C#-specific rules that hold whatever is chosen:

| Rule | Detail |
|---|---|
| Take `ILogger<T>` by constructor injection | The generic argument supplies the category; never construct a logger |
| Message templates with named placeholders, never interpolated strings | `"Workspace {WorkspaceId} created"` keeps `WorkspaceId` queryable. `$"Workspace {id} created"` destroys it before the logger sees it. |

`ConsoleAuditLog` is **not** logging. It implements `IAuditLog`, which is a durable governance record — **M-01-4.1 Durable audit log** replaces the console implementation. Do not route audit entries through `ILogger` or log lines through `IAuditLog`.

---

## 12. EF Core

**DATABASE_STANDARDS.md owns this subject.** It defines schema and table naming, the Id/Seq/Ref pattern, the `Ref` computed PERSISTED column, cascade behaviour under SQL Server error 1785, migration rules and value converters. None of it is repeated here.

The four rules that are about *C# code* rather than about the database:

| Rule | Detail |
|---|---|
| **EF Core types never leave Infrastructure** | `NexusChatDbContext` appears in `Nexus.Products.Chat.Infrastructure` and nowhere else. No `DbSet`, no `DbContext`, no EF attribute in Domain, Application or Api. The domain declares `IWorkspaceRepository`; Infrastructure supplies `SqlWorkspaceRepository`. |
| **Configuration is `IEntityTypeConfiguration<T>`, one class per aggregate, never attributes** | `WorkspaceConfiguration.cs` is the reference implementation. Data annotations on a domain class put persistence concerns in the domain. |
| **Strongly-typed id and enum converters live only in `Conventions/StronglyTypedIdConverters.cs`** | The domain never knows how its identifiers are stored |
| **A migration is generated, reviewed, and committed with the configuration change that produced it** | Never hand-edited to make a merge work. Two migrations on one `DbContext` conflict on the model snapshot even when they touch different tables — CODE_CONVENTIONS.md §22 and **M-07-2.2 Parallel-safety rules**. |

`NexusChatDbContextFactory` exists for design-time tooling only. Nothing at runtime uses it.

---

## 13. Domain entities

The base types exist in `Nexus.Products.Chat.Domain/Common/`: **`AggregateRoot`**, **`Entity`**, **`IRepository`**.

| Type | Role |
|---|---|
| `Entity` | Has identity; equality is by identity, not by value |
| `AggregateRoot` | An `Entity` that is a consistency boundary and the only thing a repository loads or saves |
| `IRepository` | The persistence contract the domain declares |

**The aggregate folder is a fixed shape** — `<Name>.cs`, `<Name>Id.cs`, `<Name>Status.cs`, `I<Name>Repository.cs` — for all eleven: `Adr`, `Artifact`, `Branch`, `Conversation`, `ConversationMessage`, `Knowledge`, `Project`, `Session`, `Snapshot`, `WorkItem`, `Workspace`.

| Rule | Detail |
|---|---|
| **Private constructor plus two named factories** | `public static <Name> Create(...)` applies invariants and produces something new. `public static <Name> Restore(...)` rehydrates a row that already exists and applies **none**. Both are observed in the codebase; keeping them separate is what allows a schema change to load old data. |
| **All state private-set; mutation only through intention-revealing methods** | `workspace.Rename(newName)`, not `workspace.Name = newName`. A public setter makes every invariant advisory. |
| **The aggregate protects its own invariants** | If `Workspace` requires a non-empty name, no code path may produce one without it. |
| **One repository per aggregate root; never a repository for a child entity** | `ConversationMessage` is loaded through its parent aggregate where it is owned. |
| **No infrastructure in Domain** | No EF Core, no `HttpClient`, no configuration, no logging framework. |
| **Status is an enum on the aggregate, transitions are methods** | `WorkspaceStatus` changes only via a method that validates the transition. |
| **Domain events are TARGET** | No event type exists. **M-01-8.1 In-process event bus**; grammar in NAMING_STANDARDS.md §26. |

---

## 14. DTOs

| Rule | Detail |
|---|---|
| A `record`, immutable, no `Dto` suffix | `CreateWorkspaceRequest`, `GetWorkspaceResponse`, `ListConversationsResponse` |
| Lives beside the endpoint that uses it | A DTO shared by two endpoints usually means one of them is modelled wrong |
| **A domain type is never serialised** | `Workspace` never reaches the wire; `GetWorkspaceResponse` does. Serialising an aggregate exports its internals and freezes them into a public contract. |
| Mapping is explicit, hand-written, in one direction, in one place | No mapping library. The two behaviour tests in the whole system include a mapper test — `Chat/ChatContextBundleMapperTests.cs` — which is what makes explicit mapping worth its cost. |
| Flat where possible | Nesting on the wire couples the client to the domain's shape |
| Only a genuine cross-repository contract belongs in a `.Contracts` project | `IntelligenceTurnRequest` qualifies. `CreateWorkspaceRequest` does not. |
| A strongly-typed id serialises as its underlying `Guid` | See §17 |

---

## 15. Validation

Concept and layering: CODE_CONVENTIONS.md §9. The C# form:

| Layer | Form |
|---|---|
| Endpoint | A validation function beside the endpoint, returning a problem response. `TurnRequestValidation` in `Nexus.Intelligence.Api/Endpoints/` is the reference. |
| Domain | Inside `Create`, throwing a domain exception. Never inside `Restore`. |
| Database | The `IEntityTypeConfiguration` and the migration |

No validation library is in use. Do not introduce one without an ADR (next: **ADR-016**) and a TECHNOLOGY_STACK.md entry.

`PolicyGate` and `IQuotaPolicy` are **not** validation — they decide whether a well-formed operation is permitted, and they return `QuotaVerdict` rather than throwing.

---

## 16. Testing

**State the position plainly: exactly two behaviour tests exist in the entire system** — `Ranking/KeywordContextRankerTests.cs` and `Chat/ChatContextBundleMapperTests.cs`. `Nexus.Platform.Tests` is a `.csproj` containing zero `.cs` files. The test framework declared by those `.csproj` files was not verified and is therefore **not named anywhere in this documentation set** — read it from the project file before writing a new test.

| Rule | Detail |
|---|---|
| File `<TypeUnderTest>Tests.cs`; folder mirrors the type's folder | `Ranking/KeywordContextRankerTests.cs` mirrors `Nexus.Intelligence.Context/Ranking/` |
| Method `<Method>_<Condition>_<ExpectedResult>` | — |
| Arrange / Act / Assert, one behaviour per test | — |
| Prefer a pure type that needs no test double | The reason `KeywordContextRanker` is one of the two things tested is that it is pure |
| A test never touches a real database, a real provider or the network | An integration test that does is a different category and needs an environment — **M-08-4.1 Environment model** |
| Architecture tests are not optional | `PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs`, `BoundaryTests.cs` are the only automated defence the architecture has. An upgrade or refactor that requires *editing* them is an architecture change needing an ADR. |
| Tests do not run in CI | There is no CI — `NexusAI\.github\workflows\` is empty; the other two repositories have no `.github` at all. **M-08-1.2 Pipelines on every repository**, blocking at **M-08-1.4**. |

---

## 17. API handlers

**Minimal APIs only. No MVC controller exists in any repository and none may be added.**

The fixed shape, observed in both `Endpoints/` folders:

```csharp
// Nexus.Products.Chat.Api/Endpoints/WorkspacesEndpoint.cs
internal static class WorkspacesEndpoint
{
    public static void MapWorkspacesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces");

        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
    }
}
```

| Rule | Detail |
|---|---|
| One `static class` per resource, `Map<Name>Endpoints(this IEndpointRouteBuilder app)` | The whole codebase uses this shape |
| A handler orchestrates; it does not contain business logic | It binds, delegates, and maps to a response |
| Handlers accept `CancellationToken` by model binding | — |
| Route constraints are mandatory | `{id:guid}` — an unconstrained route parameter defers a 400 into a parse failure deeper in |
| Return typed results | `Results<Ok<GetWorkspaceResponse>, NotFound>` — the OpenAI-facing document generated by Swashbuckle is only as good as the declared result types |
| Route strings live here and nowhere else | NAMING_STANDARDS.md §24 |
| Status codes are meaningful | `201` with a location for a create, `204` for a delete, `409` for a concurrency conflict, `400` for a validation failure, `404` for a missing entity |
| Never expose a domain type or an EF entity in a response | §14 |
| `Program.cs` composes; endpoints are mapped through the product module | `ChatProductModule` |

`HealthEndpoint` is the pattern for a liveness surface; formal health checks are **M-10-2.1 Health checks**.

---

## 18. Serialization

`System.Text.Json`. No Newtonsoft.Json anywhere, and it must not be introduced.

| Rule | Detail |
|---|---|
| Options configured once, at the host | Never per call site |
| camelCase property names on the wire | The frontend is TypeScript; the API is its only source of shape |
| Enums serialise as **strings**, not integers | An integer enum on the wire breaks silently when a member is inserted. `TrustLevel`, `AgentType`, `ContextItemKind`, `ResultOutcome`, `SideEffectClass` all cross the wire and all serialise as strings. |
| A strongly-typed id serialises as its underlying `Guid` | A converter per id type, registered centrally. The client sees a plain string. |
| `DateTimeOffset` serialises as ISO 8601 with the offset | §19 |
| No polymorphic serialization without an explicit discriminator | — |
| No `[JsonIgnore]` on a domain type to make it safe to serialise | That is the signal that a DTO is missing |
| Deserialization never trusts input | Validation is §15 |

---

## 19. Date and time

**`DateTimeOffset` everywhere. Never `DateTime`.** Rationale and the cross-language rules are CODE_CONVENTIONS.md §18.

| Rule | Detail |
|---|---|
| `DateTimeOffset` in domain, DTOs and configuration | Maps to `datetimeoffset` |
| A duration is a `TimeSpan`, never an `int` | An `int` named `timeout` is an unresolved ambiguity between seconds and milliseconds |
| Time comes from an injected clock, not `DateTimeOffset.UtcNow` scattered through code | **TARGET** — no clock abstraction exists. Introduce one with the first time-dependent behaviour test; without it, that test cannot be written. |
| `CreatedAt`/`UpdatedAt` are set by the layer that owns the write, never by the client | — |
| Compare instants, never formatted strings | — |

---

## 20. GUIDs and strongly-typed identifiers

**Every aggregate has its own identifier type.** `WorkspaceId`, `ConversationId`, `ConversationMessageId`, `WorkItemId`, `ProjectId`, `AdrId`, `ArtifactId`, `BranchId`, `KnowledgeId`, `SessionId`, `SnapshotId` — one `<Name>Id.cs` per aggregate folder.

```csharp
public readonly record struct WorkspaceId(Guid Value)
{
    public static WorkspaceId New() => new(Guid.NewGuid());
}
```

| Rule | Detail |
|---|---|
| `readonly record struct` over a single `Guid` | Value equality, no allocation, and — the point — a `ConversationId` cannot be passed where a `WorkspaceId` is expected. An entire class of bug becomes a compile error. |
| Generated in C#, not by the database | `Id` is `uniqueidentifier` and is supplied on insert |
| A raw `Guid` never crosses a domain boundary | Only DTOs and route parameters carry raw `Guid`s, and they convert immediately |
| Conversion lives only in `Sql/Conventions/StronglyTypedIdConverters.cs` | The domain does not know how it is stored |
| `Ref` and `Seq` are never identifiers in C# | `Ref` (`WKS-00000001`) is display and search; `Seq` is a database shadow property for allocation. Neither is ever a key, a foreign key, or a parameter type. DATABASE_STANDARDS.md. |
| `ScopeRef` and `ActorRef` are deliberately opaque | AI receives a `ContextBundle` and never sees product structure. Do not widen them into typed product references — that invariant is why the AI layer can be reused. |

---

## 21. Related documents

| Document | Owns |
|---|---|
| CODE_CONVENTIONS.md | Cross-language rules — error handling, cancellation, transactions, money, pagination |
| NAMING_STANDARDS.md | Every name |
| DATABASE_STANDARDS.md | Schema, migrations, Id/Seq/Ref, cascade behaviour, converters |
| TYPESCRIPT_REACT_STANDARDS.md | The other side of every API contract |
| TECHNOLOGY_STACK.md | What is approved, what is being removed, what is not selected |
| STACK_VERSION_POLICY.md | Runtime and package versions |
