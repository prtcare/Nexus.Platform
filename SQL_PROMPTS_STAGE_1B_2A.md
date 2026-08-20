# Azure SQL migration — Claude Code prompts for Stage 1b and Stage 2a

Run these in a PowerShell terminal at `C:\Personal\Nexus.Web`, one at a time.
**Commit after each stage** — a dirty tree is what blocked the V2 migration repeatedly.

Before Stage 1b:

```powershell
git status --porcelain
git add -A; git commit -m "checkpoint before SQL stage 1b"
```

---

## Stage 1b — naming decisions applied to Workspace

Paste this whole block into Claude Code:

```
Stage 1b of the Azure SQL migration. Stage 1 created dbo.Workspace before two naming
decisions were made. Apply them now, on this one table, before nineteen more copy it.

DECISION 1 - SQL schemas replace the old T_nnn_ numbering.
  Seven schemas group the tables: org, project, conversation, session, knowledge, work,
  access. Table name = the aggregate's C# class name exactly. No prefix, no number.
  Workspace belongs in the org schema: org.Workspace

DECISION 2 - every aggregate root carries a human-readable reference.
  Id   uniqueidentifier  primary key, strongly typed, never shown to a user
  Seq  int IDENTITY(1,1) allocation only, never read by application code
  Ref  computed PERSISTED unique, the string a person reads and quotes

  For Workspace the expression is:
      ('WKS-' + RIGHT('00000000' + CAST([Seq] AS varchar(8)), 8))

  Ref is computed in the database, not assigned in C#, because only the database can
  guarantee uniqueness under concurrent inserts. Do not generate it in code.

WHAT TO DO

1. DOMAIN CHANGE - this is deliberate and it is the only one.
   Add to the Workspace aggregate:
       public string Reference { get; private set; } = string.Empty;
   Application code must not be able to set it. Do NOT add it to Workspace.Create - a new
   workspace has no reference until the database allocates one.

   Then find how the EXISTING Dataverse repository rehydrates a Workspace from a row
   (a Restore/Rehydrate factory, a private constructor, or reflection - read the code, do
   not guess) and extend that same path to carry Reference. Report which path you found.

2. Look at how the Dataverse table stores its human-readable key. In Dataverse the
   nvarchar(850) primary-name column is the autonumber - it is what shows CON-00000005 and
   PRJ-00000007 in Power Apps. Map Workspace.Reference to that column in the Dataverse
   repository so both persistence implementations return the same concept.

3. SQL side, all inside src/Nexus.Products.Chat.Infrastructure/Sql:
   - WorkspaceConfiguration: builder.ToTable("Workspace", "org")
   - shadow property for Seq:
         builder.Property<int>("Seq").ValueGeneratedOnAdd().UseIdentityColumn();
   - the computed reference:
         var reference = builder.Property(w => w.Reference)
             .HasColumnName("Ref")
             .HasComputedColumnSql("('WKS-' + RIGHT('00000000' + CAST([Seq] AS varchar(8)), 8))", stored: true)
             .ValueGeneratedOnAddOrUpdate();
         reference.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
         reference.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
   - unique constraint:
         builder.HasIndex(w => w.Reference).IsUnique().HasDatabaseName("UQ_Workspace_Ref");

4. The existing migration predates all of this and there is NO DATA anywhere - LocalDB is
   scratch. So do not write a rename migration. Instead:
       dotnet ef database drop --force  (project Nexus.Products.Chat.Infrastructure)
       delete the Sql/Migrations folder contents
       dotnet ef migrations add InitialSqlSchema
       dotnet ef database update
   One clean migration is worth more than a tidy history of a schema nobody has used.

5. Confirm the generated SQL contains: CREATE SCHEMA [org], the org.Workspace table, the
   Seq identity column, the Ref computed column with PERSISTED, and the unique index.

6. Expose Reference in the API response for workspaces (the DTO/contract layer of
   Nexus.Web), so it is visible in the browser.

ACCEPTANCE - report each one:
  1. dotnet build Nexus.Web.slnx succeeds
  2. dotnet test passes
  3. git diff --stat: the ONLY Domain file touched is the Workspace aggregate (plus its
     rehydration path). Any other Domain or Application file is a FAILURE - report it,
     do not fix it.
  4. paste the CREATE TABLE statement the migration generated
  5. with Nexus:Persistence=Sql, POST /api/v1/workspaces returns a Reference of
     WKS-00000001, and a second POST returns WKS-00000002
  6. with the setting absent, Dataverse still works and returns its own reference

If you are running low on context, stop after step 4 and say so.
```

Then commit:

