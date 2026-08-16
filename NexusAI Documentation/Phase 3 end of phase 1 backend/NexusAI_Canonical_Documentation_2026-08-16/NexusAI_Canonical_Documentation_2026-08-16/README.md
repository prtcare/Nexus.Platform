# NexusAI

Canonical documentation baseline — 16 August 2026

NexusAI is a persistent, structured AI platform for turning conversations into organized projects, decisions, knowledge, work, and reusable outputs. It is the shared platform and intelligence layer; products such as the Nexus chatbot, Vault, ERP clients, developer agents, and machine-automation agents consume it through APIs.

## Current status

Phase 1 foundation is complete. Phase 2 backend persistence is substantially implemented. The current repository contains end-to-end paths for Workspace, Project, Conversation, Conversation Message, Work Item, Knowledge, Branch, Snapshot, Session, Artifact, and Chat, together with Dataverse and OpenAI infrastructure.

The next milestone is **API Contract Stabilization and Frontend Foundation**. The first frontend slice should be:

`Workspace → Project → Conversation → Messages/Chat`

Project Milestones should be implemented immediately after that first slice because they are central to the intended product hierarchy but are not present in the reviewed code.

## Technology

- .NET 10 and C#
- ASP.NET Core Minimal APIs and Swagger/OpenAPI
- Clean Architecture with command/query handlers and repositories
- Microsoft Dataverse as the operational source of truth
- Provider-neutral `ILLMProvider`, currently backed by OpenAI
- Registry-based agent framework

## Solution projects

| Project | Responsibility |
|---|---|
| `NexusAI.Domain` | Entities, aggregates, typed IDs, enums, business rules, repository contracts |
| `NexusAI.Application` | Commands, queries, handlers, chat, knowledge retrieval, planning, execution |
| `NexusAI.Core` | Shared agent and runtime abstractions |
| `NexusAI.Agents` | Concrete agents, currently including Developer Agent |
| `NexusAI.Infrastructure` | Dataverse, LLM providers, repositories, mappers, service registration |
| `NexusAI.Api` | HTTP endpoints, request/response contracts, Swagger |
| `NexusAI.Host` | Composition/host process |
| `NexusAI.Foundation` | Shared foundation project |

## Documentation map

| File | Read it for |
|---|---|
| `01_VISION_AND_PRODUCT_MODEL.md` | Product purpose, layers, principles, success criteria |
| `02_ARCHITECTURE_AND_MODULES.md` | Technical architecture, dependency rules, runtime flows |
| `03_DOMAIN_AND_DATAVERSE.md` | Domain hierarchy, persistence model, schema registry, relationships |
| `04_API_CONTRACT.md` | Current frontend-facing HTTP contract and known inconsistencies |
| `05_FRONTEND_PRODUCT_DESIGN.md` | Information architecture, screens, behavior, implementation sequence |
| `06_ENVIRONMENTS_CONFIGURATION_DEPLOYMENT.md` | Dev/Test/Prod, secrets, Dataverse solution deployment |
| `07_DEVELOPMENT_GUIDE.md` | Setup, standards, vertical-slice workflow, review checklist |
| `08_DECISIONS_AND_TECHNICAL_DEBT.md` | Accepted architectural decisions and current debt |
| `09_ROADMAP_AND_MILESTONES.md` | Completed work, current milestone, future sequence |
| `10_CHANGELOG.md` | Consolidated history |

## Getting started

1. Install the .NET 10 SDK.
2. Configure Dataverse and OpenAI secrets using user secrets or secure environment configuration.
3. Run `dotnet restore NexusAI.slnx`.
4. Run `dotnet build NexusAI.slnx`.
5. Start the canonical API entry point.
6. Verify the first-slice routes in Swagger against the development Dataverse environment.

Do not commit API keys, client secrets, passwords, certificates, connection strings containing secrets, or real production credentials.

## Review limitation

The supplied source was statically inspected. The review environment did not contain the .NET SDK, so a fresh build could not be executed there. Run and record a clean build on the developer machine before frontend work begins.
