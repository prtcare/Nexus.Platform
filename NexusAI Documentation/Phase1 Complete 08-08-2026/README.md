# NexusAI

NexusAI is a self-owned AI orchestration platform: a provider-agnostic reasoning layer, a structured and permanent memory model, and a registry of specialized agents that do real work — writing code, generating Dataverse schemas, and eventually helping automate physical machinery. It's built on .NET and backed by Microsoft Dataverse.

See [VISION.md](./VISION.md) for the full "why."

## Status

**Phase 1 (Foundation) is complete.** The solution skeleton, domain model, application layer, and a working OpenAI-backed chat loop with in-memory persistence are all in place and runnable. **Phase 2 (Real Platform)** is next — see [ROADMAP.md](./ROADMAP.md) for the milestone plan, and [AI_CONTEXT.md](./AI_CONTEXT.md) for the precise current state, including known gaps that Phase 2 addresses.

This is an active, single-developer project. Expect rough edges — they're tracked, not hidden. See the "Known Issues" section of [DECISIONS.md](./DECISIONS.md) for a running list.

## Solution Structure

```
NexusAI/
├── src/
│   ├── NexusAI.Domain/          Entities, value objects, repository interfaces
│   ├── NexusAI.Application/     Commands, handlers, queries, orchestration services
│   ├── NexusAI.Core/            Agent framework abstractions (IAgent, IAgentRuntime, IAgentRegistry)
│   ├── NexusAI.Agents/          Concrete agent implementations
│   ├── NexusAI.Infrastructure/  Dataverse persistence, OpenAI provider, DI wiring
│   ├── NexusAI.Api/             ASP.NET Core Web API (Swagger-enabled)
│   ├── NexusAI.Host/            Console runner used as an end-to-end smoke test
│   └── NexusAI.Foundation/      Reserved for shared low-level utilities (currently empty)
├── tests/                       Reserved for the test suite (not yet populated)
└── tools/                       Reserved for standalone tooling, e.g. schema deployers (not yet populated)
```

See [ARCHITECTURE.md](./ARCHITECTURE.md) for how these fit together, and [MODULES.md](./MODULES.md) for what each one is responsible for in detail.

## Requirements

- Visual Studio 2022 or later (or any .NET 10 compatible IDE)
- .NET 10 SDK
- An OpenAI API key (for chat functionality)
- A Microsoft Dataverse environment (for real persistence — Phase 2; not required to run the current in-memory setup)

## Getting Started

1. Clone/open the solution (`NexusAI.slnx`) in Visual Studio.
2. Restore NuGet packages (Visual Studio does this automatically on open, or `dotnet restore`).
3. Configure your OpenAI API key using **User Secrets** — do **not** put it directly in `appsettings.json`:
   ```
   dotnet user-secrets set "OpenAI:ApiKey" "your-key-here" --project src/NexusAI.Host
   dotnet user-secrets set "OpenAI:ApiKey" "your-key-here" --project src/NexusAI.Api
   ```
4. Run `NexusAI.Host` to execute the end-to-end smoke test — it walks through creating a workspace, project, conversation, work item, session, knowledge entry, branch, artifact, ADR, and snapshot, then exercises the planner, execution engine, and a live chat-memory test against OpenAI.
5. Run `NexusAI.Api` to bring up the REST API with Swagger UI at `/swagger`.

## Security Note

Earlier versions of this repository had a live OpenAI API key committed directly in `appsettings.json`. If you're working from an older copy, **rotate that key immediately** and switch to User Secrets or environment variables as shown above. Never commit real credentials — this applies equally to the Dataverse `ClientSecret` once real Dataverse connectivity is wired in (Phase 2, Milestone 1).

## Documentation Map

| Document | Purpose |
|---|---|
| [VISION.md](./VISION.md) | Why NexusAI exists and where it's going |
| [ARCHITECTURE.md](./ARCHITECTURE.md) | System architecture, layers, data flow |
| [MODULES.md](./MODULES.md) | What each project/module is responsible for |
| [DATABASE.md](./DATABASE.md) | Domain entities and the Dataverse schema design |
| [API.md](./API.md) | REST API reference |
| [CONVENTIONS.md](./CONVENTIONS.md) | IDs, naming, numbering, UI standards, business rules |
| [CODING-STANDARDS.md](./CODING-STANDARDS.md) | C#/.NET naming and style conventions |
| [DECISIONS.md](./DECISIONS.md) | Architecture decisions and known issues log |
| [ROADMAP.md](./ROADMAP.md) | Phase 1 recap and the full Phase 2 milestone plan |
| [CHANGELOG.md](./CHANGELOG.md) | Version history |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | Developer guidelines (solo-dev workflow today) |
| [AI_CONTEXT.md](./AI_CONTEXT.md) | Onboarding doc for AI coding agents — current state, patterns, constraints |

## License

See [License](./License).
