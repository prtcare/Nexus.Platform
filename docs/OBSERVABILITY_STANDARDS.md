# Observability Standards

**Status:** Active
**Owner:** OPERATIONS (Layer 10)
**Last updated:** 2026-08-21
**Layer:** 10 OPERATIONS — cross-cutting
**Authoritative for:** the shape of a log record, log level semantics and operational consequence,
the event taxonomy, correlation and trace identifiers as they flow *inside* a process and *between*
hosts, metrics, health checks, error telemetry, performance telemetry, AI telemetry, build
telemetry, deployment telemetry, retention of operational signal, and operational dashboards.

Not authoritative for: **what may never be written to a log** — `SECURITY_STANDARDS.md` §11 is
absolute on that and this document does not soften it; the HTTP contract for the correlation header
and the `correlationId` field in a Problem Details body — `API_STANDARDS.md` §§7, 11, 12; the
language mechanics of obtaining a logger — `CSHARP_STANDARDS.md` §11 and `CODE_CONVENTIONS.md` §11;
the frontend telemetry seam — `TYPESCRIPT_REACT_STANDARDS.md` §17; whether a requirement was
satisfied — `ASSURANCE_STANDARDS.md`.

---

## 1. Position

**CURRENT: Nexus has no observability.** Not weak observability — none.

| Capability | State today |
|---|---|
| Structured logging | No logging library in any repository. `Microsoft.Extensions.Logging` abstractions only |
| Correlation id | Does not exist on any host |
| Distributed traces, metrics, dashboards | Do not exist |
| Health checks | `HealthEndpoint.cs` (Chat) and `Endpoints/Health` (Intelligence) exist; neither probes a dependency |
| AI telemetry | `InMemoryTurnTraceStore` — lost on restart |
| Build telemetry | No CI exists, so no build produces a record |
| Deployment telemetry | Nothing is deployed |

A request from `ChatPage.tsx` through `ApiClient.ts` into `Nexus.Products.Chat.Api`, onward to
`Nexus.Intelligence.Api`, through `TurnPipeline` and out to OpenAI cannot presently be followed; if
it fails at the third hop, the only recovery is to reproduce it.

### 1.1 Why correlation is gate-critical

**M-10-1.1 Correlation across hosts** is a P1 milestone, not a convenience item, for one reason:
**correlation is the only observability capability whose cost rises with every line of code
written.** A log line added today without a correlation id must be found and edited later; a call
chain that does not forward the identifier must be re-threaded through every intermediate signature.
Metrics can be bolted on to a running system and dashboards built at any time; correlation cannot,
because it is a property of every call site simultaneously.

Its acceptance criteria, quoted rather than paraphrased: *a correlation id is generated at the edge
or accepted from the caller; it propagates through the Experience API, the Intelligence turn and the
model invocation;* **one request is retrievable end to end by that id alone;** and **no log line
contains a secret, a token or a full prompt body.**

That last criterion is a security control living inside an observability milestone, and it binds
**now**, before the milestone, on every line of code written in the interval.

---

## 2. The logging library — not yet decided

**No logging library has been selected.** Serilog is not present. Nothing beyond the
`Microsoft.Extensions.Logging` abstractions exists in NexusAI, Nexus.Int or Nexus.Web, and
`TECHNOLOGY_STACK.md` §7 records it as NOT SELECTED.

**The deciding condition**, written down so the decision is made on requirements rather than
familiarity. The selection belongs to **WI-10-1.1.1 Logging foundation**, and a candidate must
satisfy all five of:

| # | Requirement | Why it decides |
|---|---|---|
| 1 | Preserves named structured properties to a queryable sink | A message template whose values collapse into a sentence cannot be queried by `CorrelationId` |
| 2 | Supports an ambient enricher or scope that attaches the correlation id without a parameter at every call site | Threading an id manually through every logger call is the failure mode this milestone exists to avoid |
| 3 | Supports a **destructuring/redaction hook** applied before a value reaches a sink | T-10-1.1.1.2 requires a redaction policy, and redaction applied at the call site is redaction that will be forgotten |
| 4 | Runs on `net10.0` under the pinning rules of `STACK_VERSION_POLICY.md` | The stack is .NET 10; a library trailing the runtime blocks the runtime |
| 5 | Emits to a sink that survives host restart and is readable without the host | A log only readable from the machine that produced it is not operational data |

