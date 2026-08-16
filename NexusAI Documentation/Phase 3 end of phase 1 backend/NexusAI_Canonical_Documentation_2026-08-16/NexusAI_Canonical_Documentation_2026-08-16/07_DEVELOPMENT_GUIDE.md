# Development Guide

## Prerequisites

- Visual Studio with .NET 10 support or a compatible editor.
- .NET 10 SDK.
- Access to the Development Dataverse environment.
- Development app registration/credentials stored securely.
- Model-provider API key stored in user secrets.
- Git.

## Standard local commands

```powershell
dotnet restore NexusAI.slnx
dotnet build NexusAI.slnx
dotnet run --project src/NexusAI.Api/NexusAI.Api.csproj
```

If `NexusAI.Host` becomes the canonical process, update this documentation and use only that entry point.

## Work one vertical slice at a time

For each feature, complete:

`Domain → Persistence → Application → API → Dependency Injection → Build → Swagger/API test → Dataverse verification → Tests → Documentation → Commit`

Do not create domain files for ten entities and leave all their routes unfinished. Finish and verify one usable feature before starting the next.

## Coding standards

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
- Domain code cannot reference Dataverse, ASP.NET Core, or provider SDKs.

### Application

- Use explicit Command/Query/Result records and handler classes.
- Handlers expose asynchronous methods with `CancellationToken`.
- Validate required input before repository/provider calls.
- Do not leak transport or persistence types.
- Queries do not silently mutate state.

### Infrastructure

- Keep Dataverse logical names centralized.
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
- Repository interfaces: `IXRepository`; Dataverse implementations: `XDataverseRepository`.
- Dataverse schema names and choice codes follow `03_DOMAIN_AND_DATAVERSE.md`.

## Adding a new feature

1. Write the user behavior and acceptance criteria.
2. Confirm whether the concept is a new aggregate or belongs to an existing one.
3. Confirm the live Dataverse schema/names before coding.
4. Add domain ID, status/type enums, aggregate behavior, and repository contract.
5. Add Dataverse entity, mapper, and repository.
6. Add command/query handlers and results.
7. Register dependencies.
8. Add API DTOs/routes and validation.
9. Add unit, mapper, repository, and contract tests.
10. Verify in Swagger and Dataverse.
11. Update only the relevant canonical documents.
12. Commit with a focused message.

## Review checklist

- Does dependency direction remain correct?
- Are IDs strongly typed inside Domain/Application?
- Are all live columns mapped in both directions?
- Are optional/missing Dataverse values safe?
- Are list queries filtered server-side?
- Are required fields validated?
- Are enum numeric values aligned with live choices?
- Are HTTP status and error payloads correct?
- Is cancellation propagated?
- Are secrets absent from source, logs, examples, and commits?
- Do tests cover the new behavior and regression risks?
- Did Swagger and live persistence verification pass?
- Was obsolete documentation removed rather than copied into a new version?

## Git and documentation discipline

- Keep commits focused and buildable.
- Do not commit `bin`, `obj`, local user files, or secrets.
- Do not add `ReadmeV4`, `Roadmap-New`, or nested ZIP archives.
- Update the canonical subject file in place.
- Record significant architectural choices in `08_DECISIONS_AND_TECHNICAL_DEBT.md`.
- Record user-visible milestones in `10_CHANGELOG.md`.

## Working with coding agents

Give the agent a bounded vertical slice and require it to inspect the current source and live schema evidence before editing. A useful instruction includes:

- exact feature and acceptance criteria;
- files/layers it may change;
- architectural rules;
- required build/test/Swagger verification;
- instruction not to alter secrets or unrelated work;
- request for a final changed-file and verification summary.

Agents may assist with code, but schema changes, production deployment, destructive operations, permissions, and machine control require explicit human authorization.
