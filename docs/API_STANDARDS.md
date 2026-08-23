# API Standards

**Status:** Active
**Owner:** CORE (Layer 01), applied by every layer that exposes an HTTP surface
**Last updated:** 2026-08-21
**Layer:** cross-cutting
**Authoritative for:** route naming, versioning, HTTP methods, request and response DTOs, error
shape, validation, pagination, filtering, sorting, idempotency, correlation identifiers, request
logging, rate limits, timeouts, retries, Problem Details, OpenAPI, backward compatibility and
deprecation.

Not authoritative for: how a caller proves who it is or what it may do — that is
`SECURITY_STANDARDS.md`; how the data behind an endpoint is stored — `DATABASE_STANDARDS.md`; where
a base URL or a key comes from — `CONFIGURATION_STANDARDS.md`; how an endpoint is proven correct —
`ASSURANCE_STANDARDS.md`.

---

## 1. Position

Nexus APIs are ASP.NET Core Minimal APIs on .NET 10. There is no MVC controller anywhere and none is
planned. An API surface is composed of endpoint files, each mapping its own routes.

**CURRENT.** Two HTTP surfaces exist:

| Surface | Host project | Base path | Local port |
|---|---|---|---|
| Chat product API | `Nexus.Products.Chat.Api` | `/api/v1` | `http://localhost:5299` |
| Intelligence API | `Nexus.Intelligence.Api` | `/intelligence/v1` | Not verified — do not state one |

The Chat port comes from `launchSettings.json` and is confirmed in `api_run.log`. The Intelligence
API has its own `launchSettings.json`, but its port has not been verified; do not quote a number for
it in documentation, scripts or examples until someone reads the file and records it.

---

## 2. Endpoint structure

**CURRENT.** One file per resource, named `<Name>Endpoint.cs`, exposing one extension method:

```csharp
public static IEndpointRouteBuilder Map<Name>Endpoints(this IEndpointRouteBuilder app)
```

The endpoint files that exist in `Nexus.Products.Chat.Api/Endpoints/`: `Artifacts`, `Branches`,
`Chat`, `ConversationMessage`, `Conversations`, `Knowledge`, `Projects`, `Sessions`, `Snapshots`,
`WorkItems`, `WorkSpaces`, `HealthEndpoint`. In `Nexus.Intelligence.Api/Endpoints/`: `Turns`,
`Plans`, `Results`, `Capabilities`, `Health`, `TurnRequestValidation`.

`Program.cs` calls each `Map*Endpoints` once. `ChatProductModule.cs` groups the product's
registration. An endpoint method contains routing, model binding, validation invocation and result
translation — and nothing else. Business logic lives behind an application service or a repository;
an endpoint body longer than roughly twenty lines is a signal that logic has leaked into it.

Two naming inconsistencies exist in the current code and should be noted rather than papered over:
`ConversationMessage` is singular where its siblings are plural, and `WorkSpaces` capitalises the S
where the route is `workspaces`. New files follow the plural, single-capital form
(`WorkspacesEndpoint.cs`); the two existing files are renamed opportunistically when they are next
edited for another reason, not as standalone churn.

---

## 3. Routes

### 3.1 Real routes

**CURRENT, confirmed in code:**

```
GET    /api/v1/workspaces
POST   /api/v1/workspaces
GET    /api/v1/workspaces/{id:guid}
```

### 3.2 Naming rules

| Rule | Statement | Example |
|---|---|---|
| Plural | Collections are plural nouns | `/workspaces`, not `/workspace` |
| Lowercase | Lowercase, hyphen-separated where multi-word | `/work-items` |
| Nouns | Resources are nouns; the verb is the HTTP method | `POST /workspaces`, not `/createWorkspace` |
| Versioned | Version is the first segment after the base | `/api/v1/...` |
| Constrained | Route parameters carry type constraints | `{id:guid}` |
| Shallow | At most one level of nesting | `/workspaces/{id:guid}/projects` |
| No file extensions | Content type is negotiated by header | never `/workspaces.json` |

