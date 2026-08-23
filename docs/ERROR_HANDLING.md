# Error Handling

**Status:** Active
**Owner:** CORE (Layer 01), applied by every layer
**Last updated:** 2026-08-21
**Layer:** cross-cutting
**Authoritative for:** the failure taxonomy used across the whole stack, which failures are values
and which are exceptions, exception boundaries, the error-class → HTTP status → log level →
user-message mapping, transient versus permanent classification, timeout and cancellation
*semantics*, frontend error surfaces, and failure handling in AI turns, workflows and workers.

Not authoritative for: the Problem Details wire format and status vocabulary — `API_STANDARDS.md`
§§5, 7; the C# mechanics of throwing and catching — `CSHARP_STANDARDS.md` §8; the cross-language
mechanism table for cancellation, timeouts and retries — `CODE_CONVENTIONS.md` §§12–14; what a log
record contains — `OBSERVABILITY_STANDARDS.md` §3; what may never appear in a message —
`SECURITY_STANDARDS.md` §11.

---

## 1. Position

Three questions decide every error decision in Nexus, in this order:

1. **Is this an expected outcome, a caller error, or a genuine fault?** — it decides the mechanism.
2. **Is it transient?** — it decides whether a retry is legitimate.
3. **Who needs to know?** — it decides the status code, the log level and the message.

`CODE_CONVENTIONS.md` §6 states the three categories and the per-language mechanism. This document
does not repeat that table; it extends it into the classes, boundaries and mappings that the
categories imply.

**CURRENT.** Error handling in Nexus today is partly right and partly absent. Right: the value-based
failure shapes already exist and are used — `QuotaVerdict`, `ToolResult`, `TurnError`,
`ResultOutcome`, `ApiError`. Absent: there is no uniform exception-to-Problem-Details handler on
either host, no correlation id in any message, no retry policy anywhere, and no timeout policy
anywhere.

---

## 2. The failure taxonomy

Eight classes. Every failure in Nexus is one of them, and the class determines everything downstream.

| Class | Definition | Nexus example | Mechanism |
|---|---|---|---|
| **Validation** | The request is malformed or violates a stated rule, before any domain object exists | `TurnRequestValidation` rejects a turn with no input | Value at the boundary |
| **Domain rule** | The request is well-formed but the domain forbids it | A `Workspace` status transition that is not legal | Domain exception or a result value |
| **Not found** | The addressed resource does not exist, or the caller may not know it does | `GET /api/v1/workspaces/{id}` for another tenant's id | Value |
| **Conflict** | A concurrency token mismatch, or a state the resource is no longer in | Two edits to one `Project` | Value or `DbUpdateConcurrencyException` translated at the boundary |
| **Policy refusal** | A deliberate refusal by a governing rule — not a failure of the system | `PolicyGate` refuses; `IQuotaPolicy` returns an exceeded `QuotaVerdict`; a tool refuses via `ToolResult` | **Always a value. Never an exception** |
| **Infrastructure** | A dependency the system owns did not work | Azure SQL unreachable, migration not applied | Exception, caught at a boundary |
| **External dependency** | A dependency the system does not own did not work | OpenAI returned `5xx`, a throttle, or nonsense | Exception or a mapped value at the gateway |
| **Programming fault** | A bug: a null where the type said non-null, an unreachable branch reached | Anything reaching `RouteErrorBoundary.tsx` | Exception; never caught to hide it |

### 2.1 The rule that matters most

**A refusal is not a fault.** `QuotaVerdict`, `ToolResult`, `TurnError` and `ResultOutcome` exist so
that "refused", "could not proceed" and "failed with a reason" are *values a caller can inspect*.
Throwing to signal one of these is wrong on four counts: it costs a stack unwind for an expected
path, it loses the reason unless it is re-encoded in a message, it forces every caller into a
`try/catch` for normal behaviour, and it teaches everyone to catch broadly.

`Nexus.Intelligence.Contracts/Turns/TurnError` is the shape a turn uses to say what stopped it. If a
new failure mode in the turn pipeline cannot be expressed as a `TurnError`, extend `TurnError` —
do not start throwing.

### 2.2 Transient versus permanent

Orthogonal to the class, and it decides one thing only: **may this be retried?**

| Transient | Permanent |
|---|---|
| Network fault, connection reset | Validation failure |
| SQL deadlock victim (error 1205) | Domain rule violation |
| `429` throttle from a provider | Policy refusal — a retry is a second refusal |
| `503` from a dependency with `Retry-After` | `401` / `403` |
| A timeout on an **idempotent** operation | A timeout on a non-idempotent operation with unknown outcome |

