# Event Architecture

**Status:** **CURRENT — there is no event bus in Nexus and nothing publishes an event.** Every
interaction today is a direct method call or an HTTP call. This document is the design that
`M-01-8.1` will implement in P2, and its first job is to stop that design being anticipated
**Owner:** CORE (Layer 01), which owns the event abstraction; each emitting layer owns its own event
contracts
**Last updated:** 2026-08-21
**Layer:** 01 CORE — binding on every layer that would emit or handle an event
**Authoritative for:** when a synchronous call is correct and when an event is, the in-process event
model, the future cross-service model, event naming, event contract ownership, versioning,
idempotency, retry, correlation, the audit-versus-event distinction, and failure handling.

Not authoritative for: telemetry events — `OBSERVABILITY_STANDARDS.md` §6; audit records —
`SECURITY_STANDARDS.md` §8; job and workflow retry mechanics — `AUTOMATION`'s milestones and
`ERROR_HANDLING.md` §7; HTTP idempotency keys — `API_STANDARDS.md` §10; which layer may call which —
`DEPENDENCY_RULES.md`.

---

## 1. Read this before anything else

> **CURRENT: Nexus has no event bus, no publisher, no subscriber, no outbox and no message broker.
> Not one line of event infrastructure exists. Every interaction in the running system is a direct
> method call resolved through dependency injection, or an HTTP call between two hosts.**

This document exists because that will change at **`M-01-8.1` In-process event bus (P2)**, and
because a design written after the first three events have been improvised is a design that
retrofits three improvisations.

**It must not become a licence to publish.** A document describing an event bus is the most common way
an event bus gets built by accident: someone reads it, writes `IEventPublisher`, and now there are two
event mechanisms — the real one and the one that shipped early. Until `M-01-8.1` completes:

| Rule while there is no bus | |
|---|---|
| Do not write an `IEventPublisher`, `IDomainEvent`, `IEventHandler` or an outbox table | The abstraction lands once, in CORE, at `M-01-8.1` |
| Do not add a "temporary" in-process mediator | It is not temporary. It is the bus, built in the wrong layer |
| Do not design a feature whose correctness requires an event | It cannot be built. Use a direct call and record the coupling as a known cost |
| **Do** record where you wanted one | That list is what sizes `M-01-8.1` honestly |

**The concrete trigger that justifies building it.** `M-01-8.1`'s acceptance criterion is a single
sentence and it is the whole business case:

> *A DELIVERY pipeline-completed event reaches DEVELOPER without a project reference between them.*

DEVELOPER must learn that a build finished. DELIVERY must not know DEVELOPER exists — a pipeline
knows repositories and artifacts and does not know what a milestone is (`DEPENDENCY_RULES.md` §6).
DEVELOPER may reference DELIVERY, so DEVELOPER *polling* is legal and is the CURRENT answer
(§5.3). The bus becomes justified when polling stops being adequate — when `M-07-3.2` autonomous
dispatch makes latency between build completion and the next dispatch decision the thing that limits
throughput. **Until that pressure is real, the bus is deferred, and deferring it is a decision rather
than an omission.**

---

## 2. Three different things are called "event"

They have different retention, different access control and different audiences. Routing one through
another destroys the properties that made it worth keeping.

| | **Telemetry event** | **Audit entry** | **Integration event** |
|---|---|---|---|
| Question it answers | What was the system doing? | Who did what, and when? | What happened that another layer must react to? |
| Owner | OPERATIONS (10) | CORE (01), `IAuditLog` | The **emitting** layer |
| Authority | `OBSERVABILITY_STANDARDS.md` §6 | `SECURITY_STANDARDS.md` §8 | **This document** |
| Form | `snake_case`, past tense — `turn_completed` | `AuditEntry` row | PascalCase C# type — `PipelineCompleted` |
| Durability | Diagnostic and disposable | **Append-only, immutable**, retained | Delivered at least once, then done |
| Has handlers | No | No | **Yes — that is the point** |
| Exists today | No — `M-10-1.1` | Partly — `ConsoleAuditLog` writes to the console and the entry is lost | **No — `M-01-8.1`** |

