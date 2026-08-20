# Azure SQL migration — Claude Code prompts for Stage 2b and Stage 2c

Run after Stage 2a passes. Same rules: PowerShell at `C:\Personal\Nexus.Web`, one prompt at
a time, `/clear` between, commit after each.

---

## Read this before running either one — the cascade trap

Stage 2b and 2c are where `dotnet ef database update` will fail if the delete behaviour is
left at EF's default, and the error message will not be obvious. SQL Server refuses a
foreign key that creates **multiple cascade paths** to the same table (error 1785). Both
stages contain them:

```
WorkItem  → Project, Conversation, Adr, ProjectMilestone     four paths converge
Artifact  → Project, Conversation, Adr, WorkItem             four paths, one via WorkItem
Snapshot  → Branch and Conversation, while Branch → Conversation   a diamond
Adr       → Adr    (supersedes)                              self-reference
```

The fix is not a workaround, it is the correct model: **only the owning parent cascades.**
An `Artifact` belongs to a `WorkItem`; it merely *references* a `Conversation`. Deleting a
conversation must not silently delete artifacts. So every non-owning FK is
`DeleteBehavior.Restrict`, and every self-reference is `DeleteBehavior.NoAction`.

Both prompts below state this explicitly. Do not let it be inferred.

---

## Stage 2b — the context aggregates

These four are everything `ChatContextBundleMapper` reads. If they are wrong, the model
gets the wrong context and the failure is invisible — no exception, just worse answers.

```
Stage 2b of the Azure SQL migration. Port four aggregates to SQL: Knowledge, Adr, WorkItem,
Artifact. Follow exactly the pattern Stages 1b and 2a established.

HARD CONSTRAINT - Domain and Application untouched, except adding the Reference property to
these four aggregates exactly as 1b/2a did. Any other Domain change: STOP and tell me why.

TABLES

  knowledge.Knowledge   Ref prefix KNW-
  knowledge.Adr         Ref prefix ADR-
  work.WorkItem         Ref prefix WRK-
  work.Artifact         Ref prefix ART-

  Two new schemas: knowledge, work.
  Each table: Id uniqueidentifier PK, Seq int IDENTITY shadow property, Ref computed
  PERSISTED unique - identical treatment to org.Workspace.

DELETE BEHAVIOUR - read this before writing any configuration.
SQL Server rejects multiple cascade paths to the same table. These aggregates have them.
Only the OWNING parent cascades; every other FK is a reference, not ownership:

  Knowledge.WorkspaceId          -> Cascade   (owner)
  Knowledge.ProjectId            -> Restrict
  Knowledge.SourceConversationId -> Restrict
  Adr.WorkspaceId                -> Cascade   (owner)
  Adr.ProjectId                  -> Restrict
  Adr.SourceConversationId       -> Restrict
  Adr.SupersedesAdrId            -> NoAction  (self-reference)
  WorkItem.ProjectId             -> Cascade   (owner)
  WorkItem.ConversationId        -> Restrict
  WorkItem.AdrId                 -> Restrict
  Artifact.ProjectId             -> Cascade   (owner)
  Artifact.WorkItemId            -> Restrict
  Artifact.ConversationId        -> Restrict
  Artifact.AdrId                 -> Restrict

Deleting a conversation must never delete an artifact or a decision. That is the reason,
not a workaround for the SQL Server error.

INDEXES - read on every chat turn by ChatContextBundleMapper:
  Knowledge  (WorkspaceId, Status)
  Adr        (WorkspaceId, Status)
  WorkItem   (ProjectId, Status)
  Artifact   (WorkItemId)

SCHEMA CLEANUP
  - No du_ prefix, no T_nnn_ number, no nvarchar(850) key column.
  - DROP WorkItem.workitempriority. It exists in Dataverse with no C# counterpart, so
    nothing reads or writes it - same call as Project.projecttype in 2a. If priority
    becomes a real domain concept it is one migration to add properly.
  - All picklists map from the C# enum as int with a converter in Infrastructure:
    KnowledgeStatus, KnowledgeType, AdrStatus, WorkItemStatus, WorkItemType, ArtifactType.
    The Dataverse export defines no choice values for these; the C# enum is authoritative.
  - Two columns are capped at nvarchar(100) by Dataverse default in a way that does not
    match their purpose: Knowledge.keywords and Artifact.description. Check the Domain
    first - if the Domain enforces no length limit on them, widen them (keywords to
    nvarchar(400), description to nvarchar(max)) and REPORT that you did. If the Domain
    does enforce a limit, match the Domain and say so.

ONE migration for all four: dotnet ef migrations add ContextAggregates, then database update.

ACCEPTANCE - report each one:
  1. dotnet build Nexus.Web.slnx succeeds
  2. dotnet test passes
  3. git diff --stat: only Infrastructure, plus the four aggregate files gaining Reference
  4. dotnet ef database update succeeds - if you hit error 1785 (multiple cascade paths),
     do NOT weaken a FK at random; report which pair collided
  5. paste the four CREATE TABLE statements and every CREATE INDEX
  6. with Nexus:Persistence=Sql, run one chat turn and confirm the response's citations
     reference rows that exist in the SQL tables

If you are running low on context, stop after step 4 and say so.
```