Until a library is chosen, code takes `ILogger<T>` by constructor injection and uses message
templates with named placeholders; that code does not change when the library is chosen, which is why
the abstraction is used now rather than waiting. **Do not introduce a second logging path.**
`Console.WriteLine` is not logging, `console.log` is not telemetry, and `ConsoleAuditLog` is neither —
it implements `IAuditLog`, a durable governance record replaced at **M-01-4.1 Durable audit log**.

---

## 3. The log record

**TARGET — M-10-1.1.** Every log line carries these fields, whatever the library:

| Field | Source | Always present |
|---|---|---|
| `Timestamp` | UTC, `DateTimeOffset` — `CODE_CONVENTIONS.md` §18 | Yes |
| `Level` | §4 | Yes |
| `Message` | A template with named placeholders, never an interpolated string | Yes |
| `CorrelationId` | Generated at the edge or accepted from the caller | Yes, once M-10-1.1 lands |
| `TraceId` / `SpanId` | §5 | From M-10-2.2 |
| `Host` | Which process: Chat API, Intelligence API, a worker | Yes |
| `Layer` | The owning layer number — `01`…`12` | Yes |
| `TenantId` | Once tenancy exists — **M-01-2.1** | When a tenant is resolved |
| `PrincipalId` | An identifier, never a name or an email address | When a principal is resolved |
| `Operation` | What was being attempted, e.g. `CreateWorkspace`, `RunTurn` | Yes |
| `Outcome` | `Success`, `HandledFailure`, `Fault` | On completion lines |
| `DurationMs` | Elapsed milliseconds | On completion lines |

`Layer` matters more than it looks: with one platform database, eleven layer schemas and three hosts,
"which layer emitted this" is the first question asked of any unexpected line, and deriving it from
a namespace string later is guesswork.

### 3.1 Message templates

```csharp
logger.LogInformation("Workspace {WorkspaceId} created in tenant {TenantId}", id, tenantId);  // queryable
logger.LogInformation($"Workspace {id} created in tenant {tenantId}");                        // destroyed
```

The second form produces a line that can be read and never queried. Both cost the same to write.

---

## 4. Levels

`CODE_CONVENTIONS.md` §11 defines what each level *means*. This section defines what each level
*costs* and who is expected to act.

| Level | Who must act | Retention target | Alerting |
|---|---|---|---|
| `Critical` | On-call, immediately. Data loss, security failure, or the system is unusable | Longest | Always — **M-10-3.1** |
| `Error` | An owner, same working day. An operation failed and its caller could not proceed | Long | On rate threshold |
| `Warning` | Nobody immediately; the *rate* is the signal. A retry succeeded, a fallback fired, a quota was refused | Medium | On rate change only |
| `Information` | Nobody. This is the audit trail of normal behaviour: turn started, workspace created | Medium | Never |
| `Debug` | The developer running it. Never enabled in a deployed environment | Not retained | Never |

Two rules decide most level arguments. **A handled failure is a `Warning`, not an `Error`** — a
quota refusal (`QuotaVerdict`), a tool refusal (`ToolResult`) and a turn that could not proceed
(`TurnError`) are *values*, not faults (`CODE_CONVENTIONS.md` §6), and logging them at `Error`
teaches everyone to ignore `Error`. And **`Debug` is never enabled in a deployed environment** — not
"avoided", never: `Debug` is where developers put payloads, and payloads are where secrets and
prompt bodies hide.

**CURRENT:** with no logging library and no deployed environment, level configuration exists only in
`appsettings` defaults — `CONFIGURATION_STANDARDS.md` §3.

---

## 5. Correlation, trace and span

Three identifiers, three lifetimes. Conflating them is the most common instrumentation error.