**The failure to avoid:** an integration event used as an audit record. An event is delivered and
discarded; an audit entry is a governance record with retention and separate write access. A system
that reconstructs "who approved this" by replaying events has an audit log with an asterisk.

The reverse failure is quieter: writing an audit entry and expecting something to react to it.
`IAuditLog` has no subscribers and never will.

---

## 3. Synchronous call or event

The decision is not about taste. It is about whether the caller needs the answer and whether the
direction is legal.

| Choose | When |
|---|---|
| **Direct call (DI)** | Both types are in the same process, the dependency direction is downward, and the caller needs the result to continue. This is the default and covers most of Nexus |
| **HTTP call** | The dependency direction is downward but the callee is a different host. Two exist today: Nexus.Experience → `/intelligence/v1` |
| **Event** | The emitter must not know the consumer exists, **and** the emitter does not need the outcome. Both halves, not either |
| **Job (AUTOMATION 05)** | The work is durable, retryable and may take longer than a request. `M-05-1.1` — a job survives a restart; an in-process event does not |
| **Poll** | The direction is legal but the emitter would have to know the consumer. The CURRENT answer for DELIVERY → DEVELOPER |

**The single question that settles most cases:** *does the emitter need to know what happened next?*
If yes, it is a call — an event whose result the emitter waits for is a call with extra machinery and
worse error handling. If no, and the reference direction would be illegal, it is an event.

**What an event must never be used for:**

| Never | Why |
|---|---|
| To get a value back | An event has no return. If you need the answer, call |
| To evade the dependency matrix | An event between two layers that must not know each other is legitimate. An event used to dodge a rule you disagree with is the same violation with indirection |
| To make an operation transactional across layers | It does not. See §8 |
| As the only record that something happened | The fact belongs in a row in the owning layer. `DEPENDENCY_RULES.md` Rule 7 |

---

## 4. CURRENT — what actually connects things today

Three mechanisms, and that is all of them.

**4.1 Dependency injection inside a host.** `IntelligenceServiceCollectionExtensions` in
`Nexus.Intelligence.Api/DependencyInjection/` composes the turn pipeline; `ChatProductModule.cs`
composes the Chat product. Every step in `TurnPipeline` — `IntentClassifier`, `PolicyGate`,
`ContextSelector`, `AgentSelector`, `ModelSelector`, `PromptStep`, `ModelStep`, `ToolLoop`,
`ResponseComposer` — is invoked directly, in order, synchronously.

**4.2 HTTP between hosts.** `Nexus.Experience` calls Intelligence at `/intelligence/v1` through
`IIntelligenceClient`. `Nexus.Experience.Client` calls the Chat API at `/api/v1` through `ApiClient.ts`.
Both are request/response; neither is fire-and-forget.

**4.3 Polling, where an event is what you actually want.** DEVELOPER does not exist yet, so no polling
loop exists either. When it does, `M-08-1.3` gives it the shape it needs: each pipeline run publishes
a versioned JSON artifact **retrievable by branch name**. DEVELOPER reads it. That is a downward
reference the matrix allows, it needs no infrastructure, and it is the right answer until dispatch
latency makes it wrong.

**What this means for a design written today:** if a feature only works with a bus, it is not a
feature that can be built now. Say so in the work item rather than building half a bus.

---

## 5. In-process events — `M-01-8.1`, P2

**FUTURE.** The first real event mechanism. Its shape follows from what it must and must not do.

| Property | Statement |
|---|---|
| Scope | **One process.** A handler runs in the same host as the publisher |
| Owner | CORE (01) owns the abstraction. Every layer may publish and handle |
| Delivery | Synchronous to handlers by default, after the publisher's transaction commits |
| Ordering | Per publish, in registration order. **No cross-publish ordering guarantee** |
| Durability | **None.** A process that dies between commit and handler loses the event |
| Failure | A failing handler does not fail the publisher and does not stop sibling handlers (§9) |
| Reference cost | Publisher and handler both reference the **event contract**, never each other |