Nesting stops at one level. `/workspaces/{a}/projects/{b}/conversations/{c}` is not a route; it is a
filter, and it is expressed as `/conversations?projectId={b}`. The exception is a genuinely
dependent child that has no identity of its own outside its parent — `ConversationMessage` under
`Conversation` qualifies, a `Project` under a `Workspace` does not.

### 3.3 Actions that are not CRUD

A state transition is a sub-resource with `POST`, not a verb in the path and not a `PATCH` that
happens to flip a status:

```
POST /api/v1/work-items/{id:guid}/submit
POST /api/v1/work-items/{id:guid}/approve
```

This keeps the transition auditable, permissions attachable per transition, and the request body
free to carry a reason. A status field that can be `PATCH`ed to any value is an authorization hole
(see `SECURITY_STANDARDS.md`).

### 3.4 Identifiers in routes

Routes take the `Guid` `Id`. The human-facing `Ref` (`WKS-00000001`, see
`DATABASE_STANDARDS.md` §3) is a lookup key, not a route key:

```
GET /api/v1/workspaces?ref=WKS-00000001
```

Two identifier formats in the same route position makes the route constraint ambiguous and the
handler branchy. One route position, one identifier type.

---

## 4. Versioning

| Rule | Statement |
|---|---|
| Location | URL path segment, `v1`, `v2` — never a header, never a query parameter |
| Granularity | One version per surface, not per endpoint |
| Increment | A new major version only for a change that would break an existing caller |
| Coexistence | `v1` and `v2` run side by side from the same host |
| Minor versions | Do not exist. Additive changes go into the current version |

**CURRENT: everything is `v1` and nothing has ever been versioned up.** The versioning rules exist
so that the first `v2` is handled correctly, not because a `v2` is imminent.

Path-based versioning is chosen because it is visible in a log line, a browser address bar and a
`curl` command without inspecting headers. Header-based versioning is more elegant and, in practice,
harder to debug.

---

## 5. HTTP methods and status codes

| Method | Use | Success | Idempotent |
|---|---|---|---|
| `GET` | Read a resource or collection | `200` | Yes |
| `POST` | Create, or invoke a transition | `201` create, `200` transition, `202` accepted | No |
| `PUT` | Full replacement of a resource | `200` | Yes |
| `PATCH` | Partial update of named fields | `200` | No |
| `DELETE` | Remove a resource | `204` | Yes |

`GET` never changes state. Not "should not" — never. A `GET` that mutates breaks caching, retries,
prefetch and every reasonable assumption a client makes.

`201 Created` carries a `Location` header pointing at the created resource, and the created
representation in the body.

Full status vocabulary:

| Code | Meaning in Nexus |
|---|---|
| `200 OK` | Success with a body |
| `201 Created` | Resource created; `Location` set |
| `202 Accepted` | Work queued; body carries a way to check on it |
| `204 No Content` | Success with nothing to say — deletes |
| `400 Bad Request` | Malformed request, or a validation failure |
| `401 Unauthorized` | No credential, or an invalid one |
| `403 Forbidden` | Valid credential, insufficient permission |
| `404 Not Found` | No such resource, **or one the caller may not know exists** |
| `409 Conflict` | Concurrency token mismatch, or a state transition that is not legal |
| `410 Gone` | Deprecated endpoint past its removal date |
| `422 Unprocessable` | Not used — validation failures are `400` |
| `429 Too Many Requests` | Rate limit; `Retry-After` set |
| `500 Internal Server Error` | Unhandled fault; correlation id in the body |
| `503 Service Unavailable` | Dependency unavailable; `Retry-After` where known |

The `404`-versus-`403` choice matters and is a security decision, not an API-shape decision: a
resource in another tenant returns `404`, because `403` would confirm that the resource exists. See
`SECURITY_STANDARDS.md` §tenant isolation.

---

## 6. Request and response DTOs

### 6.1 Naming

**CURRENT, in actual use:**

| Shape | Name |
|---|---|
| Create input | `Create<Name>Request` |
| Create output | `Create<Name>Response` |
| Read one | `Get<Name>Response` |
| Read many | `List<Name>Response` |
| Update input | `Update<Name>Request` |
| Update output | `Update<Name>Response` |

