# API Contract

This document reflects routes found in the reviewed code. Runtime Swagger/OpenAPI is the final source of truth until a versioned contract is published.

All route IDs use GUID constraints.

**Under V2.1** (see `NEXUS_ARCHITECTURE_V2.md` and ADR-013/ADR-014 in
`08_DECISIONS_AND_TECHNICAL_DEBT.md`), these are the routes of `Nexus.Products.Chat.Api` in
the `Nexus.Experience` solution — the Chat product's own contract. They are versioned at `/api/v1`.
This is a separate contract from the Intelligence API below; the product never exposes a
Platform or Intelligence route directly.

## Current routes — Chat product, `/api/v1`

| Feature | Method | Route | Purpose |
|---|---|---|---|
| Workspace | POST | `/api/v1/workspaces` | Create |
| Workspace | GET | `/api/v1/workspaces` | List |
| Workspace | GET | `/api/v1/workspaces/{id}` | Get |
| Workspace | PUT | `/api/v1/workspaces/{id}` | Update |
| Project | POST | `/api/v1/projects` | Create |
| Project | GET | `/api/v1/projects/{id}` | Get |
| Project | GET | `/api/v1/workspaces/{workspaceId}/projects` | List by workspace |
| Project | PUT | `/api/v1/projects/{id}` | Update |
| Conversation | POST | `/api/v1/conversations` | Create |
| Conversation | GET | `/api/v1/conversations/{id}` | Get |
| Conversation | GET | `/api/v1/projects/{projectId}/conversations` | List by project |
| Conversation | PUT | `/api/v1/conversations/{id}` | Update |
| Message | GET | `/api/v1/conversations/{conversationId}/messages` | List messages |
| Chat | POST | `/api/v1/chat` | Send prompt — internally calls `IIntelligenceClient.SendTurnAsync`, not a vendor SDK |
| Work Item | POST | `/api/v1/projects/{projectId}/workitems` | Create |
| Work Item | GET | `/api/v1/workitems/{id}` | Get |
| Work Item | GET | `/api/v1/projects/{projectId}/workitems` | List by project |
| Work Item | PUT | `/api/v1/workitems/{id}` | Update |
| Knowledge | POST | `/api/v1/workspaces/{workspaceId}/knowledge` | Create |
| Knowledge | GET | `/api/v1/knowledge/{id}` | Get |
| Knowledge | GET | `/api/v1/workspaces/{workspaceId}/knowledge` | List by workspace |
| Branch | POST | `/api/v1/branches` | Create |
| Branch | GET | `/api/v1/branches/{id}` | Get |
| Branch | GET | `/api/v1/conversations/{conversationId}/branches` | List by conversation |
| Branch | PUT | `/api/v1/branches/{id}` | Update |
| Snapshot | POST | `/api/v1/snapshots` | Create |
| Snapshot | GET | `/api/v1/snapshots/{id}` | Get |
| Snapshot | GET | `/api/v1/branches/{branchId}/snapshots` | List by branch |
| Snapshot | PUT | `/api/v1/snapshots/{id}` | Update |
| Session | POST | `/api/v1/sessions` | Create |
| Session | GET | `/api/v1/sessions/{id}` | Get |
| Session | GET | `/api/v1/conversations/{conversationId}/sessions` | List by conversation |
| Session | PUT | `/api/v1/sessions/{id}` | Update status |
| Artifact | POST | `/api/v1/workitems/{workItemId}/artifacts` | Create |
| Artifact | GET | `/api/v1/artifacts/{id}` | Get |
| Artifact | GET | `/api/v1/workitems/{workItemId}/artifacts` | List by work item |
| Artifact | PUT | `/api/v1/artifacts/{id}` | Update |

## Intelligence contract — `Nexus.Intelligence`, `/intelligence/v1`

This is a second, separate contract, owned by the `Nexus.Intelligence` solution, not the Chat product.
Products **may only reach Intelligence through the `Nexus.Intelligence.Contracts` NuGet
package** — its `IIntelligenceClient` interface and the `IntelligenceTurnRequest` /
`IntelligenceTurnResponse` / `ContextBundle` records. No product may call these HTTP routes
directly, hold an HTTP client pointed at `/intelligence/v1`, or reference `Nexus.Intelligence` project
types. See §2.3 and §3 of `NEXUS_ARCHITECTURE_V2.md` for the full contract shape.

| Method | Route | Purpose |
|---|---|---|
| POST | `/intelligence/v1/turns` | Send a turn — intent classification, context ranking, model routing, tool use, reply |
| POST | `/intelligence/v1/results` | Report a real-world outcome for a prior turn (the Result Loop) |
| GET | `/intelligence/v1/turns/{id}/explanation` | Why this answer — the recorded decision trace |
| POST | `/intelligence/v1/plans` | Decompose an objective into a plan |
| GET | `/intelligence/v1/capabilities` | What this Intelligence instance can do today |

The Chat product's `POST /api/v1/chat` handler is the only caller of this contract, via
`IIntelligenceClient.SendTurnAsync`. Intelligence in turn never calls a vendor SDK directly —
it reaches the model through `Nexus.Platform.*`, in-process, as described in ADR-012 and
ADR-013.

## Current request contracts — Chat product

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

## Key response contracts — Chat product

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

| # | Problem | Status |
|---|---|---|
| 1 | List envelopes are inconsistent. | Open |
| 2 | Some DTOs return raw GUIDs while Knowledge create exposes a typed ID. | Open |
| 3 | Some routes accept raw integers and others accept enum types. | Open |
| 4 | Conversation get does not return all fields accepted by create/update. | Open |
| 5 | Failure semantics for Chat need one rule: HTTP error, response envelope, or a clearly documented combination. | Open |
| 6 | Pagination, sorting, filtering, API versioning, and authentication are not defined. | **Partially resolved** — routes are versioned at `/api/v1` (above). Pagination, sorting, filtering, and authentication remain open; see the Critical/Medium items in `08_DECISIONS_AND_TECHNICAL_DEBT.md`. |
| 7 | Error and validation payloads are not standardized. | Open — the product API currently returns raw .NET stack traces on malformed request bodies; see `08_DECISIONS_AND_TECHNICAL_DEBT.md`. |
| 8 | Delete/archive operations are absent. | Open |
| 9 | ADR, Memory, and Milestone have no frontend-facing contract. | **Changed** — Memory is no longer a product concern under V2.1; it moved to `Nexus.Intelligence.Memory`, keyed by `ScopeRef`, with no product-facing contract at all (see `12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md`). ADR and Milestone remain open. |
| 10 | The sample Weather Forecast endpoint should be removed. | **Resolved** — removed during the V2 migration cleanup. |

## Contract standard for frontend foundation

- Use a common problem-details error response.
- Use consistent list envelopes: `items`, `total`, `page`, `pageSize`, and optional continuation token.
- Serialize enums as documented strings or generate a shared typed client after numeric values are frozen.
- Serialize date/time as ISO 8601 UTC.
- Document nullable fields explicitly.
- Add stable Swagger operation IDs and feature tags.
- ~~Add `/api/v1` before the public contract is released.~~ Done — see the route tables above.
- Keep all secrets and Dataverse access in the backend; Platform credentials are additionally
  never reachable from the product at all, by construction (§2.3 of `NEXUS_ARCHITECTURE_V2.md`).

For the first frontend slice, a small handwritten TypeScript client is acceptable. Generate a client only after the contract above is stable.