The last row is the one that causes duplicates. A timed-out `POST /api/v1/workspaces` may have
succeeded on the server. Retrying it without an idempotency key creates two workspaces —
`CODE_CONVENTIONS.md` §16.

---

## 3. Exception boundaries

An exception is caught at a boundary that can **decide** something. Everywhere else it propagates.

| Boundary | Catches | Converts to | State |
|---|---|---|---|
| **HTTP endpoint** (`Program.cs` / endpoint file) | Anything unhandled | Problem Details + `500`, correlation id in body | **TARGET** — no uniform handler exists on either host |
| **Model gateway** (`RoutingModelGateway`, provider gateways) | Provider transport and protocol faults | A mapped failure value, or a rethrow after classification | TARGET |
| **Tool gateway** (`IToolGateway`) | A tool's own fault | `ToolResult` carrying the failure | TARGET |
| **Turn pipeline** (`TurnPipeline`) | A step's fault | `TurnError` with the step named | TARGET |
| **Job dispatcher** | Anything a job throws | A failed job record, then retry or dead-letter | **TARGET — M-05-1.2, M-05-1.3** |
| **React route** (`RouteErrorBoundary.tsx`) | A render-time throw | A recoverable error surface | **CURRENT** |

Between these, `catch` only what you can act on. `catch (Exception)` outside a host boundary is
forbidden — `CSHARP_STANDARDS.md` §8 — and a caught exception is handled, logged with context, or
rethrown with `throw;`. `throw ex;` erases the stack trace and destroys the diagnosis.

### 3.1 The host boundary handler

**TARGET.** One handler per host, converting an unhandled exception into a Problem Details response.
Its rules:

- It logs the exception **once**, with the correlation id, at `Error`.
- It returns a **generic** `detail` and the correlation id. Never a stack trace, a SQL statement, a
  connection string, a file path or an internal type name — `API_STANDARDS.md` §7.
- It never swallows: an unhandled exception that produced a `500` is always logged.
- It is the **only** place `catch (Exception)` appears in the codebase.

This handler is a prerequisite for M-10-1.1 in practice, because a `500` with no correlation id is
an incident with no thread to pull.

---

## 4. The mapping table

The single table this document exists for. Read it left to right: the class decides the status, the
status decides the level, and the level decides who finds out.

| Error class | HTTP status | Log level | User-visible message policy |
|---|---|---|---|
| **Validation** | `400` | `Information` | Full detail. Field-keyed `errors`. The user can fix it, so tell them exactly what is wrong |
| **Domain rule** | `409` (illegal transition) or `400` (invalid input) | `Information` | The rule, in the domain's own words: *"A workspace that is archived cannot be renamed."* |
| **Not found** | `404` | `Information` | "Not found." **Nothing more** — see §4.1 |
| **Conflict / concurrency** | `409` | `Warning` | "This was changed by someone else. Reload and try again." Actionable, not apologetic |
| **Unauthenticated** | `401` | `Warning` | "Sign in required." Never why the credential failed |
| **Unauthorized** | `403` | `Warning` | "You do not have access." Never what would grant it |
| **Cross-tenant access** | `404` | `Warning` | "Not found." **Deliberately indistinguishable from a real 404** |
| **Policy refusal (quota)** | `429` if rate-limited, `403` if entitlement | `Warning` | The limit and when it resets. A refusal the user cannot understand reads as a bug |
| **Policy refusal (tool/turn)** | `200` with a `TurnError`/`ToolResult` payload | `Information` | The refusal reason, in plain words. This is a normal answer, not an HTTP failure |
| **Rate limited** | `429` + `Retry-After` | `Warning` | "Too many requests. Try again in N seconds." |
| **Timeout (inbound)** | `504` if a dependency timed out, `503` if shedding load | `Warning` | "This took too long. Try again." |
| **Cancellation (by caller)** | No response — the caller left | `Information` | None. Nobody is listening |
| **Infrastructure** | `503` + `Retry-After` where known | `Error` | "Temporarily unavailable." Never the dependency's name |
| **External dependency** | `502` or `503` | `Error` (`Warning` if a retry then succeeded) | "The assistant is unavailable." Never the provider's error text |
| **Programming fault** | `500` | `Error` | A generic sentence **plus the correlation id**. Nothing else |
| **Data loss / security failure** | `500` | `Critical` | Generic. Then a security incident — `SECURITY_STANDARDS.md` |

### 4.1 Why `404` appears twice