So `CreateWorkspaceRequest`, `CreateWorkspaceResponse`, `GetWorkspaceResponse`,
`ListWorkspaceResponse`, `UpdateWorkspaceRequest`, `UpdateWorkspaceResponse`. The pattern is
mechanical on purpose — a reader who knows one endpoint knows them all.

### 6.2 Rules

| Rule | Statement |
|---|---|
| Type | C# `record` — positional or with init-only properties |
| Location | The API project, alongside the endpoint that uses them |
| Never a domain type | An aggregate is never serialised directly to the wire |
| Never shared across surfaces | Chat DTOs are not reused by Intelligence, or vice versa |
| No inheritance | DTOs do not derive from each other |
| Explicit nullability | Nullable reference types on, and honoured |
| Enums as strings | Serialised as names, not integers — an integer in JSON is unreadable and brittle |

Mapping between DTO and domain is explicit code. No automatic mapper is in use and none is
selected — see §17.

Serialising a domain aggregate directly is the single most common way an API accidentally becomes
a public schema: every private field, every renamed property and every added child becomes a
breaking change for callers. The DTO is the contract; the aggregate is an implementation detail.

### 6.3 Response envelopes

Single resources are returned bare, not wrapped. Collections are wrapped, because they need
pagination metadata (§9). There is no universal `{ "data": ..., "success": true }` envelope; the
HTTP status code already carries success or failure and duplicating it in the body invites the two
to disagree.

---

## 7. Errors — Problem Details

**TARGET.** Every non-`2xx` response carries an RFC 7807 Problem Details body,
`application/problem+json`:

```json
{
  "type": "https://nexus/errors/validation",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "Name must not be empty.",
  "instance": "/api/v1/workspaces",
  "correlationId": "0HN7A1B2C3D4E",
  "errors": {
    "name": ["Name must not be empty."]
  }
}
```

| Field | Rule |
|---|---|
| `type` | A stable URI identifying the error class; never changes once published |
| `title` | Human-readable, invariant per `type` — does not vary with the instance |
| `status` | Matches the HTTP status code exactly |
| `detail` | Instance-specific, safe to show a user, **never contains internal state** |
| `instance` | The request path |
| `correlationId` | The correlation id for this request (§11) |
| `errors` | Field-keyed validation messages, present only on `400` |

`detail` never contains a stack trace, a SQL statement, a connection string, a file path, an internal
type name or a database identifier that the caller is not otherwise entitled to. A `500` returns a
generic detail and the correlation id; the actual fault is in the log.

**CURRENT.** ASP.NET Core produces Problem Details for some framework-generated failures by default,
but Nexus does not yet have a uniform exception-to-Problem-Details handler on either host, so the
shape of a `500` is not currently guaranteed. Closing this is a prerequisite of
**M-10-1.1 Correlation across hosts**, which is what makes `correlationId` populatable in the first
place. Until then, write new endpoints to return Problem Details explicitly.

---

## 8. Validation

Validation runs at the edge, before any domain object is constructed.

| Layer | Responsibility |
|---|---|
| Route constraints | Shape — `{id:guid}` rejects a non-GUID before the handler runs |
| Request validation | Presence, length, range, format, enum membership |
| Domain invariants | Rules that require domain knowledge, enforced in the aggregate |
| Database constraints | Last line of defence — uniqueness, referential integrity |

**CURRENT.** `Nexus.Intelligence.Api/Endpoints/TurnRequestValidation` is the only request validation
component in the system. It is hand-written. **No validation library is selected** — FluentValidation
and DataAnnotations are both plausible and neither has been chosen. Until one is, validation is
hand-written in the same style, returning a `400` with a field-keyed `errors` dictionary.

A validation failure returns `400` with every failing field, not the first one. Returning one error
at a time forces the caller into a round-trip-per-field loop.

Domain invariant violations are not validation errors. A `Workspace` that refuses an illegal status
transition returns `409 Conflict`, because the request was well-formed and the state was wrong.

