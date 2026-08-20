# Development Guide

## Prerequisites

- Visual Studio with .NET 10 support or a compatible editor.
- .NET 10 SDK.
- SQL Server LocalDB. Azure SQL arrives at migration Stage 4.
- Development app registration/credentials stored securely.
- Model-provider API key set via `set-openai-key.ps1` — never `dotnet user-secrets set`; it parks the value in shell history.
- Git.

## Standard local commands

Three solutions, each with its own purpose:

```powershell
dotnet build Nexus.AI.slnx     # libraries - packed via .\pack-local.ps1, never run
dotnet build Nexus.Int.slnx    # Intelligence - runs at /intelligence/v1
dotnet build Nexus.Web.slnx    # products + API - runs at /api/v1, plus the React client
```

## Work one vertical slice at a time

For each feature, complete:

`Domain → Persistence → Application → API → Dependency Injection → Build → Swagger/API test → persistence verification → Tests → Documentation → Commit`

Do not create domain files for ten entities and leave all their routes unfinished. Finish and verify one usable feature before starting the next.

## Coding standards

### Architecture boundaries

- Three solutions, one direction of dependency: Intelligence decides, Platform executes, products own the data and the experience. `Nexus.Web` may depend on `Nexus.Intelligence.Contracts`; nothing may depend on Intelligence internals or on another product.
- These boundaries are enforced by NetArchTest architecture tests in each solution, not by convention alone. A boundary test nobody has seen fail is a boundary test nobody has verified — when you add a boundary rule, break it deliberately once to confirm the test catches it.
- No shared kernel. `Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never reference a product type; a product never references an Intelligence-internal type. Two layers that seem to need the same shape need a mapper, not a shared class.
- Provider credentials belong to `Nexus.Intelligence.Api` only, under `Platform:Providers:<Provider>:ApiKey`. A product holding a provider key is an architectural violation, not a configuration choice.

### General

- Nullable reference types and implicit usings follow project settings.
- One public type per file unless small private/nested types improve locality.
- File name matches the primary type.
- Use file-scoped namespaces consistently.
- Use clear business names; avoid unexplained abbreviations.
- Keep methods small enough to express one responsibility.

### Domain

- Use sealed classes for entities/aggregates unless inheritance is intentional.
- Use strongly typed immutable IDs.
- Enforce invariants in constructors/factory methods and behavior methods.
- Avoid public setters that bypass business rules.
- Domain code cannot reference EF Core, ASP.NET Core, or provider SDKs.
- The C# enum is authoritative for every status/type value. Never a lookup table.

### Application

- Use explicit Command/Query/Result records and handler classes.
- Handlers expose asynchronous methods with `CancellationToken`.
- Validate required input before repository/provider calls.
- Do not leak transport or persistence types.
- Queries do not silently mutate state.

### Infrastructure

- One `IEntityTypeConfiguration` per aggregate, under `Sql/Configurations`.
- Mappers support both directions and handle optional columns.
- Use server-side filters and select only required columns.
- Treat transient external failures separately from validation/not-found errors.
- Never log credentials or full sensitive prompts/documents.

### API

- Keep endpoints thin.
- Use request/response DTOs; do not expose domain entities directly.
- Return correct HTTP codes and common problem details.
- Add OpenAPI summaries, tags, operation IDs, and documented responses.
- Validate GUID relationships and required strings.

### Async and cancellation

- Use `Async` suffix where consistent with the project convention.
- Pass cancellation tokens through every external/repository call.
- Avoid `.Result`, `.Wait()`, and sync-over-async.

## Naming conventions

- Types and public members: PascalCase.
- Parameters and locals: camelCase.
- Interfaces: `I` prefix.
- Commands: `CreateXCommand`; queries: `GetXQuery`; handlers: corresponding `Handler`; results: corresponding `Result`.
- Repository interfaces: `IXRepository`; SQL implementations: `XSqlRepository` under `Sql/Repositories`. The Dataverse variants are deleted at migration Stage 3.
- SQL schemas, not prefixes: schema name is the cluster, table name is the C# class name verbatim — no `du_`/`T_nnn_`-style prefix or numbering.
- Primary keys follow the `Id` / `Seq` / `Ref` pattern: `Id` is the GUID key, `Seq` is an `IDENTITY` allocation, `Ref` is a computed `PERSISTED` column derived from `Seq`. `Ref` is computed by the database, never in C#, because only the database guarantees uniqueness under concurrent inserts.

## Adding a new feature

1. Write the user behavior and acceptance criteria.
2. Confirm whether the concept is a new aggregate or belongs to an existing one.
3. Work EF code-first: Domain class, then configuration, then migration, then DDL. Nobody hand-writes DDL a migration doesn't know about.
4. Add domain ID, status/type enums, aggregate behavior, and repository contract.
5. Add the EF entity configuration, mapper, and SQL repository.
6. Add command/query handlers and results.
7. Register dependencies.
8. Add API DTOs/routes and validation.
9. Add unit, mapper, repository, and contract tests.
10. Verify in Swagger and against the database.
11. Update only the relevant canonical documents.
12. Commit with a focused message.

## Review checklist

- Does dependency direction remain correct?
- Are IDs strongly typed inside Domain/Application?
- Are all live columns mapped in both directions?
- Are optional/missing column values handled safely?
- Are list queries filtered server-side?
- Are required fields validated?
- Do the C# enum and the EF converter agree?
- Are HTTP status and error payloads correct?
- Is cancellation propagated?
- Are secrets absent from source, logs, examples, and commits?
- Do tests cover the new behavior and regression risks?
- Did Swagger and persistence verification pass?
- Was obsolete documentation removed rather than copied into a new version?

## Git and documentation discipline

- Keep commits focused and buildable.
- Do not commit `bin`, `obj`, local user files, or secrets.
- Do not add `ReadmeV4`, `Roadmap-New`, or nested ZIP archives.
- Update the canonical subject file in place.
- Record significant architectural choices in `08_DECISIONS_AND_TECHNICAL_DEBT.md`.
- Record user-visible milestones in `10_CHANGELOG.md`.
- Route PowerShell calls to native executables through an `Invoke-Native`-shaped helper. `$ErrorActionPreference='Stop'` plus `2>&1` turns a success message written to stderr into a fatal error.

## Working with coding agents

Give the agent a bounded vertical slice and require it to inspect the current source before editing. A useful instruction includes:

- exact feature and acceptance criteria;
- files/layers it may change;
- architectural rules;
- required build/test/Swagger verification;
- instruction not to alter secrets or unrelated work;
- request for a final changed-file and verification summary.

One concern per prompt, ending in build + test + commit, with `/clear` between prompts. Never paste staged prompts in sequence — each stage assumes the previous one's build is real, and pasting several together means only the first one runs. For any prompt large enough that a mid-flight stall would be expensive to diagnose, add: *"if you are running low on context, stop at `<boundary>` and say so."*

Agents may assist with code, but schema changes, production deployment, destructive operations, permissions, and machine control require explicit human authorization.
