# CONVENTIONS.md

System-level conventions: IDs, numbering, naming, UI standards, and business rules. For pure C#/.NET code style (brace placement, naming casing, etc.), see [CODING-STANDARDS.md](./CODING-STANDARDS.md).

## ID Conventions

- Every entity ID is a distinct C# type — a `readonly record struct` wrapping a single `Guid` — never a bare `Guid` passed around. This gives compile-time protection against passing a `ProjectId` where a `WorkspaceId` is expected.
- **Standard shape** (apply to all new ID types):
  ```csharp
  public readonly record struct {Entity}Id(Guid Value)
  {
      public static {Entity}Id New() => new(Guid.NewGuid());
      public override string ToString() => Value.ToString();
  }
  ```
- **Current inconsistency**: not every existing ID type includes the `ToString()` override (`WorkspaceId` and `WorkItemId` have it; `ProjectId` and `ConversationId` don't). Add it when touching these types — it matters for logging and for any place an ID gets string-interpolated.
- IDs are always generated client-side via `{Entity}Id.New()` at creation time, never assigned by the storage layer. This keeps entity creation testable without a database round-trip.

## Enum / Status Numbering

**Going forward, all new status/type enums start at `1`, reserving `0` as an implicit "unspecified" default.** This matters because C#'s default value for an uninitialized `int`-backed enum is `0` — if `0` is a valid, meaningful status (like "Active"), an uninitialized or deserialization-failure case can silently look like a valid, active record instead of an obviously-wrong one.

**Current state is inconsistent** — some enums already start at `0` (`ProjectStatus`, `ConversationStatus`, `AdrStatus`, `SnapshotStatus`, `SessionStatus`), others correctly start at `1` (`WorkspaceStatus`, `WorkItemStatus`, `WorkItemType`, `KnowledgeSource`, `ConversationMessageRole`, `ArtifactType`, `BranchStatus`). This is tracked as a known inconsistency in [DECISIONS.md](./DECISIONS.md) rather than silently fixed, because changing existing enum values is a breaking change once real data exists — do not renumber existing enums without a deliberate migration.

## Dataverse Naming

- Publisher prefix: **`nexus_`** for every table and custom column.
- Table logical names: `nexus_{entitynamesingular}` — e.g. `nexus_workspace`, `nexus_workitem`, `nexus_conversationmessage`. All lowercase, no separators between compound words.
- Primary key columns follow Dataverse's automatic pattern: `nexus_{tablename}id`.
- Lookup columns are named `nexus_{parenttablename}id` (e.g. `nexus_workspaceid` on `nexus_project`).
- Status/type fields use Dataverse **Choice (Option Set)** columns, not plain integers, so the option labels are visible and editable in the Power Platform UI — even though current in-memory entity classes store them as raw `int` pending the Milestone 1 rework.
- Full schema: see [DATABASE.md](./DATABASE.md).

## Namespace / Physical Location Alignment

**Rule: a file's declared `namespace` must match the project it physically lives in.** This sounds obvious, but the current codebase violates it in at least three places — `AgentRuntime.cs` (physically in `NexusAI.Core`, namespaced `NexusAI.Infrastructure.Agents`), `Session.cs` and its related types (physically in `NexusAI.Application`, namespaced `NexusAI.Domain.Session`), and `IRepository<TDomain,TId>` (physically in `NexusAI.Domain`, namespaced `NexusAI.Infrastructure.Dataverse.Common`). These are tracked as cleanup items in [DECISIONS.md](./DECISIONS.md) — new code should not repeat the pattern. If a type conceptually belongs to a different layer than the project you're adding it to, that's a signal to move the file, not just rename the namespace.

## Folder / File Organization (Application Layer)

- One folder per aggregate root (e.g. `Projects/`, `WorkItem/`, `Knowledge/`) — note that pluralization is itself inconsistent today (`Projects/` vs `WorkItem/` vs `Workspaces/`); prefer the **plural** form for new folders to match the majority.
- Commands live under `{Entity}/Commands/{Verb}{Entity}/`, each with three files: `{Verb}{Entity}Command.cs`, `{Verb}{Entity}Handler.cs`, `{Verb}{Entity}Result.cs`.
- Queries follow the same pattern under `{Entity}/Queries/{Verb}{Entity}/`.
- A handler is a plain class (not resolved through a mediator library) with a single public `HandleAsync(command, cancellationToken)` method, injected directly into whatever calls it (an Api endpoint, another handler, or `Host`).

## Business Rules

**Immutability by design**: `ConversationMessage` and `Knowledge` have no update methods — once created, their content never changes. This is intentional, not an oversight: chat messages are an audit trail, and knowledge entries are meant to be superseded by new entries (or an `Adr`) rather than silently edited. Do not add mutation methods to these two types without a deliberate design discussion.

**Milestone approval rule** *(planned, Phase 2)*: a project's `ProjectMilestone` content changes **only** on explicit user approval — never automatically, never inferred from conversation content, regardless of how confident the system is. This is the one place in the whole memory model where "the AI decided this is worth remembering" is not sufficient; a human has to confirm it. See [VISION.md](./VISION.md) for why this matters.

**Status lifecycles (intended)**: the enum values below imply a natural progression, but as of Phase 1, **most entities' `ChangeStatus`-style methods accept any value with no transition validation** — e.g. `WorkItem.ChangeStatus(status)` will happily accept `Cancelled → New`. The intended lifecycles are:

| Entity | Intended flow |
|---|---|
| WorkItem | New → Active → (Blocked ↔ Active) → Completed \| Cancelled |
| Adr | Proposed → Accepted → (Deprecated \| Superseded) |
| Branch | Active → Merged \| Archived |
| Snapshot | Draft → Finalized |
| Session | Active → Completed \| Cancelled |
| Workspace / Project / Conversation | Active → Archived (no un-archive today) |

Enforcing these transitions (rejecting invalid ones) is not yet implemented anywhere. Treat this table as the target behavior, not current behavior, and check [DECISIONS.md](./DECISIONS.md) before assuming a transition is validated.

## UI Standards *(forward-looking — no client UI exists yet)*

These apply once the Power Apps and Visual Studio desktop clients are built (Phase 2, Milestone 3+). They're recorded now so both clients stay consistent with each other from the start rather than diverging:

- The **Workspace → Project → Conversation** selector is always visible/accessible, never buried — the hierarchy is core to the product, not an afterthought.
- A conversation's **milestone window** (once implemented) is always visible alongside the chat, not hidden behind a menu — it's meant to be a constant, trusted reference, and hiding it would undermine the point of it being approval-gated.
- **Branch conversations** are visually distinguished from the main conversation thread (e.g. a different background tint or an indentation/breadcrumb showing "branched from main") so a user always knows which thread they're in.
- Only the **branch's conclusion** — never the full branch transcript — appears back in the main conversation view by default; the full branch remains accessible on demand.
- Destructive or hard-to-reverse actions (archiving a workspace/project, merging a branch) require explicit confirmation in both clients — not just the desktop app.

This section will grow as the front ends are actually built; treat it as a starting contract, not a finished spec.