A resource belonging to another tenant returns `404`, not `403`, because `403` confirms that the
resource exists. That is a security decision, owned by `SECURITY_STANDARDS.md` §3.4 and repeated in
`API_STANDARDS.md` §5. It appears here because the *log* line must distinguish what the *response*
deliberately does not: a cross-tenant attempt is logged at `Warning` with the tenant and principal
ids, because a real `404` is noise and a cross-tenant `404` is a signal.

### 4.2 The three user-message rules

| Rule | Statement |
|---|---|
| Name what failed and what was attempted | *"Workspace 3f2a… not found."* Never "Error", never "Something went wrong" |
| Never render a server exception verbatim | It leaks internals — `TYPESCRIPT_REACT_STANDARDS.md` §10 |
| Give the user their next action | Retry, reload, sign in, or quote the correlation id. A message with no next step is a dead end |

---

## 5. Timeouts

`CODE_CONVENTIONS.md` §13 owns the mechanism and the per-boundary rules. **TARGET — no timeout
policy exists in the codebase.** The semantics that belong here:

- A timeout is an **outcome, not an absence of one**. It is logged, counted (§10.2 of
  `OBSERVABILITY_STANDARDS.md`) and mapped per §4.
- **An inner timeout is shorter than its outer timeout.** When the model-provider timeout exceeds
  the API request timeout, the client gives up while the server keeps burning provider cost, and
  both the log and the bill record a success.
- A timeout on a non-idempotent operation leaves the outcome **unknown**, not failed. Treating
  unknown as failed and retrying is how duplicates are created.
- A timeout is not a bug report. If a boundary times out routinely, the limit or the operation is
  wrong; raising the limit hides the finding.

---

## 6. Cancellation

**CURRENT — and this is one of the few things already right.** `CancellationToken` is threaded
through the real repository interfaces in `Nexus.Products.Chat.Domain`. Do not regress it.

Cancellation is **not an error**. It is the caller withdrawing the request.

| Rule | Statement |
|---|---|
| A cancelled operation logs at `Information`, not `Error` | Nobody is waiting for the answer |
| A cancelled operation produces no response | The socket is gone |
| Cancellation is cooperative and must leave state consistent | `CODE_CONVENTIONS.md` §§12, 15 |
| Never convert a cancellation into a `500` | An `OperationCanceledException` from a client disconnect is not a fault |
| Distinguish caller cancellation from a timeout | A timeout is the system deciding; cancellation is the caller deciding. Same exception type in C#, different meanings, different levels |

On the client, `AbortSignal` from TanStack Query's query function is the mechanism: navigating away
from `ChatPage` aborts the in-flight request rather than resolving into an unmounted component.

---

## 7. Retries

**TARGET — no retry logic exists. M-05-1.2 Dispatch loop with retry and backoff** is the milestone.
`CODE_CONVENTIONS.md` §14 owns the rules. The two that are error-handling decisions rather than
mechanism:

- **Retry only what §2.2 classes as transient, and only what §16 of `CODE_CONVENTIONS.md` classes as
  idempotent.** Both, not either.
- **Never nest retries.** Retry at one layer. Three layers of three attempts is twenty-seven calls
  and an outage amplifier — and each layer's logs will show a reasonable-looking three.

A retry that eventually succeeded is a `Warning`, not a silence. The fact that it was needed is the
signal; the success is not.

---

## 8. Domain and validation errors in C#

### 8.1 Validation

Validation runs at the edge, before a domain object is constructed — `API_STANDARDS.md` §8. A
domain constructor may still guard its invariants, and should, but a `400` is produced by the edge,
not by catching a constructor throw. Catching your own guard clause to produce a status code turns
an invariant into control flow.

**CURRENT.** `Nexus.Intelligence.Api/Endpoints/TurnRequestValidation` is the only named validation
component in the system. No validation library is selected — `API_STANDARDS.md` §18.

### 8.2 Domain exceptions

A domain exception is `sealed`, ends in `Exception`, and carries the identifiers needed to diagnose
it — `CSHARP_STANDARDS.md` §8. It is used for a violated invariant that a caller could not
reasonably have anticipated. Where the caller *could* have anticipated it — the aggregate's status
forbids the transition — a result value is better, because the caller must handle it either way.

Aggregates in `Nexus.Products.Chat.Domain` each carry a status enum (`<Name>Status.cs`); a
transition guard on that enum is the most common source of a domain rule error in Nexus, and it maps
to `409`.

### 8.3 Persistence errors