---

## 9. Pagination, filtering and sorting

**TARGET — no listing endpoint paginates today.** `GET /api/v1/workspaces` returns everything. That
is survivable at the current data volume and will not be at the first real tenant. The standard
below applies to every new listing endpoint from now on, and existing ones are retrofitted before
any product carries real data.

### 9.1 Pagination

Offset pagination by default:

```
GET /api/v1/workspaces?page=1&pageSize=50
```

| Parameter | Rule |
|---|---|
| `page` | 1-based; default 1; a page beyond the end returns an empty `items`, not `404` |
| `pageSize` | Default 50, maximum 200; a larger request is clamped, not rejected |

Response shape:

```json
{
  "items": [ ... ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 137
}
```

`totalCount` is a second query and is therefore optional on endpoints where the count is expensive;
where it is omitted, the field is absent rather than zero or null.

Keyset pagination — `?after=<opaque cursor>` — is used instead where the collection is large,
append-heavy or ordered by time: conversation messages, audit entries, operational events. Offset
pagination over an append-heavy collection skips and repeats rows as new ones arrive.

### 9.2 Filtering

Filters are explicit named query parameters. There is no generic query language, no OData, no
`?filter=name eq 'x'`:

```
GET /api/v1/work-items?status=InProgress&projectId={guid}&createdAfter=2026-08-01T00:00:00Z
```

| Rule | Statement |
|---|---|
| Named | Every filterable field is a declared parameter |
| Additive | Multiple filters combine with AND |
| Multi-value | Repeat the parameter for OR: `?status=Draft&status=InProgress` |
| Indexed | Every filterable column is indexed — `DATABASE_STANDARDS.md` §5.2 |
| Enumerable | The OpenAPI document lists every valid value |

A generic query language turns every index into a guess and every endpoint into an unbounded
query surface. Named filters can be indexed, permission-checked and rate-limited.

### 9.3 Sorting

```
GET /api/v1/workspaces?sort=createdAtUtc&direction=desc
```

`sort` accepts only an allow-listed field name per endpoint. `direction` is `asc` or `desc`,
defaulting to `asc`. Every listing has a deterministic default sort — without one, pagination
returns overlapping and missing rows across pages. Where the natural sort is not unique, `Seq` is
appended as the tie-breaker.

---

## 10. Idempotency

| Method | Idempotent | Mechanism |
|---|---|---|
| `GET`, `PUT`, `DELETE` | By definition | None needed |
| `POST` create | No | `Idempotency-Key` header |
| `POST` transition | No | State check — a second `approve` on an approved item returns `409` |

**TARGET.** For creates that a client may retry — anything a network timeout could duplicate — the
client sends:

```
Idempotency-Key: <client-generated GUID>
```

The server stores the key with the response for 24 hours. A repeat of the same key returns the
original response without re-executing. A repeat of the same key with a *different* body returns
`409 Conflict`, because that is a client bug worth surfacing.

`DELETE` on an already-deleted resource returns `204`, not `404`. The caller's intent — that the
resource not exist — is satisfied.

**CURRENT: no idempotency mechanism exists.** The store this needs is a persistence concern and
lands with the work that makes retries meaningful. Until then, clients must not blindly retry
`POST`; the frontend's TanStack Query mutations do not auto-retry mutations, which is the correct
default and must not be changed.

---

## 11. Correlation identifiers

**TARGET — M-10-1.1 Correlation across hosts.**

| Rule | Statement |
|---|---|
| Header | `X-Correlation-Id` |
| Generation | Generated at the edge if the caller did not supply one |
| Acceptance | Accepted from the caller when supplied, and validated for shape |
| Propagation | Flows through the Experience API, the Intelligence turn, and the model invocation |
| Response | Echoed on every response, success or failure |
| Errors | Present in every Problem Details body |
| Logs | Present on every log line for the request |

The acceptance test for this milestone is exact: **one request must be retrievable end to end by
that id alone**, across all three hosts.