| Identifier | Lifetime | Set by | Milestone |
|---|---|---|---|
| **CorrelationId** | One logical user request, across every host it touches | The edge, or the caller | M-10-1.1 |
| **TraceId** | One distributed trace, W3C `traceparent` | Tracing instrumentation | M-10-2.2 |
| **SpanId** | One operation in a trace — a pipeline step, a query, a model call | Tracing instrumentation | M-10-2.2 |

A correlation id can outlive a trace: a queued job resumed hours later (**M-05-1.1**) carries the
originating correlation id while starting a new trace. That is deliberate — it is how a background
failure joins back to the user action that caused it.

### 5.1 Propagation, end to end

**TARGET — M-10-1.1**, subtasks S-10-1.1.1.1.1 *correlation middleware in every host* and
S-10-1.1.1.1.2 *outbound HTTP propagates the header*.

```
ApiClient.ts ──X-Correlation-Id──▶ Chat API ──▶ Intelligence API (/intelligence/v1)
  └─▶ TurnPipeline: IntentClassifier → PolicyGate → ContextSelector → AgentSelector →
      ModelSelector → PromptStep → ModelStep → ToolLoop → ResponseComposer
        └─▶ IModelGateway ──▶ OpenAI
```

Every arrow forwards the identifier. The one forgotten is the last: an outbound HTTP client that does
not add the header breaks the chain at exactly the hop where cost is incurred. The header name, the
echo-on-response rule and the `correlationId` field in a Problem Details body are
`API_STANDARDS.md` §§7, 11 and are not restated here.

### 5.2 The frontend end of the chain

`ApiClient.ts` is the single HTTP path and so the single place the identifier is attached;
`ChatTelemetryContext.tsx` is the single telemetry seam and so the single place it reaches a client
event; `RouteErrorBoundary.tsx` shows it to the user at the last resort. See
`TYPESCRIPT_REACT_STANDARDS.md` §17 and `ERROR_HANDLING.md` §9.

---

## 6. Events

> **Scope.** This section owns **telemetry events** only — diagnostic, `snake_case`, past tense, no handlers, disposable. **Integration events** — PascalCase, handled, contract-bearing, e.g. `PipelineCompleted`, `JobEscalated`, `CertificateExpiring` — belong to [EVENT_ARCHITECTURE.md](EVENT_ARCHITECTURE.md). The two are different things that share a word; do not apply these rules to those.


An **event** is a named, structured fact about something that happened. It is not a sentence.

| Rule | Statement |
|---|---|
| Named for the fact, not the code; `snake_case`, past tense | `conversation_created`, `turn_completed`, `quota_refused` — never `handleSubmit_called` |
| Stable once emitted | A renamed event silently breaks every query and dashboard built on it |
| Carries identifiers, never content | `ConversationId`, not the message body |
| Emitted at a boundary | Not inside `ToolLoop`'s iteration — §7.3 |

An event is **not** an audit record. `AuditEntry` via `IAuditLog` is a durable governance record with
its own retention and access control (`SECURITY_STANDARDS.md` §8); an event is diagnostic and
disposable. Routing either through the other loses the properties that made it worth keeping.

---

## 7. Metrics

**TARGET — M-10-2.2 Metrics and distributed tracing.** Outcome: *latency, throughput and error rate
are visible per layer and per endpoint.* Acceptance: *a slow turn is attributable to a specific
pipeline step.* The roadmap names OpenTelemetry instrumentation in WI-10-2.2.1; **no OpenTelemetry
package exists in any repository today**, and adopting one follows `TECHNOLOGY_STACK.md` §8.

### 7.1 The minimum set

| Metric | Kind | Dimensions |
|---|---|---|
| `http.server.duration` | Histogram | host, route template, method, status class |
| `http.server.requests` | Counter | host, route template, status class |
| `db.command.duration` | Histogram | schema, operation |
| `turn.duration` | Histogram | pipeline step, outcome |
| `model.invocation.duration` | Histogram | provider, model |
| `model.tokens` | Counter | provider, model, direction (prompt/completion) |
| `job.queue.depth` | Gauge | queue name — **M-05-6.1** |

