# Architecture and Modules

## Architectural style

NexusAI follows Clean Architecture with domain-centric modeling, command/query application handlers, repository abstractions, dependency injection, minimal HTTP endpoints, and infrastructure adapters.

The dependency direction is:

`API/Host → Application → Domain`

`Infrastructure → Application/Domain contracts`

The Domain must not know that Dataverse, OpenAI, Swagger, or ASP.NET Core exists.

## Layer responsibilities

### Domain

Contains business concepts and invariants:

- aggregates and entities;
- strongly typed IDs;
- statuses and types;
- behavior such as rename, archive, update, start, and end;
- repository interfaces.

Domain code must not contain Dataverse logical names, SDK `Entity` objects, HTTP DTOs, secret handling, or provider SDK calls.

### Application

Coordinates use cases:

- command/query/result records;
- handlers;
- chat orchestration;
- prompt/context construction;
- knowledge retrieval and ranking;
- planning and execution interfaces;
- transaction/use-case validation.

Handlers depend on repository/provider abstractions and return application results. They do not directly manipulate Dataverse SDK objects.

### Infrastructure

Implements external integration:

- `IDataverseClient` and Dataverse context;
- Dataverse entity representations;
- domain-to-Dataverse mappers;
- repository implementations;
- OpenAI provider;
- clocks, registries, and service registration.

Each persistent feature should have a domain model, repository contract, Dataverse entity, mapper, repository, application handler, API route, registration, and verification.

### API

Owns transport concerns:

- route definitions;
- request and response DTOs;
- HTTP status mapping;
- validation at the boundary;
- Swagger/OpenAPI metadata.

Endpoints should remain thin and call application handlers.

### Core and Agents

`NexusAI.Core` defines `IAgent`, registry/runtime contracts, agent metadata, contexts, and results. `NexusAI.Agents` contains concrete agents. Agents use capabilities exposed through controlled abstractions; they must not bypass permissions or persistence rules.

### Host and Foundation

`NexusAI.Host` is a composition/hosting project. `NexusAI.Foundation` is reserved for genuinely shared primitives. Avoid turning Foundation into a miscellaneous dependency bucket.

## Primary runtime flows

### Standard command

`HTTP request → Endpoint → Command handler → Domain/repository → Dataverse → HTTP response`

### Standard query

`HTTP request → Endpoint → Query handler → Repository → Mapper → Result DTO → HTTP response`

### Chat

`POST /api/chat → SendChatHandler → Conversation context → Knowledge context → Prompt builder → ILLMProvider → Persist messages → Reply`

### Agent execution

`Execution request → Planner → Agent registry/dispatcher → Selected agent → Controlled tools → Execution result → Persist result/artifact`

## Repository and mapper pattern

- Domain repositories expose domain concepts, not Dataverse query syntax.
- A Dataverse entity class holds logical column mapping.
- A mapper performs both directions and handles missing optional fields safely.
- List repositories filter server-side wherever possible.
- Unknown Dataverse choice values must not crash the entire query; define a deliberate fallback or validation policy.
- Strongly typed IDs are converted only at boundaries.

## Current feature coverage

| Feature | Current coverage |
|---|---|
| Workspace | Domain, application, Dataverse, create/get/list/update API |
| Project | Domain, application, Dataverse, create/get/list/update API |
| Conversation | Domain, application, Dataverse, create/get/list/update API |
| Conversation Message | Domain, Dataverse, list API; creation through Chat |
| Work Item | Domain, application, Dataverse, create/get/list/update API |
| Knowledge | Domain, application, Dataverse, create/get/list API |
| Branch | Domain, application, Dataverse, create/get/list/update API |
| Snapshot | Domain, application, Dataverse, create/get/list/update API |
| Session | Domain, application, Dataverse, create/get/list/update API |
| Artifact | Domain, application, Dataverse, create/get/list/update API |
| ADR | Domain/repository and create application path; no public API found |
| Memory | Domain/repository infrastructure; no public API found |
| Project Milestone | Planned; no current implementation found |

## Composition decision still required

The repository contains both `NexusAI.Api` and `NexusAI.Host`. Before frontend work, choose and document the canonical development and deployment entry point. Ensure endpoint registration, configuration, user secrets, and dependency injection are not split inconsistently between the two processes.

## Testing architecture

Add automated projects aligned to behavior, not merely layers:

- Domain unit tests for invariants and enum/state transitions.
- Application handler tests with repository/provider fakes.
- Mapper tests for every Dataverse column and missing optional values.
- Repository integration tests against the development/test environment.
- API contract tests for routes, status codes, validation, and JSON.
- End-to-end tests for the first frontend journey.