**The durability gap is the design's most important property, because it bounds what may use it.** An
in-process event is a notification, not a guarantee. Anything that must not be lost — a deployment,
a payment, an escalation — goes through AUTOMATION's job store (`M-05-1.1`), which survives a host
restart with the job still `Pending` and claimable. Choosing between them is choosing whether losing
the message is acceptable.

**The publish point.** Events are published **after commit**, never inside the transaction. Publishing
inside a transaction that then rolls back tells the rest of the system something happened that did
not, and no compensation makes that clean.

---

## 6. Cross-service events — FUTURE, no milestone

**There is no milestone for a cross-process message bus, and adding one is not a documentation
decision.** `M-01-8.1` is explicitly in-process; `M-01-8.2` Notification transport (P3) is about
reaching a *person* — *an approval request in DEVELOPER reaches the reviewer without DEVELOPER
knowing the transport* — not about services messaging each other.

What would justify the step, stated so that the decision is checkable rather than atmospheric:

| Trigger | Why it is decisive |
|---|---|
| Two hosts must react to one fact, and neither may reference the other | In-process delivery cannot cross the host boundary at all |
| Loss of a notification has a business consequence | In-process events have no durability. A broker or an outbox is the only answer |
| A consumer must be able to be down and catch up | Requires a durable log, which no in-process mechanism provides |

**Prerequisites that are not yet met.** No message broker technology is selected. No deployment
environment exists — `M-08-4.1` Environment model is P2 and nothing is deployed anywhere. A
cross-service bus needs somewhere to run, something to monitor it (`M-10-2.2`), and an operational
owner. Until those exist, the in-process bus plus AUTOMATION's durable job store covers every case
Nexus actually has.

**When it is built, one thing must already be true:** every event contract must already be versioned
and idempotently handled (§7, §8). Retrofitting either across a live broker is expensive; adding a
transport under contracts that already have both is not.

---

## 7. Naming, ownership and versioning

### 7.1 Naming

| Rule | |
|---|---|
| **Past tense, always** | The event states a fact that has already happened. `PipelineCompleted`, not `CompletePipeline` |
| PascalCase C# type | `JobEscalated`, `CertificateExpiring`, `ProductLifecycleChanged` — all three are named in the roadmap's acceptance criteria |
| Named for the fact, not the mechanism | `WorkItemIntegrated`, never `IntegrationHandlerFinished` |
| No consumer in the name | `PipelineCompletedForDeveloper` is the coupling the event exists to remove |
| Carries identifiers, never content | `ConversationId`, never the message body. `SECURITY_STANDARDS.md` §11 forbids content in a log line and the same reasoning applies |

`CertificateExpiring` is the one legitimate present-participle form in the set: it reports a threshold
crossing rather than a completed action, and `M-03-3.3` requires it be emitted **once** per
certificate crossing the warning threshold.

### 7.2 Ownership — the emitting layer owns the contract

> **The layer that publishes an event owns its type, its schema and its compatibility. A consumer
> never owns an event it consumes, and never asks for a field for its own convenience.**

`PipelineCompleted` is DELIVERY's type, in `Nexus.Delivery.Contracts` (TARGET). DEVELOPER references
that contracts assembly — a downward reference the matrix allows — and DELIVERY references nothing of
DEVELOPER's. That asymmetry is the entire point: **the emitter must not know its consumers exist**,
which is also why an emitter may not be asked to add a field "because DEVELOPER needs it". If a
consumer needs more, it reads the owning layer's API with the identifier the event carried.

The corollary: an event contract in a Contracts assembly must not name a product type. Rule 3, no
shared kernel, applies to events exactly as to everything else.

### 7.3 Versioning

| Change | Allowed | How |
|---|---|---|
| Add an optional field | **Yes** | Handlers ignore what they do not read |
| Add a required field | No | It breaks every existing handler. Publish a new event type |
| Remove or rename a field | No | Same reason |
| Change a field's meaning | **The worst case** — it breaks handlers silently, with no compile error and no failure | Publish a new event type |
| Retire an event | Yes | Publish both for one release, migrate handlers, then stop |