### 7.2 Dimension discipline

**A dimension value must be bounded.** A workspace id, a conversation id or a correlation id as a
metric dimension produces unbounded cardinality and turns a metric store into a very expensive log
store. Route **templates**, never concrete paths: `/api/v1/workspaces/{id}`, never
`/api/v1/workspaces/3f2a…`. High-cardinality context belongs on the log line or trace span.

### 7.3 Where instrumentation goes

At boundaries: the HTTP edge, the repository, the model gateway, the job dispatcher, each named
`TurnPipeline` step. **Not inside loops.** A per-item log line or metric increment inside `ToolLoop`
produces volume without information, and volume makes an incident harder to diagnose, not easier.

---

## 8. Health checks

**TARGET — M-10-2.1 Health checks.** Outcome: *every host reports its own readiness and the health
of its dependencies.* Acceptance: *a database outage surfaces as unhealthy within one check
interval.*

**CURRENT.** `Nexus.Products.Chat.Api/Endpoints/HealthEndpoint.cs` and
`Nexus.Intelligence.Api/Endpoints/Health` exist; the client consumes them through
`features/system/systemApi.ts`, `useSystemHealth.ts` and `types/SystemHealth.ts`. **Neither probes a
dependency** — they report that the process answers, which the request's arrival already proved.

### 8.1 The three checks

| Check | Question | Consequence of failure |
|---|---|---|
| **Liveness** | Is the process alive and not deadlocked? | Restart the process |
| **Readiness** | Can it serve traffic — dependencies reachable, migrations applied? | Remove from rotation; do not restart |
| **Startup** | Has initialisation finished? | Wait; do not judge liveness yet |

Confusing the first two produces the worst failure mode available: a database outage makes every host
report unhealthy, an orchestrator restarts them all, and the restarts hide the outage. **Liveness
never checks a dependency.**

### 8.2 Rules

| Rule | Statement |
|---|---|
| Readiness names each dependency and its own state | "Unhealthy" without a name is a second investigation |
| A health check is cheap and bounded | `SELECT 1` with a timeout, never a real query |
| Health carries no secret | Not a connection string, not a server name, not a version that aids an attacker |
| A public probe returns only healthy/unhealthy | Detail requires authorization once **M-01-3.1** exists |
| The check interval is configured | `CONFIGURATION_STANDARDS.md`; the acceptance criterion is stated in check intervals |

---

## 9. PII and sensitive data in telemetry

`SECURITY_STANDARDS.md` §11 is authoritative and its prohibitions are absolute: **no log record,
metric dimension, trace attribute or client telemetry event may contain a secret, a token, a full
prompt or completion body, a message body, a document body, or personal data beyond an identifier.**

What is logged instead: the correlation id, the tenant id, the principal id, the model name, token
counts, duration, and the outcome. That is enough to diagnose nearly anything; where it is not, the
conversation store holds the content under its own access control, and reaching for it is an
authorized act rather than a `grep`. Three consequences belong to this document:

| Consequence | Rule |
|---|---|
| Redaction is central, not local | Applied in the logging pipeline (§2 requirement 3), never remembered at each call site |
| Redaction is tested | S-10-1.1.1.2.1 — *a unit test asserting a known secret pattern is redacted*. Untested redaction has already failed somewhere |
| Classification gates retention | **TARGET — M-02-5.1.** Until it exists, telemetry retention is a manual decision and should stay short |

An AI turn is where the rule is under most pressure, because the prompt is exactly what an engineer
wants to see when an answer is wrong. §11 says what to record instead.

---

## 10. Error and performance telemetry

### 10.1 Errors

`ERROR_HANDLING.md` owns the classification and the error-class → status → level mapping. Every error
record carries the correlation id, the **error class** from that taxonomy (not a free-text
category), the operation attempted, a **fingerprint** so ten thousand instances read as one problem,
and the exception type and stack — **in the log only**, never in the response body
(`API_STANDARDS.md` §7).

Log the exception **once**, at the boundary that decides. An exception logged at every frame it
passes through produces one incident that looks like five.