**CURRENT: no correlation id exists on any host.** A request cannot presently be followed from the
frontend through the Chat API into Intelligence and out to OpenAI. This is the single largest gap in
operability and is why M-10-1.1 is a P1 milestone rather than a nicety.

---

## 12. Logging

**No logging library is selected.** Serilog is not present. Nothing beyond the built-in
`Microsoft.Extensions.Logging` abstractions exists in any repository. Choosing one is part of
**M-10-1.1**, whose work item is explicitly "Logging foundation — structured logging with a
correlation enricher".

What is already binding, whatever library is eventually chosen:

| Rule | Statement |
|---|---|
| Structured | Log structured properties, never interpolated strings |
| One line per request | Method, path, status, duration in ms, correlation id |
| No secrets | **No log line contains a secret, a token or a full prompt body** |
| No PII by default | Identifiers, not names or email addresses |
| Errors carry context | The correlation id and the operation, never the raw exception to the caller |
| Levels | `Information` for request completion; `Warning` for handled failure; `Error` for unhandled |

The prohibition on logging full prompt bodies is an acceptance criterion of M-10-1.1 and a hard
rule, not a preference: a prompt body routinely contains a user's actual content, and a log store
has different access controls from the conversation store. `SECURITY_STANDARDS.md` §logging
restrictions is authoritative on what may never be written.

---

## 13. Rate limits, timeouts and retries

### 13.1 Rate limits

**TARGET.** ASP.NET Core's built-in rate limiting is used when limits are introduced. Limits are per
authenticated principal where identity exists, and per IP address before it does. A limited request
returns `429` with `Retry-After` in seconds.

Two limits are needed before public exposure and neither is optional:

1. **Sign-in** — lockout after repeated failure is an explicit task of **M-01-1.2 Authentication
   flow**. Without it, the sign-in endpoint is a credential-stuffing target.
2. **Model-invoking endpoints** — an unlimited `POST /intelligence/v1/turns` is an unlimited bill.

**CURRENT: no rate limiting exists on any endpoint.**

### 13.2 Timeouts

| Call | Timeout |
|---|---|
| Inbound HTTP request | 30 s default |
| Database command | 30 s default; longer only where recorded and justified |
| Model invocation | Longer, bounded, and always explicit — never infinite |
| Outbound service call | 10 s default |

Every outbound call has a timeout. A call without one inherits the platform default, which is
effectively infinite, and one slow dependency then consumes the whole thread pool.

### 13.3 Retries

| Situation | Retry |
|---|---|
| `GET`, `PUT`, `DELETE` on a transport failure | Yes — up to 3, exponential backoff with jitter |
| `POST` without an idempotency key | **No** |
| `POST` with an idempotency key | Yes |
| `429` | Yes, honouring `Retry-After` |
| `4xx` other than `429` | Never — the request is wrong and will stay wrong |
| `5xx` | Yes, bounded, with backoff |

No resilience library is selected. Where retries are needed today they are written explicitly with
bounded attempts and jittered backoff, so that a downstream recovery is not immediately flattened by
every client retrying in lockstep.

---

## 14. OpenAPI

**CURRENT.** `Swashbuckle.AspNetCore` is in use — `Swashbuckle.AspNetCore.Swagger`, `.SwaggerGen`
and `.SwaggerUI` are all present. The Intelligence host's `launchSettings.json` sets
`launchUrl: swagger`, so Swagger UI opens on run in development.

| Rule | Statement |
|---|---|
| Generated | The document is generated from code; it is never hand-maintained |
| Documented responses | Every endpoint declares its response types with `Produces<T>` |
| Documented failures | Every endpoint declares the failure codes it can return |
| Summaries | Every endpoint has `WithName` and `WithSummary` |
| Environments | Swagger UI is enabled in development; disabled elsewhere unless deliberately exposed |
| Tags | Grouped by resource, matching the endpoint file name |

A generated document that does not list a `404` the endpoint actually returns is worse than no
document, because a client will trust it.

---

## 15. Backward compatibility

Within a published version, these changes are safe and may ship at any time:

