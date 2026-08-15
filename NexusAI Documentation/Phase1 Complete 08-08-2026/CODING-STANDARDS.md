# CODING-STANDARDS.md

C#/.NET naming and style conventions, derived from the patterns already established across the codebase. The goal is consistency with what's already there, not a generic style guide — follow these because the existing code follows them, not the other way around.

## Project-Wide Settings

Set once in `Directory.Build.props`, inherited by every project:
```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<LangVersion>latest</LangVersion>
```
Target framework: **.NET 10** across every project. Nullable reference types are on — use `string?` / `T?` deliberately, not `#nullable disable`.

## Namespaces

- **File-scoped namespaces only**: `namespace NexusAI.Domain.Workspace;` — never the braced block style.
- The namespace must match the project the file physically lives in (see [CONVENTIONS.md](./CONVENTIONS.md) for the "Namespace / Physical Location Alignment" rule and the current known violations of it).

## Types: Entities vs. Records vs. Interfaces

**Domain entities** are `sealed class`, with a public constructor that enforces required state, private setters for anything mutable, and explicit behavior methods rather than public setters:
```csharp
public sealed class Workspace : AggregateRoot<WorkspaceId>
{
    public Workspace(WorkspaceId id, string name, DateTimeOffset createdAt) : base(id)
    {
        Rename(name);
        CreatedAt = createdAt;
        Status = WorkspaceStatus.Active;
    }

    public string Name { get; private set; } = string.Empty;
    public WorkspaceStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    public void Rename(string name) { /* validation + assignment */ }
    public void Archive() => Status = WorkspaceStatus.Archived;
}
```
Never expose a public setter on a domain entity — state changes go through named methods (`Rename`, `Archive`, `ChangeStatus`), even if the method currently does no validation.

**Commands, Results, Requests, Responses, and other immutable data carriers** are `sealed record` (or positional `record`), not classes:
```csharp
public sealed record CreateWorkspaceCommand(string Name);
public sealed record CreateWorkspaceResult(WorkspaceId WorkspaceId, string Name);
```
For DTOs where every property must be set but positional syntax isn't used, use `required` + `init`:
```csharp
public sealed class ChatResponse
{
    public required string Text { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
}
```

**Interfaces** are always `I{Name}`, one per file, filename matching exactly (`IWorkspaceRepository.cs` contains only `IWorkspaceRepository`).

**Enums** are plain `public enum`, explicit numeric values always assigned (never left implicit) — see [CONVENTIONS.md](./CONVENTIONS.md) for the numbering rule (start at 1, reserve 0).

## Handlers and Services

Handlers are `sealed class`, constructor-injected, with a single public `HandleAsync` method:
```csharp
public sealed class CreateWorkspaceHandler
{
    private readonly IWorkspaceRepository _repository;

    public CreateWorkspaceHandler(IWorkspaceRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateWorkspaceResult> HandleAsync(
        CreateWorkspaceCommand command,
        CancellationToken cancellationToken = default)
    {
        // ...
    }
}
```
- Private fields are `_camelCase`, one per injected dependency, assigned directly in the constructor — no property wrapping, no `Guard.Against` library, just direct assignment.
- Handlers are **not** resolved through a mediator (no MediatR-style `ISender.Send()`) — they're registered directly in DI and injected wherever needed (an Api endpoint delegate, another handler, or `Host`).

## Async Conventions

- Every async method name ends in `Async`.
- `CancellationToken cancellationToken = default` is always the **last** parameter, even on interface methods.
- Always `await` — no `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` anywhere in the codebase; keep it that way.

## Formatting

- **One parameter per line** once a method call or declaration has more than one or two arguments — this is the dominant style throughout the codebase:
  ```csharp
  public WorkItem(
      WorkItemId id,
      ProjectId projectId,
      string title,
      WorkItemType type,
      DateTimeOffset createdAt)
  ```
- Braces on their own line (Allman style), consistently.
- Blank line between logical steps inside a handler method is common and encouraged for readability, especially in longer handlers like `SendChatHandler`.

## Comments

Comments are sparse by default — the code is expected to be self-explanatory through naming. The one accepted pattern is a short `// Step description` comment marking discrete steps inside a longer orchestration method (see `SendChatHandler.HandleAsync` for the reference example: `// Load conversation`, `// Persist user message`, `// Load conversation history`, etc.). Don't add comments that restate what the next line obviously does; do add them to mark a logical phase boundary in an otherwise long method.

## Dependency Injection

- Registration happens in a project's `ServiceCollectionExtensions.cs` (or a `Registration/` module for cross-cutting groups), as an extension method on `IServiceCollection` — e.g. `AddApplication()`, `AddInfrastructure(configuration)`.
- Prefer `AddScoped` for anything that touches a repository or per-request state; `AddSingleton` for stateless services and options-bound configuration classes.
- **Before adding a new registration**, check whether it might already exist elsewhere — the codebase currently has some duplicate registrations across `AddApplication()`/`AddInfrastructure()` and dead/unreachable registration code in `ModuleExtensions.cs`. See [DECISIONS.md](./DECISIONS.md); don't add a third place to register the same thing.

## What Not to Do (based on current known issues)

- Don't reuse a namespace across projects "because it's close enough" — see the physical/namespace mismatch issue in [CONVENTIONS.md](./CONVENTIONS.md).
- Don't create a second abstraction for a concept that already has one (see the dual `IAgent` interfaces in [DECISIONS.md](./DECISIONS.md)) — extend or refactor the existing one instead.
- Don't register the same service in two different `ServiceCollectionExtensions`/module files.
- Don't add public setters to domain entities — add a named behavior method instead, even a trivial one.