### 10.2 Performance

**TARGET — M-10-4.2 Capacity and performance baselines.** A performance number without a baseline is
a number, not a signal.

| Rule | Statement |
|---|---|
| Measure at boundaries | Request, database command, model invocation, pipeline step |
| Percentiles, not averages | p50, p95, p99. An average hides the experience of every user who has a bad one |
| Attributable | *A slow turn is attributable to a specific pipeline step* — the M-10-2.2 criterion |
| Compared against a recorded baseline | M-10-4.2 |
| A timeout is a performance signal, not only an error | `CODE_CONVENTIONS.md` §13 |

---

## 11. AI telemetry

The AI layer is the most expensive and least deterministic part of Nexus, and the only part whose
failures are frequently *plausible*. It gets its own signal set.

**CURRENT.** `Nexus.Intelligence.Core/Turns/InMemoryTurnTraceStore` and
`Nexus.Intelligence.Api/ResultReports/InMemoryResultReportStore` hold turn state **in memory**; a
restart erases the reasoning behind every answer already given. **M-04-1.1 Durable turn trace and
result report** (P0) closes this — a trace must be retrievable by turn id *after a host restart*,
with the in-memory implementations *remaining only as test doubles*.

| Signal | Source | State |
|---|---|---|
| `DecisionTrace` — intent, policy verdict, selected context ids, agent, model | `Nexus.Intelligence.Contracts/Turns` | CURRENT, in memory |
| `UsageSummary` / `ModelUsage` — token counts | Contracts `Turns` / Platform `Models` | CURRENT, in memory |
| `UsageRecord` via `IUsageMeter` | `Nexus.Platform.Contracts/Governance` | `InMemoryUsageMeter` — durable at **M-01-4.2** |
| Per-turn cost | — | **TARGET — M-04-4.1 Per-turn cost attribution** |
| Citation coverage, evaluation scores | `Citation` in Contracts `Context` | **TARGET — M-04-2.1, M-04-5.1 / M-09-6.1** |

**What an AI telemetry record contains:** the correlation id, the turn id, the intent class, the
policy verdict, the **ids** of the selected `ContextItem`s, the agent, the model, prompt and
completion **token counts**, the tool calls attempted with their `SideEffectClass`, latency per step,
and the outcome.

**What it never contains:** the prompt body, the completion body, the content of any `ContextItem`,
or a citation's source text. The `ContextItem` ids are the join key — the content is retrievable
from `DATA` under `DATA`'s access control by someone entitled to it, which is the point of recording
the id rather than the text. Prompt **versioning** (M-04-2.2) is what eventually makes this
comfortable: a trace records the prompt *version*, which resolves to the template without copying
user content anywhere.

---

## 12. Build and deployment telemetry

### 12.1 Build

**CURRENT: no CI exists.** `NexusAI/.github/workflows/` is empty; Nexus.Web and Nexus.Int have no
`.github` directory at all. Every claim that something built is a person's assertion.

**TARGET — M-08-1.3 Machine-readable results.** Each pipeline run publishes a JSON artefact with
branch, commit, outcome and test counts, retrievable by branch name, with a **versioned schema** so
DEVELOPER's ingestion can evolve. **M-07-4.1 Build and test records** ingests it into `BuildRecord`
and `TestRun`. The join key is the branch name matched to the work item id — which is why branch
naming (`GIT_WORKFLOW.md` §4) is an observability concern rather than a cosmetic one, and why a
result whose branch matches no active assignment is **rejected** rather than stored loose
(S-07-4.1.1.1.2).

### 12.2 Deployment

**TARGET — M-08-5.1 Automated deployment**, observed by **M-10-2.3 Deployment health**: *a
deployment that degrades error rate is flagged against that deployment record.* That requires the
deployment to be an **identifiable event in the telemetry stream** — every log record, metric point
and trace from a deployed host carries the deployment identifier and the commit, so "when did this
start" is answered by comparison rather than by memory. Recording it is cheap; reconstructing when a
regression began is not.

---

## 13. Dashboards

