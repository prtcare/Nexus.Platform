# Nexus Documentation

Canonical documentation for all three Nexus repositories. Last reviewed 2026-08-20.

Nexus is a persistent, structured AI system for turning conversations into organised
projects, decisions, knowledge, work and reusable outputs. It is built as three layers, each
its own repository and solution:

> **Intelligence decides. Platform executes. Products own the data and the experience.**

## The three solutions

| Solution | Repository | Is | Deployed as |
|---|---|---|---|
| **Nexus.Platform** | `C:\Personal\Nexus.Platform` | Platform — model gateways, provider neutrality, usage metering, quota, audit | NuGet packages, never run directly |
| **Nexus.Intelligence** | `C:\Personal\Nexus.Intelligence` | Intelligence — policy, intent, context ranking, agent and model selection, prompt assembly | `/intelligence/v1` |
| **Nexus.Experience** | `C:\Personal\Nexus.Experience` | Chat product — domain, database, API and React client | `/api/v1` |

Platform holds **no product data and no product schema**. Workspaces, projects,
conversations, knowledge and chat all belong to the Chat product, because every future
product will structure its data differently.

## Current status

V2.1 three-solution restructure is **complete** — all ten stages, tagged `v2-arch` in all
three repos. The end-to-end path is verified: browser → product API → Intelligence → policy
gate → ranking → agent and model selection → prompt assembly → Platform gateway → OpenAI.

Two workstreams are in flight:

- **Azure SQL migration** (ADR-014) — replacing Dataverse. Stage 1 leak test passed; Stages
  1b through 2c are next. Dataverse data is disposable, so this is a schema rewrite, not a
  data migration.
- **Frontend chat UI** — F0 complete. The chat UI is the measuring instrument; without it,
  every intelligence change is unfalsifiable.

See `09_ROADMAP_AND_MILESTONES.md` for the sequence and
`08_DECISIONS_AND_TECHNICAL_DEBT.md` for current debt.

## Technology

- .NET 10 and C#, ASP.NET Core Minimal APIs, Swagger/OpenAPI
- Clean Architecture — command/query handlers, repositories, strongly typed IDs
- EF Core code-first against SQL Server (LocalDB now, Azure SQL at Stage 4)
- Provider-neutral model gateway, currently backed by OpenAI
- React + Vite client (`Nexus.Experience.Client`)
- Architecture tests (NetArchTest) enforcing the layer boundaries in each solution

## Documentation map

| File | Read it for |
|---|---|
| `00_DOCUMENTATION_STANDARD.md` | Where documentation lives, numbering, ownership, update rules. **Read before adding a document.** |
| `01_VISION_AND_PRODUCT_MODEL.md` | Product purpose, layers, principles, success criteria |
| `02_ARCHITECTURE_AND_MODULES.md` | Technical architecture, dependency rules, runtime flows |
| `03_DOMAIN_AND_DATAVERSE.md` | Domain hierarchy and persistence model. **Stale** — contradicted by ADR-014; rewritten and renamed to `03_DOMAIN_AND_PERSISTENCE.md` at SQL Stage 3 |
| `04_API_CONTRACT.md` | Frontend-facing HTTP contract |
| `05_FRONTEND_PRODUCT_DESIGN.md` | Information architecture, screens, behaviour, sequence |
| `06_ENVIRONMENTS_CONFIGURATION_DEPLOYMENT.md` | Dev/Test/Prod, secrets, deployment |
| `07_DEVELOPMENT_GUIDE.md` | **Coding standards, naming conventions**, vertical-slice workflow, review checklist |
| `08_DECISIONS_AND_TECHNICAL_DEBT.md` | The ADR log (one global sequence, ADR-015 next) and current debt |
| `09_ROADMAP_AND_MILESTONES.md` | Completed work, current milestone, future sequence |
| `10_CHANGELOG.md` | Consolidated history |
| `11_FUTURE_OF_NEXUS_AI.md` | Long-range direction |
| `12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md` | Entity model and relationships — all of it belongs to the **Chat product**, not to the platform |

Supporting references, kept alongside:

| File | Read it for |
|---|---|
| `ADR-014_AZURE_SQL_MIGRATION.md` | The Azure SQL decision, the staged plan, and the schema cleanup rules |
| `NEXUS_ARCHITECTURE_V2.md` | The V2.1 blueprint — contracts, dependency edges, file-by-file map |
| `NEXUS_MIGRATION_RUNBOOK.md` | The V2.1 restructure runbook |
| `DATAVERSE_SCHEMA_REFERENCE.md` | The Dataverse schema as exported, with its 34 anomalies catalogued. Retire once Stage 3 deletes Dataverse |

## Getting started

1. Install the .NET 10 SDK. `global.json` pins the version.
2. Set the model-provider key with `set-openai-key.ps1`. It belongs to `Nexus.Intelligence` only,
   under `Platform:Providers:OpenAI:ApiKey` — a product must never hold a provider
   credential. Never use `dotnet user-secrets set`; it parks the value in shell history.
3. Build and pack Platform: `dotnet build Nexus.Platform.slnx` then `.\pack-local.ps1`, which
   publishes to the local feed at `C:\Personal\LocalNuGet`.
4. Build and run Intelligence: `dotnet build Nexus.Intelligence.slnx`, then run
   `Nexus.Intelligence.Api`. Swagger at `http://localhost:5000/swagger`.
5. Build and run the product: `dotnet build Nexus.Experience.slnx`, run
   `Nexus.Products.Chat.Api`, then `npm install && npm run dev` in `src\Nexus.Experience.Client`.
6. Verify a chat turn end to end.

Do not commit API keys, client secrets, passwords, certificates, connection strings
containing secrets, or real production credentials.

## Historical snapshots

`..\NexusAI Documentation\Phase1 Complete 08-08-2026\` and `..\NexusAI Documentation\Phase 2\`
are kept for provenance only. They describe the single-solution, Dataverse-era system and
are **not** current — do not consult them for how Nexus works today.