| Situation | Handling |
|---|---|
| Concurrency token mismatch | `409` — `DATABASE_STANDARDS.md` §7 |
| Unique constraint violation on `Ref` | A programming fault: `Ref` is DB-computed and unique by construction. Investigate, do not retry |
| Multiple cascade paths (error 1785) | A **design** error caught at migration time, never at runtime — `DATABASE_STANDARDS.md` §5.3 |
| Deadlock victim (error 1205) | Transient; retriable if the transaction is idempotent |
| Migration not applied | Infrastructure: fail **readiness**, do not serve traffic and error per request |

---

## 9. Frontend errors

Three surfaces for three failures — `TYPESCRIPT_REACT_STANDARDS.md` §10 owns the table. What the two
existing files are and what is expected of them:

### 9.1 `src/api/ApiError.ts` — CURRENT

The single typed error crossing the transport boundary. Every non-`2xx` response from
`ApiClient.ts` becomes an `ApiError`; nothing else escapes the client. It carries the HTTP status
and the response's Problem Details body, which is what lets a caller branch on *why* rather than
parse a string.

| Rule | Statement |
|---|---|
| Only `ApiClient.ts` constructs it | A component that builds an `ApiError` has bypassed the single HTTP path |
| Only `ApiError` escapes the transport | Raw `fetch` rejections, JSON parse failures and aborts are all normalised inside the client |
| It is branched on, not stringified | `401` routes to sign-in, `409` prompts a reload, `429` shows the wait — a single message for all statuses discards the information the status carries |
| It carries the correlation id once one exists | **TARGET — M-10-1.1**. `ApiClient.ts` is the single seam that reads the header |

`ApiError`'s exact member names live in the file; this document does not restate them, because a
standard that duplicates a signature becomes wrong the first time the file is edited.

### 9.2 `src/components/RouteErrorBoundary.tsx` — CURRENT

The last-resort surface for a render-time throw, mounted around routed content by
`routes/AppRoutes.tsx` within `layouts/AppLayout.tsx`.

| Rule | Statement |
|---|---|
| It is a last resort, not a strategy | **Reaching it means a bug.** Every arrival is worth investigating |
| A **query** failure never reaches it | That is the hook's `isError` branch, rendered inline by the component that owns the query. Sending every failed fetch to the boundary blanks the screen for a recoverable problem |
| It offers recovery, not only apology | A retry or a route home. A dead end forces a full reload and loses unsaved input |
| It shows the correlation id | **TARGET — M-10-1.1.** A user who can quote an id turns an unreproducible report into one query |
| It never renders a server exception message | `SECURITY_STANDARDS.md` — it can leak internals |

`pages/NotFoundPage.tsx` is the third surface and handles an unknown route — a routing outcome, not
an error.

### 9.3 The frontend rule that catches the most bugs

**Every query hook's consumer handles three states: loading, error, ready.** Two-state handling is
the most common defect shape in this codebase. TanStack Query's `data` is `undefined` before the
first successful fetch — that is *loading*, not *empty*, and conflating them renders "no
conversations" to a user who has fifty.

---

## 10. AI failures

An AI failure is different in kind: the call can succeed and the answer still be wrong. Four
distinct failure modes, only two of which are errors.

| Mode | Detection | Handling |
|---|---|---|
| **Transport failure** | Provider returned `5xx`, timed out, or refused the connection | Transient. Retry with backoff at **one** layer; then a `TurnError` naming unavailability |
| **Throttle** | `429` from the provider | Transient. Honour `Retry-After`. Surface as a wait, not a failure |
| **Refusal** | The model or `PolicyGate` declined | **Not an error.** A `TurnError` or a refusal payload with the reason. Logged at `Information` |
| **Bad output** | Parsed badly, unsupported by citations, or plainly wrong | **Not an exception.** Guardrails and validation — **TARGET — M-04-5.2** |

Rules specific to this layer:

- **A model invocation that failed still cost money and still emits telemetry.** Record the model,
  the attempt, the token counts if any, and the outcome — `OBSERVABILITY_STANDARDS.md` §11.
- **Never log the prompt or completion body when handling a failure.** This is the moment the rule is
  most tempting to break and it has no exception — `SECURITY_STANDARDS.md` §11.
- **A degraded turn is better than a failed turn where the degradation is honest.** If context
  retrieval fails, a turn answering without retrieved context — and saying so — beats a `500`.
  Silent degradation is worse than either.
- The pipeline step that failed is named in the `TurnError`. `TurnPipeline` has nine steps; "the
  turn failed" is not a diagnosis.

---

## 11. Workflow and worker failures

**TARGET — AUTOMATION F-05-1 (M-05-1.1 to M-05-1.3) and DEVELOPER F-07-3.** Nothing here exists
today: there is no job runner, no workflow engine and no dispatcher.

### 11.1 Jobs and workflows

