# API.md

## Overview

`NexusAI.Api` exposes a REST API using ASP.NET Core minimal APIs (not MVC controllers, aside from one leftover scaffolded `WeatherForecastController` that should be removed). Swagger UI is available at `/swagger` when running locally. All routes are prefixed `/api`.

**Current coverage**: Chat, Conversations, Projects, WorkItems, Knowledge. **Not yet exposed**: Workspaces (no creation endpoint exists at all), Planning, Execution, Branches, Sessions, Artifacts, ADRs, Snapshots, Agents. See "Gaps" at the end of this document.

## Chat

### `POST /api/chat`
Send a message in a conversation; response is generated with full memory context (history + ranked workspace knowledge).

**Request**
```json
{
  "conversationId": { "value": "guid-here" },
  "prompt": "string"
}
```
**Response**
```json
{
  "success": true,
  "reply": "string",
  "error": "string"
}
```
⚠️ **Note**: `conversationId` is serialized as a wrapped object (`{"value": "..."}`), not a plain GUID string — because the request DTO uses the domain `ConversationId` value-object type directly instead of a plain `Guid`. This is inconsistent with every other endpoint in the API (which accept/return plain GUID strings) and is a real integration gotcha for any client (Power Apps, desktop app) calling this endpoint. See [DECISIONS.md](./DECISIONS.md).

## Conversations

### `POST /api/conversations`
**Request**: `{ "projectId": "guid", "title": "string" }`
**Response**: `{ "conversationId": "guid", "title": "string" }`

### `GET /api/conversations/{id}`
**Response**: `{ "conversationId": "guid", "title": "string", "createdAt": "datetime" }` — `404` if not found.

### `GET /api/projects/{projectId}/conversations`
**Response**: array of `{ "conversationId": {"value": "guid"}, "title": "string", "createdAt": "datetime" }`
⚠️ Same value-object serialization issue as Chat above — this endpoint returns the raw Application-layer result record directly rather than mapping to a dedicated Response DTO, so `conversationId` is wrapped.

### `GET /api/conversations/{id}/messages`
**Response**: array of `{ "messageId": "guid", "role": "User" | "Assistant" | "System", "content": "string", "createdOn": "datetime" }`

## Projects

### `POST /api/projects`
**Request**: `{ "workspaceId": "guid", "name": "string" }`
**Response**: `{ "projectId": "guid", "name": "string" }`

### `GET /api/projects/{id}`
**Response**: `{ "projectId": "guid", "workspaceId": "guid", "name": "string", "createdAt": "datetime" }` — `404` if not found.

### `GET /api/workspaces/{workspaceId}/projects`
**Response**: array of `{ "projectId": "guid", "name": "string", "createdAt": "datetime" }`

## Work Items

### `POST /api/projects/{projectId}/workitems`
**Request**: `{ "title": "string", "description": "string", "type": 1 }` — `type` is the raw integer value of `WorkItemType` (Task=1, Bug=2, Feature=3, Epic=4, Story=5, Research=6, Idea=7, Spike=8).
**Response**: `{ "workItemId": "guid" }`

### `GET /api/workitems/{id}`
**Response**: `{ "workItemId": "guid", "projectId": "guid", "title": "string", "description": "string", "type": 1, "status": 1, "createdAt": "datetime" }` — `status` is the raw int of `WorkItemStatus` (New=1, Active=2, Blocked=3, Completed=4, Cancelled=5). `404` if not found.

### `GET /api/projects/{projectId}/workitems`
**Response**: array in the same shape as the single-item GET above.

### `PUT /api/workitems/{id}`
**Request**: `{ "title": "string", "description": "string", "type": 1, "status": 1 }`
**Response**: `{ "workItemId": "guid" }` — `404` if not found.

## Knowledge

### `POST /api/workspaces/{workspaceId}/knowledge`
**Request**: `{ "title": "string", "content": "string", "source": 1 }` — `source` is the raw int of `KnowledgeSource` (Document=1, Web=2, Code=3, User=4).
**Response**: `{ "knowledgeId": {"value": "guid"} }`
⚠️ Same value-object serialization issue — `CreateKnowledgeResponse` declares `KnowledgeId` as the wrapped value-object type instead of `Guid`, unlike every sibling `Create*Response` in the API.

### `GET /api/knowledge/{id}`
**Response**: raw `GetKnowledgeResult` — `{ "knowledgeId": {"value": "guid"}, "workspaceId": {"value": "guid"}, "title": "string", "content": "string", "source": 1, "createdAt": "datetime" }`. `404` if not found.
⚠️ This endpoint returns the Application-layer result directly rather than a dedicated API Response DTO — both ID fields are wrapped objects, not plain GUID strings.

### `GET /api/workspaces/{workspaceId}/knowledge`
**Response**: array of raw `ListKnowledgeResult` — same wrapped-ID caveat as above, and note this list response omits `content` (only `title`, `source`, `createdAt` are included per item).

## Type Reference

| Enum | Values |
|---|---|
| `WorkItemType` | Task=1, Bug=2, Feature=3, Epic=4, Story=5, Research=6, Idea=7, Spike=8 |
| `WorkItemStatus` | New=1, Active=2, Blocked=3, Completed=4, Cancelled=5 |
| `KnowledgeSource` | Document=1, Web=2, Code=3, User=4 |
| `ConversationMessageRole` | User=1, Assistant=2, System=3 |

See [CONVENTIONS.md](./CONVENTIONS.md) for the full authoritative list across all entities, including ones not yet exposed via the API.

## Gaps (Not Yet Implemented)

These exist in the Domain/Application layers but have **no API endpoint** yet:

- **Workspaces** — there is no `POST /api/workspaces` at all. A workspace can currently only be created by calling `CreateWorkspaceHandler` directly (as `NexusAI.Host` does) — there's no way for a front-end client to create one today.
- **Planning** (`CreatePlanHandler`) and **Execution** (`ExecutePlanHandler`) — no endpoints. A front end can't yet trigger the planner or run an execution.
- **Branches**, **Sessions**, **Artifacts**, **ADRs**, **Snapshots** — full CRUD exists in the Application layer, no Api exposure.
- **Agents** — no way to list available agents or invoke one directly via the API.

Closing these gaps is **Phase 2, Milestone 3** (see [ROADMAP.md](./ROADMAP.md)) — required before the Power Apps and desktop front ends can be built against a complete surface.

## Authentication

There is currently **no authentication or authorization** on any endpoint. This is acceptable for local development against the in-memory backend, but must be addressed before the API is exposed to real front ends or a real Dataverse backend with real business data. Tracked as part of Milestone 3.
