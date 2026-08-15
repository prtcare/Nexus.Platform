# DATABASE.md

## Current State

Today, persistence is an **in-memory dictionary** (`InMemoryDataverseContext`) shaped to look like a real Dataverse context — same repository/mapper pattern, same `IDataverseContext` interface — so that swapping in a real Dataverse SDK client is a drop-in replacement rather than a rewrite. Nothing persists across process restarts yet. Real Dataverse connectivity is **Phase 2, Milestone 1** (see [ROADMAP.md](./ROADMAP.md)).

This document covers both: the current entity model (implemented, in `NexusAI.Domain`) and the target Dataverse schema (designed here, not yet created in any environment).

## Entity-Relationship Map

```
Workspace (root)
  ├── Project (WorkspaceId)
  │     ├── Conversation (ProjectId)
  │     │     ├── ConversationMessage (ConversationId)
  │     │     ├── Session (ConversationId)
  │     │     └── Branch (ConversationId)
  │     │           └── Snapshot (BranchId)
  │     └── WorkItem (ProjectId)
  │           └── Artifact (WorkItemId)
  └── Knowledge (WorkspaceId)
        └── Adr (KnowledgeId)
```

Every relationship above is a simple one-to-many lookup, confirmed directly from the domain model's constructors — there are no many-to-many relationships in the current design.

## ID and Status Conventions