**Version by type, not by a version field.** `PipelineCompletedV2` is explicit, compiles or does not,
and lets both live during a migration. A `Version` property inside one type means every handler
branches on it, and a handler that forgot to is wrong at runtime rather than at build time.

DELIVERY's result artifact already sets the precedent: `M-08-1.3` requires *the schema is versioned so
DEVELOPER's ingestion can evolve*. An event contract carries the same obligation.

---

## 8. Idempotency

> **Assume every handler will see the same event more than once, and design so that the second time
> changes nothing.**

This is not defensive pessimism. It is what `M-05-3.2` requires outright: *redelivery of the same
event id does not start a second instance*. Redelivery arrives from retry (§9), from a future
at-least-once transport (§6), and from a replayed dead-letter job (`M-05-1.3`).

| Mechanism | Use |
|---|---|
| **Event id** | Every event carries a stable id assigned at publish. A handler records ids it has processed |
| **Natural idempotence** | Preferred where available: setting a state to `Completed` twice is one outcome; incrementing a counter twice is two |
| **Conditional write** | Write only if the current state permits it. A second `approve` on an approved gate is a conflict, not a second approval — `M-05-5.1` |
| **Idempotency key** | For work an event enqueues. `M-05-1.1`: the same key twice yields one job row |

**Events are not transactions.** Publishing after commit (§5) means the publisher's write has already
succeeded when a handler fails. There is no rollback across that boundary and pretending otherwise is
how partial state is created. Where several layers' writes must all succeed or all fail, they belong
in one transaction and therefore in one layer — or the operation needs an explicit compensating
action, which is AUTOMATION's `M-05-6.2` and not an event concern.

---

## 9. Retries, failure and dead-lettering

**A failing handler must not fail the publisher.** The publisher's work has already committed; making
it appear to fail because a downstream reaction failed reports a lie to the caller.

| Situation | Behaviour |
|---|---|
| One handler throws | Log with correlation id, continue to sibling handlers, do not fail the publish |
| The failure is transient and the handler is idempotent | Retry — bounded, exponential backoff with jitter. `ERROR_HANDLING.md` §7 owns the classification |
| The failure is permanent | Do not retry. A `4xx`-class failure will stay wrong |
| Retries are exhausted | Dead-letter it. `M-05-1.3`: exactly one `DeadLetterEntry` retaining the original payload and last error, replayable as a new job linked back to the original id |
| The event matches no handler at all | **Record it as unhandled and do not throw** — `M-05-3.2`. An unhandled event is a configuration finding, not a fault |

**Never nest retries.** Retry at one layer. Three layers of three attempts is twenty-seven calls and
an outage amplifier, and each layer's log shows a reasonable-looking three.

**An in-process handler that needs durable retry does not belong in a handler.** It should enqueue a
job and return. The event notified; the job is what guarantees.

---

## 10. Correlation — `M-10-1.1`

> **Every event carries the correlation id of the request that caused it, and every handler logs
> under that same id.**

Without it an event is the point where a traceable request becomes two unrelated log streams. With
it, `M-10-1.1`'s acceptance criterion holds across the asynchronous boundary too: *one request is
retrievable end to end by that id alone.*

| Field | Source |
|---|---|
| `CorrelationId` | The `X-Correlation-Id` of the causing request — generated at the edge or accepted from the caller |
| `EventId` | Assigned at publish; the idempotency key of §8 |
| `OccurredAt` | UTC, set by the publisher, never by a handler |
| `TenantId` | Resolved from the acting identity. A handler runs in the tenant of the event, never a wider one |
| `ActorRef` | The principal whose action caused it, human or agent |

**CURRENT: no correlation id exists on any host.** A request cannot presently be followed from the
frontend through the Chat API into Intelligence and out to OpenAI. `M-10-1.1` is P1, ahead of the
event bus at P2, and that ordering is deliberate — an event system without correlation is a system
whose failures cannot be reconstructed.

