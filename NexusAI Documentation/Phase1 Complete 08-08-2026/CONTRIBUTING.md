# CONTRIBUTING.md

NexusAI is currently a single-developer project. This document is written for that reality — it's a personal workflow discipline, not open-source-project boilerplate — but structured so it still makes sense if collaborators (human or AI coding agents) join later.

## Environment Setup

1. Visual Studio 2022+ (or any .NET 10-compatible IDE) and the .NET 10 SDK.
2. Open `NexusAI.slnx`.
3. Set secrets via User Secrets, never in `appsettings.json`:
   ```
   dotnet user-secrets set "OpenAI:ApiKey" "..." --project src/NexusAI.Host
   dotnet user-secrets set "OpenAI:ApiKey" "..." --project src/NexusAI.Api
   ```
   Once Dataverse connectivity lands (Phase 2, Milestone 1), the same applies to `Dataverse:ClientSecret` — see the historical incident in [DECISIONS.md](./DECISIONS.md) Known Issue #19 for exactly why this matters.
4. Run `NexusAI.Host` to confirm everything still works end-to-end before starting new work — it's the fastest smoke test available today, since `tests/` isn't populated yet.

## Before Starting Work

- Check [ROADMAP.md](./ROADMAP.md) — work should map to a specific milestone, not be ad hoc, so the platform builds toward Phase 2 in the sequence it was deliberately planned (Milestone 0's foundation rework before agents get built on top of it, for example).
- Check [DECISIONS.md](./DECISIONS.md)'s Known Issues log before assuming how something currently behaves — several things (which agent actually runs, whether history is reused, how IDs serialize) don't work the way a quick read of the code might suggest.

## Adding a New Entity (the established pattern)

The codebase follows one consistent recipe per aggregate — follow it rather than inventing a new shape:

1. **Domain**: create `{Entity}/{Entity}.cs`, `{Entity}Id.cs`, `{Entity}Status.cs` (if applicable), `I{Entity}Repository.cs`. Entity is a `sealed class` (extend `Entity<TId>` or `AggregateRoot<TId>`), constructor enforces required state, mutations go through named methods — never public setters.
2. **Application**: `{Entity}/Commands/Create{Entity}/` with `Create{Entity}Command.cs`, `Create{Entity}Handler.cs`, `Create{Entity}Result.cs`. Add `Queries/` similarly for reads.
3. **Infrastructure**: `Dataverse/Entities/{Entity}Entity.cs` (extends `DataverseEntity`), `Dataverse/Mapping/{Entity}Mapper.cs`, `Dataverse/Repositories/{Entity}DataverseRepository.cs`.
4. **DI**: register the mapper, repository, and handlers in `NexusAI.Infrastructure/ServiceCollectionExtensions.cs` — check first that you're not duplicating a registration that already exists in `NexusAI.Application/DependencyInjection/ServiceCollectionExtensions.cs` (see [DECISIONS.md](./DECISIONS.md) Known Issue #6 for why this matters).
5. **Api** (if the entity needs external exposure): `Endpoints/{Entity}/{Entity}Endpoint.cs` following the existing minimal-API pattern, plus Request/Response DTOs — using plain `Guid` in the DTOs, not the wrapped ID type (see [DECISIONS.md](./DECISIONS.md) Known Issue #15 for why that's a real trap).
6. **Docs**: add the new table to [DATABASE.md](./DATABASE.md), the new endpoints to [API.md](./API.md), and update [MODULES.md](./MODULES.md)'s table for whichever project gained the new folder.

## Code Review Checklist (self-review before considering work "done")

- [ ] Follows [CODING-STANDARDS.md](./CODING-STANDARDS.md) (file-scoped namespaces, sealed classes, no public setters on entities, `Async` suffix + trailing `CancellationToken`).
- [ ] Namespace matches the physical project — see [CONVENTIONS.md](./CONVENTIONS.md); don't add a 4th instance of Known Issue #3.
- [ ] No duplicate DI registration — check both `AddApplication()` and `AddInfrastructure()` before adding a new one.
- [ ] New enums start at `1`, not `0` (see [CONVENTIONS.md](./CONVENTIONS.md)).
- [ ] API DTOs use plain `Guid`, not wrapped value-object ID types.
- [ ] `NexusAI.Host`'s smoke test still runs end-to-end.
- [ ] If a known issue from [DECISIONS.md](./DECISIONS.md) was fixed, move it to a "Resolved" note there with the date rather than just deleting it — the history is useful.
- [ ] If a new inconsistency was introduced or discovered, add it to [DECISIONS.md](./DECISIONS.md) rather than leaving it undocumented.

## Testing

There is no test suite yet (`tests/` exists as a placeholder in the `.slnx` but is empty). Until one exists, `NexusAI.Host`'s smoke test is the closest thing to a regression check — run it before and after a change. Standing up an xUnit test project is not currently scheduled on the roadmap but should be treated as overdue; consider it fair game to pick up ahead of a Phase 2 milestone if it would meaningfully de-risk that milestone's work.

## Commit Style

Small, scoped commits mapped to a single roadmap item or known issue where possible (e.g. `Fix: RetrieveMultipleAsync predicate → attribute filter (Milestone 1)`). This project doesn't currently enforce a strict commit message format, but referencing the milestone or known-issue number makes the history genuinely useful for reconstructing "why" later — which matters more on a solo project than a formal message template would.

## Working with AI Coding Agents

If you (or a future collaborator) use an AI coding agent on this repository: **have it read [AI_CONTEXT.md](./AI_CONTEXT.md) first**, before it touches any code. That document exists specifically to prevent an agent from confidently assuming things about the architecture that the Known Issues log already contradicts — like assuming `DeveloperAgent` is what runs when a plan executes. After the agent makes changes, ask it to update [DECISIONS.md](./DECISIONS.md) and [CHANGELOG.md](./CHANGELOG.md) as part of the same piece of work, not as a separate afterthought — documentation drift is worse on a project that leans on AI agents for continuity between sessions than on one with a single consistent human maintainer.