```powershell
git add -A; git commit -m "SQL stage 2b: Knowledge, Adr, WorkItem, Artifact on Azure SQL"
```

---

## Stage 2c — session, branch, snapshot

The last three modelled aggregates. Lowest traffic, and once this passes, Dataverse has
nothing left to serve.

```
Stage 2c of the Azure SQL migration. Port the last three modelled aggregates: Session,
Branch, Snapshot. Same pattern as 1b, 2a and 2b.

HARD CONSTRAINT - Domain and Application untouched, except the Reference property on these
three aggregates.

TABLES

  session.Session    Ref prefix SES-
  session.Branch     Ref prefix BRN-
  session.Snapshot   Ref prefix SNP-

  One new schema: session. Same Id / Seq / Ref treatment as every other table.

DELETE BEHAVIOUR - there is a diamond here:
    Branch   -> Conversation
    Snapshot -> Branch  AND  Snapshot -> Conversation
  Set:
    Session.ConversationId   -> Cascade   (owner)
    Branch.ConversationId    -> Cascade   (owner)
    Snapshot.BranchId        -> Cascade   (owner)
    Snapshot.ConversationId  -> Restrict  (breaks the diamond, and is correct anyway:
                                           the branch owns the snapshot, not the
                                           conversation directly)

INDEXES:
  Session   (ConversationId)
  Branch    (ConversationId)
  Snapshot  (BranchId)

SCHEMA CLEANUP
  - No du_ prefix, no T_nnn_ number, no nvarchar(850) key column.
  - SessionStatus and BranchStatus map from the C# enum as int. No lookup tables.
  - Snapshot.state is nvarchar(max) holding serialised state - leave it as text, do not
    try to model its shape.

ONE migration: dotnet ef migrations add SessionAggregates, then database update.

ACCEPTANCE - report each one:
  1. dotnet build succeeds, dotnet test passes
  2. git diff --stat: Infrastructure only, plus the three aggregates gaining Reference
  3. paste the CREATE TABLE and CREATE INDEX statements
  4. THE IMPORTANT ONE: with Nexus:Persistence=Sql, every repository interface the Domain
     declares now has a SQL implementation registered. List each interface and its SQL
     implementation, and confirm no interface still resolves to a Dataverse type when
     Nexus:Persistence=Sql. If any does, name it - that is what blocks Stage 3.
  5. full smoke test on SQL: POST workspace -> project -> chat -> GET history

If you are running low on context, stop after step 3 and say so.
```

```powershell
git add -A; git commit -m "SQL stage 2c: Session, Branch, Snapshot on Azure SQL"
```

---

## After 2c

Acceptance check 4 is the gate. Once every repository interface resolves to a SQL type,
**Stage 3 deletes Dataverse** — the client, the entities, the mappers, the repositories, the
`Microsoft.PowerPlatform.Dataverse.Client` package reference and the
`System.Security.Cryptography.Xml` pin that only existed to satisfy it. That prompt is
already written in `ADR-014_AZURE_SQL_MIGRATION.md` §2 Stage 3.

Two things are deliberately **not** in 2b, and both belong later:

**Vector retrieval on `knowledge.Knowledge`.** It is driver #2 for this whole migration, but
LocalDB cannot do it — the `vector` type is an Azure SQL feature. Adding an embedding column
now would mean writing a schema that cannot be tested until Stage 4. It goes in Stage 4,
against real Azure SQL, where it can be proven.

**The ten unmodelled tables** (`WorkspaceMember`, `Team`, `TeamMember`, `ProjectMember`,
`ProjectBrief`, `ProjectMilestone`, `MilestoneCriterion`, `ConversationSummary`,
`ConversationLink`, `AccessGrant`). These are not a migration task — there is no C# aggregate
to port. They are domain design, and `ProjectBrief` in particular is the single biggest lever
on answer quality, because today the `Objective` context item carries only the project
*name* at `Authoritative` trust. That deserves its own conversation, not a schema prompt.