A dashboard answers a question somebody actually asks; one built to display available data is a
screen nobody opens twice.

| Dashboard | Question | Depends on | Milestone |
|---|---|---|---|
| **System health** | Is every host up, with dependencies reachable? | Health checks | M-10-2.1 |
| **Request health** | Latency and error rate per endpoint and layer | Metrics | M-10-2.2 |
| **Turn health** | Where is time and cost going inside `TurnPipeline`? | AI telemetry | M-04-1.1, M-10-2.2 |
| **Cost** | What did AI usage cost, by tenant, product and model? | `UsageRecord`, per-turn cost | M-01-4.2, M-04-4.1, M-10-4.1 |
| **Deployment** | Did the last deployment change anything for the worse? | Deployment health | M-10-2.3 |
| **Development progress** | What is in flight, blocked, or waiting? | Work graph, build records | M-07-5.2, M-07-6.3 |
| **Assurance** | Which criteria are unverified, which gaps are open? | Verification runs | M-09-1.1, M-09-1.2 |

The last two are **DEVELOPER** and **ASSURANCE** surfaces on their own schemas. They are listed here
because operational and development dashboards must share one identifier vocabulary — work item id,
branch name, commit, deployment id, correlation id — or "which change caused this" requires a human
to join them by eye.

---

## 14. Retention

**Not yet decided.** Periods depend on **M-02-5.1 Classification and retention** and on obligations
**M-03-4.1 Compliance obligation catalogue** has not yet catalogued. What is decided now:

| Rule | Statement |
|---|---|
| Every signal has a retention period, set by classification | An unbounded log store is both a cost and a liability — M-02-5.1 |
| An `AuditEntry` outlives a log line | Different record, different retention — `SECURITY_STANDARDS.md` §8 |
| A turn trace has a bounded window | S-04-1.1.1.1.2 — *retention window so traces do not grow without bound* |
| Evidence is immutable and is not a log | `ASSURANCE_STANDARDS.md` §10 |

---

## 15. What to do today

Until M-10-1.1 lands, a developer writing code now should:

1. Inject `ILogger<T>`; never construct a logger, never `Console.WriteLine`.
2. Use message templates with named placeholders (§3.1), carrying the identifiers you already have —
   workspace id, conversation id, turn id — so they are queryable the day a sink exists.
3. Log **at boundaries**: endpoint entry and exit, repository call, model invocation. Not in loops.
4. **Never** log a secret, a token, a prompt or completion body, a message body, or personal data
   beyond an identifier.
5. Accept and forward an `X-Correlation-Id` header on any new outbound call, even before middleware
   generates one — forwarding a sometimes-absent header is trivial; retrofitting it is the expense
   this milestone exists to avoid.

---

## 16. Open decisions

| Question | What would decide it | Milestone |
|---|---|---|
| Logging library | The five requirements in §2 | M-10-1.1 |
| Log sink, metric store and query surface | First deployed environment | M-08-4.1, M-10-1.1 |
| Tracing implementation | The instrumentation work item names OpenTelemetry; nothing is installed | M-10-2.2 |
| Retention periods | Data classification and compliance obligations | M-02-5.1, M-03-4.1 |
| Alert routing and on-call | Whether anyone is on call | M-10-3.1 |

---

## 17. References

- `SECURITY_STANDARDS.md` §11 — what may never be written to a log. Absolute.
- `API_STANDARDS.md` §§7, 11, 12 — Problem Details, the correlation header contract, request logging.
- `ERROR_HANDLING.md` — error classification and the error-class → status → level → message table.
- `CODE_CONVENTIONS.md` §§6, 11, 13 and `CSHARP_STANDARDS.md` §11 — what to log, and `ILogger<T>`.
- `TYPESCRIPT_REACT_STANDARDS.md` §17 — `ChatTelemetryContext.tsx`, the only client telemetry seam.
- `ASSURANCE_STANDARDS.md` §10 — evidence, a different thing from a log line.
- `CONFIGURATION_STANDARDS.md` — where levels, intervals and endpoints are configured.