**Tenant propagation is a security property, not a convenience.** A handler that resolves its tenant
from ambient state rather than from the event is one scheduling change away from processing tenant
A's event in tenant B's context. `SECURITY_ARCHITECTURE.md` §6 owns the boundary.

---

## 11. Audit, and what an event is not

Some events must also be audited. **That is two writes, not one mechanism.**

| Event | Also audited? | Authority |
|---|---|---|
| `PipelineCompleted` | No — a build result is DELIVERY's record | `M-08-1.3` |
| Any tool invocation with side effects | **Yes**, always | `SECURITY_STANDARDS.md` §10 |
| A permission or role change | **Yes** | §8.3 there |
| A cross-tenant operation | **Yes**, every time | §4.4 there |
| An approval decision | **Yes** — actor, decision, gate | `M-05-5.1` |
| `JobEscalated` | The escalation is operational; the *decision* it leads to is audited | `M-05-1.3` |

The audit write happens where the action happens, synchronously, through `IAuditLog`. It does not
happen in a handler, because a handler that fails would then lose the audit record — and an audit
record whose existence depends on a best-effort notification is not an audit record.

**CURRENT.** `IAuditLog` and `AuditEntry` exist in `Nexus.Platform.Contracts/Governance/`; the only
implementation is `ConsoleAuditLog`, which writes to the console where the entry is then lost.
`M-01-4.1` makes it durable.

---

## 12. What must never happen

| Never | Why |
|---|---|
| Build an event bus before `M-01-8.1` | Two mechanisms, one of them unowned |
| Publish inside a transaction | It announces something that may not have happened |
| Wait on a handler's result | That is a call. Write the call |
| Name a consumer in an event | The coupling the event exists to remove |
| Put content in an event | Identifiers only. Content lives in its owning store with its own access control |
| Use an event as the record of a fact | The fact is a row in the owning layer. Rule 7 |
| Let a handler widen its tenant | Every handler runs in the event's tenant |
| Retry a non-idempotent handler | Duplicates, created deliberately |
| Route an audit record through the bus | Different retention, different access control, different audience |
| Add a required field to a shipped event | A silent break in every existing handler |

---

## 13. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Whether the in-process bus is hand-written or a library | `M-01-8.1`. No library is selected and none may be assumed | **Not yet decided** |
| Whether an outbox table backs the in-process bus | Whether any GATE B consumer cannot tolerate loss. If one can't, it should be a job instead | Not yet decided |
| Whether a cross-service transport is ever built | §6's three triggers, none of which is met | Deliberately deferred — **no milestone** |
| Which broker, if one is ever needed | Nothing is deployed; `M-08-4.1` comes first | Not yet decided — NOT SELECTED |
| Whether event contracts live per layer or in one assembly | Per layer, by Rule 3 reasoning — but no layer has published one yet | Provisional; confirm at `M-01-8.1` |
| Whether DEVELOPER polls DELIVERY permanently or migrates to events | Dispatch latency measured after `M-07-3.2` | Not yet decided — polling is correct until measured otherwise |

---

## 14. References

- `OBSERVABILITY_STANDARDS.md` §6 — telemetry events, which are a different thing with the same name.
- `SECURITY_STANDARDS.md` §8 — the audit log, its properties and what must be audited.
- `ERROR_HANDLING.md` §7 — transient versus permanent, and the never-nest-retries rule.
- `API_STANDARDS.md` §10, §11 — idempotency keys and correlation identifiers over HTTP.
- `INTEGRATION_ARCHITECTURE.md` — the synchronous half of the same subject, and today's real
  integration points.
- `DEPENDENCY_RULES.md` §8 — "emit an event" as one of the four legitimate ways to go upward.
- `BOUNDED_CONTEXTS.md` — which context would own each event contract.
- `../nexus-roadmap.yaml` — `M-01-8.1`, `M-01-8.2`, `M-05-1.3`, `M-05-3.2`, `M-08-1.3`, `M-10-1.1`.