- Every entity ID is a `readonly record struct` wrapping a single `Guid` (e.g. `public readonly record struct WorkspaceId(Guid Value)`), with a static `New()` factory calling `Guid.NewGuid()`. This gives compile-time type safety (you can't accidentally pass a `ProjectId` where a `WorkspaceId` is expected) at zero runtime cost.
- Status fields are C# enums in the Domain layer, but stored as raw `int` in the current Dataverse-shaped entity classes (e.g. `WorkspaceEntity.Status` is `int`, not `WorkspaceStatus`). See [CONVENTIONS.md](./CONVENTIONS.md) for the full enum-to-storage mapping table.
- `CreatedAt`/`CreatedOn` is `DateTimeOffset`, set once at construction, never updated.

## Planned Dataverse Schema

All tables use publisher prefix **`nexus_`**. Primary keys follow Dataverse's standard auto-generated pattern: table `nexus_workspace` gets primary key column `nexus_workspaceid`. Status enums map to Dataverse **Choice (Option Set)** columns rather than raw integers — this is a deliberate upgrade over the current in-memory entity shape, planned for Milestone 1.

### nexus_workspace
| Column | Type | Notes |
|---|---|---|
| `nexus_workspaceid` | Unique Identifier (PK) | Auto-generated |
| `nexus_name` | Text (100) | Required |
| `nexus_status` | Choice | Active = 1, Archived = 2 |
| `createdon` | DateTime | Dataverse system column (replaces custom `CreatedAt`) |

### nexus_project
| Column | Type | Notes |
|---|---|---|
| `nexus_projectid` | Unique Identifier (PK) | |
| `nexus_workspaceid` | Lookup → nexus_workspace | Required |
| `nexus_name` | Text (200) | Required |
| `nexus_status` | Choice | Active = 0, Archived = 1 |
| `createdon` | DateTime | |

### nexus_conversation
| Column | Type | Notes |
|---|---|---|
| `nexus_conversationid` | Unique Identifier (PK) | |
| `nexus_projectid` | Lookup → nexus_project | Required |
| `nexus_title` | Text (200) | Required |
| `nexus_status` | Choice | Active = 0, Archived = 1 |
| `createdon` | DateTime | |

### nexus_conversationmessage
| Column | Type | Notes |
|---|---|---|
| `nexus_conversationmessageid` | Unique Identifier (PK) | |
| `nexus_conversationid` | Lookup → nexus_conversation | Required |
| `nexus_role` | Choice | User = 1, Assistant = 2, System = 3 |
| `nexus_content` | Text (multi-line, max) | Message body |
| `createdon` | DateTime | Maps from domain's `CreatedOn` |

### nexus_workitem
| Column | Type | Notes |
|---|---|---|
| `nexus_workitemid` | Unique Identifier (PK) | |
| `nexus_projectid` | Lookup → nexus_project | Required |
| `nexus_title` | Text (200) | Required, mutable |
| `nexus_description` | Text (multi-line) | Optional, mutable |
| `nexus_type` | Choice | Task=1, Bug=2, Feature=3, Epic=4, Story=5, Research=6, Idea=7, Spike=8 |
| `nexus_status` | Choice | New=1, Active=2, Blocked=3, Completed=4, Cancelled=5 |
| `createdon` | DateTime | |

### nexus_knowledge
| Column | Type | Notes |
|---|---|---|
| `nexus_knowledgeid` | Unique Identifier (PK) | |
| `nexus_workspaceid` | Lookup → nexus_workspace | Required |
| `nexus_title` | Text (200) | |
| `nexus_content` | Text (multi-line, max) | |
| `nexus_source` | Choice | Document=1, Web=2, Code=3, User=4 |
| `createdon` | DateTime | Immutable after creation |

### nexus_adr
| Column | Type | Notes |
|---|---|---|
| `nexus_adrid` | Unique Identifier (PK) | |
| `nexus_knowledgeid` | Lookup → nexus_knowledge | Required |
| `nexus_title` | Text (200) | Mutable |
| `nexus_decision` | Text (multi-line) | Mutable |
| `nexus_status` | Choice | Proposed=0, Accepted=1, Superseded=2, Deprecated=3 |
| `createdon` | DateTime | |

### nexus_artifact
| Column | Type | Notes |
|---|---|---|
| `nexus_artifactid` | Unique Identifier (PK) | |
| `nexus_workitemid` | Lookup → nexus_workitem | Required |
| `nexus_name` | Text (200) | Mutable |
| `nexus_type` | Choice | SourceCode=1, Markdown=2, Json=3, Yaml=4, Sql=5, Image=6, Document=7, Other=99 |
| `nexus_content` | Text (multi-line, max) | Mutable |
| `createdon` | DateTime | |

### nexus_branch
| Column | Type | Notes |
|---|---|---|
| `nexus_branchid` | Unique Identifier (PK) | |
| `nexus_conversationid` | Lookup → nexus_conversation | Required |
| `nexus_name` | Text (200) | Mutable |
| `nexus_status` | Choice | Active=1, Merged=2, Archived=3 |
| `createdon` | DateTime | |

### nexus_snapshot
| Column | Type | Notes |
|---|---|---|
| `nexus_snapshotid` | Unique Identifier (PK) | |
| `nexus_branchid` | Lookup → nexus_branch | Required |
| `nexus_description` | Text (multi-line) | Mutable |
| `nexus_status` | Choice | Draft=0, Finalized=1 |
| `createdon` | DateTime | |

### nexus_session
| Column | Type | Notes |
|---|---|---|
| `nexus_sessionid` | Unique Identifier (PK) | |
| `nexus_conversationid` | Lookup → nexus_conversation | Required |
| `nexus_status` | Choice | Active=0, Completed=1, Cancelled=2 |
| `nexus_startedat` | DateTime | Maps from domain's `StartedAt` |

### nexus_projectmilestone *(planned — not yet in the Domain model)*
Reserved for Phase 2, Milestone 2 (the approval-gated project memory window described in [VISION.md](./VISION.md) and [ARCHITECTURE.md](./ARCHITECTURE.md)). Included here now so the Milestone 1 schema deployment doesn't need a second pass.

| Column | Type | Notes |
|---|---|---|
| `nexus_projectmilestoneid` | Unique Identifier (PK) | |
| `nexus_projectid` | Lookup → nexus_project | Required |
| `nexus_summary` | Text (multi-line, max) | The approved, curated memory content |
| `nexus_approvedon` | DateTime | Null until explicitly approved |
| `nexus_approvedby` | Text (200) | Placeholder until real auth/user model exists |
| `createdon` | DateTime | |

## Relationship Summary

| Child Table | Lookup Column | Parent Table |
|---|---|---|
| nexus_project | nexus_workspaceid | nexus_workspace |
| nexus_conversation | nexus_projectid | nexus_project |
| nexus_conversationmessage | nexus_conversationid | nexus_conversation |
| nexus_session | nexus_conversationid | nexus_conversation |
| nexus_branch | nexus_conversationid | nexus_conversation |
| nexus_snapshot | nexus_branchid | nexus_branch |
| nexus_workitem | nexus_projectid | nexus_project |
| nexus_artifact | nexus_workitemid | nexus_workitem |
| nexus_knowledge | nexus_workspaceid | nexus_workspace |
| nexus_adr | nexus_knowledgeid | nexus_knowledge |
| nexus_projectmilestone *(planned)* | nexus_projectid | nexus_project |

## Deployment Approach

Tables will be created via a small idempotent console tool (planned for `tools/`, see [ROADMAP.md](./ROADMAP.md) Milestone 1) using the Dataverse metadata API (`CreateEntityRequest`, `CreateAttributeRequest`, `CreateOneToManyRequest`), rather than manual creation through the Power Platform admin center — this keeps the schema versioned in source control and repeatable across dev/prod environments. This same tool pattern is designed to be reused by the Compiler agent (Phase 2, Milestone 5), which needs to create arbitrary business-domain Dataverse tables from an Excel specification using the same underlying mechanism.

## Known Gaps Blocking Real Dataverse Persistence

See [DECISIONS.md](./DECISIONS.md) for full detail, but in summary: `IDataverseContext.RetrieveMultipleAsync` currently accepts a C# `Func<TEntity,bool>` predicate, which only works against an in-memory collection — a real Dataverse client needs a query built from column names and values *before* any request is sent. Every actual current usage is a simple single-column equality filter, so the fix is small: replace the lambda with an `(attributeName, value)` filter. This is first on the list for Milestone 1.