| Rule | Statement | Milestone |
|---|---|---|
| A failed job is a **record**, not a log line | It has an id, a state and a history someone can query | M-05-1.1 |
| Retry is bounded and backed off, with jitter | Fixed-interval retry from many callers synchronises them | M-05-1.2 |
| After the bound, **dead-letter** — never drop | A job that disappears is worse than one that failed loudly | M-05-1.3 |
| A dead-lettered job is owned by someone | Escalation is part of the mechanism, not a follow-up | M-05-1.3 |
| A workflow instance failing mid-way leaves durable state | The instance is resumable or explicitly abandoned, never ambiguous | M-05-2.2 |
| A job carries the originating correlation id | It is how a background failure joins to the user action that caused it | M-10-1.1 |

### 11.2 Development workers

A worker — human or agent — failing is a **development** event, not a runtime one, and its handling
is `DEVELOPMENT_WORKFLOW.md` §2.3. The error-handling rules that belong here:

| Rule | Statement |
|---|---|
| A failing build blocks its own work item and **no other** | M-07-4.1 acceptance criterion; S-07-4.1.2.1.1 tests it by failing worker B and confirming A and C complete |
| A red integration build **halts the batch** | T-07-5.1.2.2. Continuing to merge onto a red integration branch multiplies one failure into many |
| A rejected review returns the item to its worker **with the reason recorded** | M-07-5.1 acceptance criterion |
| A production defect is a **new** work item | Not a reopened one — the original item's history stays true |
| An agent worker exceeding its permissions is a security event | `SECURITY_STANDARDS.md` §12 |

---

## 12. Anti-patterns

Each of these has a specific cost, stated.

| Anti-pattern | Cost |
|---|---|
| `catch (Exception)` outside a host boundary | Hides faults you did not anticipate, which are exactly the ones worth seeing |
| `throw ex;` instead of `throw;` | Resets the stack trace — the diagnosis is gone |
| Empty `catch { }` | The failure never happened, as far as anyone can tell |
| Exceptions as control flow | A stack unwind per expected outcome, and every caller wrapped in `try` |
| Logging and rethrowing at every frame | One incident looks like five |
| A generic "Something went wrong" | Guarantees a support conversation and provides nothing to it |
| Returning `403` for a cross-tenant resource | Confirms the resource exists |
| A stack trace in a response body | Hands an attacker the internal type graph |
| Retrying a `4xx` | It will fail identically, more expensively |
| A silent `catch` on a mutation | The user believes their work was saved |
| Swallowing a failed telemetry call | The **one** legitimate silent catch — telemetry never breaks the UI |

---

## 13. Where Nexus is weakest today

| Gap | Consequence | Closed by |
|---|---|---|
| No uniform exception handler on either host | The shape of a `500` is not guaranteed; internals may leak | Prerequisite of M-10-1.1 |
| No correlation id in any error | A `500` is an incident with no thread to pull | M-10-1.1 |
| No retry or timeout policy anywhere | A slow provider hangs a request indefinitely | M-05-1.2; `CODE_CONVENTIONS.md` §13 |
| No idempotency mechanism | A retried `POST` creates duplicates | `CODE_CONVENTIONS.md` §16 |
| Exactly two behaviour tests exist | No error path in the system is proven | `ASSURANCE_STANDARDS.md` §2 |
| No dead-letter path | A failed background operation would vanish silently, if one existed | M-05-1.3 |

The honest summary: **the error *shapes* in Nexus are well chosen and the error *handling* is not
yet built.** `QuotaVerdict`, `ToolResult`, `TurnError` and `ApiError` are the right primitives.
There is no boundary handler behind them, no correlation to join them, and no test proving any of
them behaves as described.

---

## 14. References

- `API_STANDARDS.md` §§5, 7, 8, 11 — status vocabulary, Problem Details, validation, correlation.
- `CODE_CONVENTIONS.md` §§6, 12–16 — error categories, cancellation, timeouts, retries, transactions, idempotency.
- `CSHARP_STANDARDS.md` §8 — exception mechanics.
- `TYPESCRIPT_REACT_STANDARDS.md` §§10, 14 — client error surfaces and `AbortSignal`.
- `OBSERVABILITY_STANDARDS.md` §§4, 10 — levels and error telemetry.
- `SECURITY_STANDARDS.md` §§3.4, 11 — `403` versus `404`, and what may never be logged.
- `DATABASE_STANDARDS.md` §§5.3, 7 — cascade paths and concurrency.
- `DEVELOPMENT_WORKFLOW.md` §2.3 — what a failed state transition does to a work item.