```powershell
git add -A; git commit -m "SQL stage 1b: org schema, Ref computed column, Workspace.Reference"
```

---

## Stage 2a — the chat hot path

Three aggregates: Project, Conversation, ConversationMessage. These are read on every
single chat turn, so they get the indexes.

```
Stage 2a of the Azure SQL migration. Port three aggregates to SQL: Project, Conversation,
ConversationMessage. Follow exactly the pattern Stage 1/1b established for Workspace.

HARD CONSTRAINT - Domain and Application are untouched this time. Stage 1b's Reference
property was a one-off, already done. If you believe a Domain change is needed here, STOP
and tell me why instead of making it.

TABLES

  project.Project                    Ref prefix PRJ-
  conversation.Conversation          Ref prefix CON-
  conversation.ConversationMessage   Ref prefix MSG-

  Each gets the same three-column identity treatment as org.Workspace:
    Id uniqueidentifier PK, Seq int IDENTITY shadow property, Ref computed PERSISTED unique.
  PRJ- and CON- are the prefixes already live in Dataverse - keep them identical so
  references a person has already seen still read the same way.

  Add Reference to these three aggregates the same way Stage 1b did for Workspace, via the
  same rehydration path, and map it to the Dataverse autonumber column too. (This is the
  one Domain touch permitted in 2a - the same permitted change, three more classes.)

INDEXES - these are the reads on every chat turn, they are not optional:
  ConversationMessage  (ConversationId, CreatedOn)   history load, the hottest read
  Conversation         (ProjectId)
  Project              (WorkspaceId)

SCHEMA CLEANUP - carry none of the Dataverse anomalies across:
  - DROP projectstatus01 and projecttype01. The unsuffixed projectstatus is real; the 01
    columns are artifacts of a column being created, renamed and re-added.
  - DROP conversationtype01, keep conversationtype.
  - DROP projecttype entirely. It exists in Dataverse with no C# counterpart, so nothing
    reads or writes it. Do not create a column that no code touches. If it becomes a real
    domain concept later it is one migration to add.
  - Every picklist maps from the C# enum, stored as int with a value converter in
    Infrastructure. Do not create lookup tables. Do not trust the Dataverse choice values -
    several picklists have none defined while the C# enum works today.
  - No du_ prefix, no T_nnn_ number, no nvarchar(850) key columns anywhere.

WORK
  - one IEntityTypeConfiguration per aggregate, under Sql/Configurations
  - one repository per aggregate, under Sql/Repositories, implementing the interface the
    Domain already declares
  - register all three in ServiceCollectionExtensions behind Nexus:Persistence=Sql
  - value converters for the strongly typed IDs live in Sql/Conventions alongside the
    existing ones
  - ONE migration for all three: dotnet ef migrations add ChatHotPath
  - dotnet ef database update

ACCEPTANCE - report each one:
  1. dotnet build Nexus.Web.slnx succeeds
  2. dotnet test passes
  3. git diff --stat shows changes ONLY under src/Nexus.Products.Chat.Infrastructure and
     the four aggregate files receiving Reference. Anything else in Domain or Application
     is a FAILURE - report it, do not fix it.
  4. paste the three CREATE TABLE statements and the CREATE INDEX statements
  5. with Nexus:Persistence=Sql: POST /api/v1/workspaces -> POST /api/v1/projects ->
     POST /api/v1/chat all succeed, and GET the conversation returns the message history
  6. with the setting absent, Dataverse still works

If you are running low on context, stop after the migration is generated (step 4) and say
so - do not start the smoke test on an empty tank.
```

Then commit:

```powershell
git add -A; git commit -m "SQL stage 2a: Project, Conversation, ConversationMessage on Azure SQL"
```

---

## What follows

| Stage | Aggregates | Schema |
|---|---|---|
| 2b | Knowledge, Adr, WorkItem, Artifact | `knowledge`, `work` |
| 2c | Session, Branch, Snapshot | `session` |
| 2d | WorkspaceMember, Team, TeamMember, ProjectMember, ProjectBrief, ProjectMilestone, MilestoneCriterion, ConversationSummary, ConversationLink, AccessGrant | the ten not yet modelled in C# |
| 3 | delete Dataverse entirely | — |
| 4 | Azure hardening — real Azure SQL, managed identity, firewall | — |

One decision is still open and belongs in 2b: `WorkItem.workitempriority` exists in
Dataverse with no C# counterpart. Same situation as `Project.projecttype`, and my
recommendation is the same — drop it rather than carry a column nothing reads. Say so if
you would rather model priority as a real domain concept, and I will write it into 2b.
