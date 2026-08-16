# API Contract

This document reflects routes found in the reviewed code. Runtime Swagger/OpenAPI is the final source of truth until a versioned contract is published.

All route IDs use GUID constraints.

## Current routes

| Feature | Method | Route | Purpose |
|---|---|---|---|
| Workspace | POST | `/api/workspaces` | Create |
| Workspace | GET | `/api/workspaces` | List |
| Workspace | GET | `/api/workspaces/{id}` | Get |
| Workspace | PUT | `/api/workspaces/{id}` | Update |
| Project | POST | `/api/projects` | Create |
| Project | GET | `/api/projects/{id}` | Get |
| Project | GET | `/api/workspaces/{workspaceId}/projects` | List by workspace |
| Project | PUT | `/api/projects/{id}` | Update |
| Conversation | POST | `/api/conversations` | Create |
| Conversation | GET | `/api/conversations/{id}` | Get |
| Conversation | GET | `/api/projects/{projectId}/conversations` | List by project |
| Conversation | PUT | `/api/conversations/{id}` | Update |
| Message | GET | `/api/conversations/{conversationId}/messages` | List messages |
| Chat | POST | `/api/chat` | Send prompt |
| Work Item | POST | `/api/projects/{projectId}/workitems` | Create |
| Work Item | GET | `/api/workitems/{id}` | Get |
| Work Item | GET | `/api/projects/{projectId}/workitems` | List by project |
| Work Item | PUT | `/api/workitems/{id}` | Update |
| Knowledge | POST | `/api/workspaces/{workspaceId}/knowledge` | Create |
| Knowledge | GET | `/api/knowledge/{id}` | Get |
| Knowledge | GET | `/api/workspaces/{workspaceId}/knowledge` | List by workspace |
| Branch | POST | `/api/branches` | Create |
| Branch | GET | `/api/branches/{id}` | Get |
| Branch | GET | `/api/conversations/{conversationId}/branches` | List by conversation |
| Branch | PUT | `/api/branches/{id}` | Update |
| Snapshot | POST | `/api/snapshots` | Create |
| Snapshot | GET | `/api/snapshots/{id}` | Get |
| Snapshot | GET | `/api/branches/{branchId}/snapshots` | List by branch |
| Snapshot | PUT | `/api/snapshots/{id}` | Update |
| Session | POST | `/api/sessions` | Create |
| Session | GET | `/api/sessions/{id}` | Get |
| Session | GET | `/api/conversations/{conversationId}/sessions` | List by conversation |
| Session | PUT | `/api/sessions/{id}` | Update status |
| Artifact | POST | `/api/workitems/{workItemId}/artifacts` | Create |
| Artifact | GET | `/api/artifacts/{id}` | Get |
| Artifact | GET | `/api/workitems/{workItemId}/artifacts` | List by work item |
| Artifact | PUT | `/api/artifacts/{id}` | Update |

## Current request contracts

| Operation | Fields |
|---|---|
| Create Workspace | `name`, `owner`, `description` |
| Update Workspace | `name`, `owner`, `description` |
| Create Project | `workspaceId`, `name` |
| Update Project | `name` |
| Create Conversation | `projectId`, `workspaceId`, `title`, `description`, `type`, `visibility` |
| Update Conversation | `title`, `description`, `type`, `visibility` |
| Send Chat | `conversationId`, `prompt` |
| Create Work Item | `title`, `description`, `type` |
| Update Work Item | `title`, nullable `description`, `type`, `status` |
| Create Knowledge | `title`, `content`, `type` |
| Create Branch | `conversationId`, `name`, `description` |
| Update Branch | `name`, `description`, `status` |
| Create Snapshot | `branchId`, `conversationId`, `name`, `state` |
| Update Snapshot | `name`, `state` |
| Create Session | `conversationId` |
| Update Session | `status` |
| Create/Update Artifact | `name`, `type`, `content` |

## Key response contracts

- Workspace get: ID, name, owner, description, status, created time.
- Project get: ID, Workspace ID, name, created time.
- Conversation get: ID, title, created time.
- Message list item: ID, role, content, created time.
- Chat: success, reply, error.
- Work Item get: ID, Project ID, title, description, type, status, created time.
- Branch get: ID, Conversation ID, name, description, status, created time.
- Snapshot get: ID, Branch ID, Conversation ID, name, state, created time.
- Session get: ID, Conversation ID, status, start time, optional end time.
- Artifact get: ID, Work Item ID, name, type, content, created time.
- Artifact list intentionally omits content; call the get route for full content.

## Contract problems to stabilize

1. List envelopes are inconsistent.
2. Some DTOs return raw GUIDs while Knowledge create exposes a typed ID.
3. Some routes accept raw integers and others accept enum types.
4. Conversation get does not return all fields accepted by create/update.
5. Failure semantics for Chat need one rule: HTTP error, response envelope, or a clearly documented combination.
6. Pagination, sorting, filtering, API versioning, and authentication are not defined.
7. Error and validation payloads are not standardized.
8. Delete/archive operations are absent.
9. ADR, Memory, and Milestone have no frontend-facing contract.
10. The sample Weather Forecast endpoint should be removed.

## Contract standard for frontend foundation

- Use a common problem-details error response.
- Use consistent list envelopes: `items`, `total`, `page`, `pageSize`, and optional continuation token.
- Serialize enums as documented strings or generate a shared typed client after numeric values are frozen.
- Serialize date/time as ISO 8601 UTC.
- Document nullable fields explicitly.
- Add stable Swagger operation IDs and feature tags.
- Add `/api/v1` before the public contract is released.
- Keep all secrets and Dataverse access in the backend.

For the first frontend slice, a small handwritten TypeScript client is acceptable. Generate a client only after the contract above is stable.