| Safe | Breaking |
|---|---|
| Adding an endpoint | Removing an endpoint |
| Adding an optional request field | Adding a required request field |
| Adding a response field | Removing or renaming a response field |
| Adding an enum member to a response | Adding an enum member the client must handle to function |
| Widening an accepted range | Narrowing an accepted range |
| Relaxing validation | Tightening validation |
| Adding an optional header | Changing a status code for an existing condition |
| Making a required field optional | Changing a field's type or its units |

Two rules that catch most accidental breakage:

- **A client must tolerate unknown response fields.** The frontend's typed models describe what it
  reads, not an exhaustive schema, and adding a field must never break parsing.
- **Adding an enum member to a response is safe only if clients handle unknown members.** If a
  client switches exhaustively on a status, a new status is a breaking change in effect if not in
  form. Frontend enum handling always has a default branch.

Anything in the breaking column requires a new major version.

---

## 16. Deprecation

| Step | Requirement |
|---|---|
| 1. Announce | The successor exists and is documented before deprecation is announced |
| 2. Mark | `[Obsolete]` on the handler; `deprecated: true` in the OpenAPI document |
| 3. Signal | Responses carry `Deprecation` and `Sunset` headers with the removal date |
| 4. Observe | Usage is monitored; removal does not proceed while a real caller remains |
| 5. Remove | After the sunset date, the route returns `410 Gone` for one further version |
| 6. Delete | The route disappears in the next major version |

Minimum notice: 90 days for an external caller, one integration cycle for an internal one. Deleting
an endpoint on the same day it is deprecated is not deprecation, it is a removal with a note
attached.

Nothing is deprecated today. Every endpoint that exists is `v1` and current.

---

## 17. Frontend contract

**CURRENT.** The React client consumes these APIs through `src/api/ApiClient.ts` and
`src/api/ApiError.ts`, with one typed API module per feature — `workspacesApi.ts`, `projectsApi.ts`,
`chatApi.ts`, `systemApi.ts` — and TanStack Query hooks over the top (`useWorkspaces.ts`,
`useCreateWorkspace.ts`, `useConversationMessages.ts`, and the rest).

Contract rules that fall on the client side:

| Rule | Statement |
|---|---|
| One client | Every request goes through `ApiClient.ts`; no component calls `fetch` |
| One error type | Every failure surfaces as `ApiError`, carrying status and Problem Details |
| Typed models | `Workspace.ts`, `Project.ts`, `chat.types.ts` mirror the response DTOs |
| Base URL from config | `config/environment.ts` with the `VITE_` prefix — `CONFIGURATION_STANDARDS.md` |
| No mutation auto-retry | Query retries are acceptable; mutation retries are not, until idempotency exists |
| Unknown fields tolerated | Models describe what is read, not the full schema |

`RouteErrorBoundary.tsx` is the last-resort surface for an unhandled failure. It shows the
correlation id once M-10-1.1 makes one available — a user who can quote an id turns an unreproducible
report into a one-query investigation.

---

## 18. Not yet decided

| Question | What would decide it | Milestone |
|---|---|---|
| Validation library | First endpoint with non-trivial validation rules | — |
| Logging library | Logging foundation work item | M-10-1.1 |
| Resilience library | First unreliable external dependency in production | — |
| Object mapping library | Not needed; explicit mapping is currently preferred | — |
| Streaming protocol for chat responses | Streaming and realtime presence | M-11-7.1 |
| Whether Intelligence is ever public | Governance decision, not a technical one | — |
| API gateway or reverse proxy | First multi-host deployment | M-08-4.1 |

---

## 19. References

- `SECURITY_STANDARDS.md` — authentication, authorization, tenant isolation, what `404` versus `403`
  means, and what may never be logged.
- `DATABASE_STANDARDS.md` — `Id`/`Seq`/`Ref`, indexing for filters and sorts, concurrency tokens.
- `CONFIGURATION_STANDARDS.md` — base URLs, ports, `VITE_` variables, environment overrides.
- `ASSURANCE_STANDARDS.md` — contract tests and how an endpoint's behaviour is proven.
- `DEVELOPMENT_WORKFLOW.md` — when a contract change may be made while other work is in flight.
