# Operations Architecture

**Status:** TARGET — **nothing is deployed anywhere, so there is nothing to operate.** No
`Nexus.Operations.*` project exists, no `operations` schema exists, and no logging library has been
selected. Each gap names the milestone that closes it
**Owner:** Durai
**Last updated:** 2026-08-21
**Layer:** 10 OPERATIONS — repository `Nexus.Platform`, schema `operations`, cross-cutting
**Authoritative for:** the shape and boundaries of the OPERATIONS layer — what runtime ownership
means, the entity model, the boundary with DELIVERY and ASSURANCE, the observability progression from
correlation to metrics to tracing, health, incidents and alerts, performance, cost and capacity,
backup monitoring and recovery drills, security monitoring, and what "operational state" is allowed
to be.

**Not authoritative for:** the shape of a log record, log levels and their operational consequence,
correlation identifier format, the event taxonomy, metric naming, health check response shape, PII
rules in telemetry, or retention — all `OBSERVABILITY_STANDARDS.md`. Deployment, promotion,
provisioning, backup mechanism and restore — `DELIVERY_ARCHITECTURE.md`. Whether a requirement was
satisfied — `ASSURANCE_ARCHITECTURE.md`. Incident-derived work — `DEVELOPER_ARCHITECTURE.md`.

---

## 1. Purpose

OPERATIONS keeps running Nexus systems and products healthy, secure, observable and recoverable.

The layer has one governing property that separates it from every other layer: **it is the only layer
whose subject is the present tense.** DEVELOPER records what was built. DELIVERY records what shipped.
ASSURANCE records what was proven. All three are durable claims about the past. OPERATIONS answers
"is it working *now*", and an answer to that question is worthless the moment it is stale.

That has an architectural consequence. Operations data is time-series, high-volume and
low-individual-value — one log line matters far less than the ability to query ten million of them by
one identifier. It is the most likely candidate of any layer to leave the shared `NexusPlatform`
database for a purpose-built store, and the design should not assume it stays.

---

## 2. The boundary with DELIVERY and ASSURANCE

These three layers are all cross-cutting, all concerned with things going right, and are routinely
conflated. The distinction is precise:

> **DELIVERY ships it. ASSURANCE proved it satisfied the requirement. OPERATIONS proves it stays
> healthy in production.**

| | DELIVERY | ASSURANCE | OPERATIONS |
|---|---|---|---|
| Question | Did it build, and did it reach an environment | Was the requirement satisfied | Is it healthy right now |
| Tense | Past — a shipment | Past — a verdict | **Present — a condition** |
| Subject | An artifact, a deployment | A criterion, a piece of evidence | A running process, a dependency, a request |
| Record | `PipelineRun`, `Deployment` | `VerificationRun`, `Evidence` | `HealthCheck`, `Metric`, `Trace` |
| Fails as | The build is red; the deploy rolled back | A criterion is unverified | Error rate rose; a dependency is unreachable |
| Owns nothing about | What a build *means* | Runtime behaviour | What was built, or why, or how it shipped |

Three worked separations:

**A test passes, and the endpoint is slow in production.** ASSURANCE is satisfied — the criterion said
what it said and the evidence supports it. OPERATIONS is not — latency is a runtime property that no
pre-production verification observed. Neither layer is wrong; the criterion did not cover latency,
which is a traceability gap in ASSURANCE, and the slowness is an OPERATIONS signal.

**A deployment lands and the error rate rises.** DELIVERY's record says the deployment succeeded, and
it did — the artifact reached the environment. OPERATIONS attributes the degradation to that
deployment record (`M-10-2.3`). The `Deployment` row is DELIVERY's; the health signal correlated
against it is OPERATIONS'.

**A backup fails at 03:00.** The `BackupRecord` and the mechanism are DELIVERY's (`M-08-7.1`).
**Noticing** is OPERATIONS' — a backup whose failure is discovered at restore time was not a backup.
§8 covers this seam.

The rule that resolves any remaining case: OPERATIONS **owns nothing durable about what was built or
why, and nothing about how it shipped.** If a fact would still be interesting after the process
producing it has been retired, it belongs to another layer.

---

## 3. The entity model

Ten entities in four groups. `DATA_OWNERSHIP.md` §4 holds the canonical list.

| Group | Entities | Milestone |
|---|---|---|
| Telemetry | `LogStream`, `Metric`, `Trace` | `M-10-1.1`, `M-10-2.2` |
| Health | `HealthCheck`, `Incident`, `Alert` | `M-10-2.1`, `M-10-3.1`, `M-10-3.2` |
| Efficiency | `PerformanceRecord`, `CapacityRecord`, `CostRecord` | `M-10-4.1`, `M-10-4.2` |
| Runtime control | `FeatureFlagState` | `M-10-5.1` |

One name appears twice across layers and the split is deliberate. `FeatureFlag` (06 PRODUCT CORE) is
the **definition** — this product has this flag. `FeatureFlagState` (10 OPERATIONS) is the **runtime
value** in a given environment. Same word, two facts, two layers. A flag that exists but has no state
in an environment is a product capability nobody has enabled; a state with no definition is orphan
runtime data.

---

## 4. Correlation first — `M-10-1.1`, the only GATE A contribution

OPERATIONS contributes exactly one thing to GATE A: **structured logging with correlation IDs.**
Nothing else — no metrics, no tracing, no health checks, no alerts.

The reason is narrow and worth stating precisely, because it is the only argument for putting any
operations work in front of a gate whose subject is development readiness:

> **Correlation is disproportionately expensive to retrofit.**

Adding a metric later costs one instrumented call site. Adding correlation later costs every call
site, in every host, in every outbound request, plus a migration of every log sink that was written
without the field. And the cost is not linear in the number of hosts — it is linear in the number of
*boundaries between* hosts, which is the thing that multiplies as the layers arrive. A request that
crosses Experience, Developer and Intelligence has three boundaries today and will have more; each
one added before correlation exists is a boundary someone has to go back and thread an identifier
through.

`M-10-1.1` requires four properties:

| Property | Why it is stated as an acceptance criterion |
|---|---|
| A correlation id is **generated at the edge or accepted from the caller** | Generating unconditionally discards the caller's id and breaks the chain one hop up |
| It **propagates** through the Experience API, the Intelligence turn and the model invocation | The three-hop path is the one that exists today |
| One request is **retrievable end to end by that id alone** | The test of the whole milestone — no join across three log formats |
| **No log line contains a secret, a token or a full prompt body** | Redaction applied at the call site is redaction that will be forgotten |

The last one is why redaction is a **policy applied before a value reaches a sink**, not a discipline
at the call site — and why it is a hard requirement on whichever library is selected. A unit test
asserts that a known secret pattern is redacted.

### 4.1 No logging library has been selected

**No logging library has been selected.** Serilog is not present. Nothing beyond the
`Microsoft.Extensions.Logging` abstractions exists in NexusAI, Nexus.Int or Nexus.Web, and
`TECHNOLOGY_STACK.md` records it as NOT SELECTED. The selection belongs to work item
`WI-10-1.1.1 Logging foundation`, and the five requirements a candidate must satisfy are
`OBSERVABILITY_STANDARDS.md` §2 — including the two that are architectural rather than ergonomic: an
**ambient enricher** that attaches the correlation id without a parameter at every call site, and a
**redaction hook** applied before the sink.

Until then, code takes `ILogger<T>` by constructor injection and uses message templates with named
placeholders. That code does not change when the library is chosen, which is why the abstraction is
used now rather than waiting. **Do not introduce a second logging path.** `Console.WriteLine` is not
logging, `console.log` is not telemetry, and `ConsoleAuditLog` is neither — it implements `IAuditLog`,
a durable governance record replaced at `M-01-4.1`.

---

## 5. Metrics, tracing and health — P2 / GATE B

Everything else observability-related arrives after GATE A, and the ordering is dependency-driven:
there is no point measuring latency in an environment that does not exist.

| Milestone | Delivers | Depends on |
|---|---|---|
| `M-10-2.1` Health checks | Every host reports its own readiness and the health of its dependencies. A database outage surfaces as unhealthy within one check interval | `M-08-5.1` — something must be deployed |
| `M-10-2.2` Metrics and distributed tracing | Latency, throughput and error rate per layer and per endpoint. **A slow turn is attributable to a specific pipeline step** | `M-10-1.1`, `M-10-2.1` |
| `M-10-2.3` Deployment health | A deployment that degrades error rate is flagged **against that deployment record** | `M-10-2.2`, `M-08-5.1` |

`M-10-2.2` is one of the seven milestones that close GATE B. Its acceptance criterion is the most
demanding in the layer, because attributing a slow turn to a specific step of the AI turn pipeline
requires that every step is a span and that spans carry the correlation established at `M-10-1.1`.
That is the whole argument for §4 restated as a consequence.

`M-10-2.3` is the deliberate join between DELIVERY and OPERATIONS. DELIVERY records that the
deployment happened; OPERATIONS records what happened to health afterwards; the correlation between
them is what turns "the system got worse this week" into "the system got worse at 14:07 when
`DEP-00000042` landed".

### CURRENT reality

A `HealthEndpoint.cs` exists in the Chat API and a `useSystemHealth.ts` hook consumes it in the
frontend. That is a liveness endpoint on a laptop, not a health model: it has no dependency probes,
no check interval, nothing consuming it outside the browser, and nothing to compare it against.
`OBSERVABILITY_STANDARDS.md` §8 owns the shape it grows into.

---

## 6. Incidents and alerts — P3

| Milestone | Delivers |
|---|---|
| `M-10-3.1` Alerting | Threshold and anomaly conditions notify a responsible person. An `Alert` **names the affected product, environment and probable layer** |
| `M-10-3.2` Incident lifecycle | An `Incident` has an owner, a timeline and a recorded resolution |

The distinction between the two is the whole design. An **alert is a signal**; it fires, it is
delivered, and it may be ignored. An **incident is owned work**; it has a person, a timeline, and an
outcome that is recorded whether or not anyone remembers to write it down. Turning a signal into owned
work is the point of `M-10-3.2` — without it, an alert is a notification someone may have seen.

The alert content requirement is unusually specific for a reason. An alert saying "error rate is
elevated" produces a search; an alert naming product, environment and probable layer produces an
action. The probable layer is a guess, and it is worth making, because a wrong guess still narrows
the space.

`M-10-3.2`'s acceptance criterion crosses into DEVELOPER: **an incident can produce a work item
without retyping its context.** The incident stays OPERATIONS'; the work stays DEVELOPER's; the
handoff is mechanical. It is the same shape as the ASSURANCE `Defect` → `WorkItem` handoff
(`M-09-2.1`), and at P5 it becomes an input to DEVELOPER proposing its own work (`M-07-9.1`).

---

## 7. Performance, cost and capacity — P3

| Milestone | Delivers |
|---|---|
| `M-10-4.1` Cost monitoring | `CostRecord`. Spend attributable to product, environment, layer and model. **A cost anomaly raises an alert before the billing period closes** |
| `M-10-4.2` Capacity and performance baselines | `CapacityRecord`, `PerformanceRecord`. A projected capacity breach is reported with lead time |

Cost is an operational signal in Nexus in a way it is not in most systems, because a substantial share
of running cost is **model spend, which varies per request rather than per deployment**. An
infrastructure bill is roughly predictable from what is provisioned; an AI bill is a function of what
people asked and which model answered. That is why `M-10-4.1` depends on `M-04-4.1` per-turn cost
attribution: without attribution at the turn, the layer can report a total and nothing else.

"Before the billing period closes" is the criterion that makes it operational rather than financial.
A cost report at month end is accounting. A cost anomaly at hour three is an operations signal, and it
is the only form that can prevent the overspend rather than describe it.

`M-10-4.2` trends growth against capacity rather than discovering the breach. **With lead time** is
the operative phrase — a capacity report that arrives when capacity is exhausted has reported an
incident, not a projection.

---

## 8. Backup monitoring and recovery

The split with DELIVERY is clean and worth stating explicitly, because "backup" reads like one
concern and is two:

| Concern | Layer | Milestone |
|---|---|---|
| Taking the backup; `BackupRecord`, `RestorePoint` | 08 DELIVERY | `M-08-7.1` |
| **Noticing that it failed** | 10 OPERATIONS | `M-10-3.1` alerting over backup failure |
| Performing a restore | 08 DELIVERY | `M-08-7.2` |
| **Rehearsing recovery on a schedule and measuring it** | 10 OPERATIONS | `M-10-7.1`, P4 |

`M-08-7.1`'s own criterion is that backup success **and failure** are both recorded and alertable —
recording is DELIVERY's, alerting is OPERATIONS'. The pairing exists because the 2026-08-20 loss was
survivable only because GitHub held the history; once production data exists there is no equivalent
third party, and a silently failing backup is indistinguishable from no backup until the day it
matters.

`M-10-7.1` makes recovery a **practised capability rather than a document**. A drill result records
actual recovery time against the stated objective — and per `DELIVERY_ARCHITECTURE.md` §14, those
objectives are not yet stated, which is a prerequisite this milestone inherits.

---

## 9. Feature flags and security monitoring

**`M-10-5.1` Runtime feature flags**, P3. Behaviour can be enabled per environment, tenant or member
without redeploying, and a flag change takes effect without a restart and is audited. This decouples
*deploying code* from *releasing behaviour*, which is what lets a risky change ship dark. The
definition lives in PRODUCT CORE; only the runtime value lives here.

**`M-10-6.1` Security signal detection**, P4. Authentication anomalies and privilege escalation
attempts raise alerts — specifically, repeated cross-tenant access attempts raise an alert naming the
actor.

The P4 placement is honest rather than casual: detecting misuse becomes meaningful only once there are
real users and real data. What is *not* deferred is the control itself — tenant isolation is enforced
at `M-01-2.1` in P1, with a cross-tenant denial test written before the implementation. **Monitoring
is not a substitute for a control.** This milestone detects attempts against a boundary that already
holds; it does not create the boundary. `SECURITY_STANDARDS.md` owns the controls.

---

## 10. Operational state — and what it may not become

OPERATIONS holds derived state: `OperationalHealth` is derived from health checks and is never
entered by hand, in the same way DEVELOPER's `Blocked` is derived from dependencies.
`DATA_OWNERSHIP.md` §5 holds the full derived-versus-owned table.

The constraint on this layer specifically:

> **Nothing durable about what was built, or why, or how it shipped, may be stored here.**

The temptation is real and arrives in a predictable form: an incident wants to record which release
caused it, so someone adds a release field; a health record wants to know which commit is running, so
someone adds a commit field; and gradually the operations schema becomes a second, staler copy of
DELIVERY's deployment history. The rule is that OPERATIONS **references** a `Deployment` and reads
what it needs — `M-10-2.3` is exactly that reference, not a copy.

The test: if a fact is still interesting a year after the process producing it was retired, it is not
operations data.

---

## 11. GATE A, GATE B and later

| Capability | Milestone | Phase / Gate |
|---|---|---|
| **Structured logging with correlation** | `M-10-1.1` | P1 / **GATE A — the only one** |
| Health checks | `M-10-2.1` | P2 / GATE B |
| Metrics and distributed tracing | `M-10-2.2` | P2 / **GATE B closer** |
| Deployment health | `M-10-2.3` | P2 / GATE B |
| Alerting | `M-10-3.1` | P3 |
| Incident lifecycle | `M-10-3.2` | P3 |
| Cost monitoring | `M-10-4.1` | P3 |
| Capacity and performance baselines | `M-10-4.2` | P3 |
| Runtime feature flags | `M-10-5.1` | P3 |
| Security signal detection | `M-10-6.1` | P4 |
| Recovery drills | `M-10-7.1` | P4 |

GATE A's own summary states it as *"OPERATIONS beyond structured logging"* is explicitly not required,
and that must not be softened. The gate exists to get real business systems started at the earliest
safe point; a system that is not deployed cannot be observed, so observability beyond the one
retrofit-expensive property is by definition not on the critical path.

The GATE B rule applies here in full: this work runs in parallel with business development and must
never pause or block it. A business system waiting for tracing is a scheduling error.

---

## 12. Boundaries with the sibling layers

| Layer | The seam |
|---|---|
| 08 DELIVERY | DELIVERY ships and backs up; OPERATIONS observes and alerts. `M-10-2.3` correlates health to a `Deployment` |
| 09 ASSURANCE | ASSURANCE proved the requirement was satisfied before release; OPERATIONS proves the system stays healthy after |
| 07 DEVELOPER | An `Incident` produces a `WorkItem` without retyping context (`M-10-3.2`); at P5 incidents feed `M-07-9.1` |
| 04 AI | Per-turn cost attribution (`M-04-4.1`) feeds `CostRecord`; the turn pipeline is where tracing spans attach |
| 06 PRODUCT CORE | `FeatureFlag` is the definition there; `FeatureFlagState` is the runtime value here |
| 01 CORE | Correlation depends on identity (`M-01-1.1`); alert delivery uses the notification transport (`M-01-8.2`) |

---

## 13. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Which logging library | `WI-10-1.1.1`, against the five requirements in `OBSERVABILITY_STANDARDS.md` §2 | **Not yet decided** — NOT SELECTED |
| Whether `operations` leaves the shared database | Time-series volume, after `M-10-2.2` | Not yet decided — most likely of any layer |
| Telemetry backend and sink | `M-10-2.2` presumes OpenTelemetry instrumentation; the backend is unchosen | **Not yet decided** |
| Whether turn traces live in the `ai` schema or a time-series store | Volume, after `M-10-2.2` | Not yet decided — `AI_ARCHITECTURE.md` |
| Alert routing and on-call model | `M-10-3.1`, and there is currently one person | Not yet decided |
| Recovery time and recovery point objectives | `M-10-7.1` measures against them, and they are unstated | **Not yet decided** |

---

## 14. References

- `OBSERVABILITY_STANDARDS.md` — the log record, levels, correlation and span identifiers, the event
  taxonomy, metrics, health check shape, PII rules, retention, and the logging library decision
  criteria.
- `DELIVERY_ARCHITECTURE.md` — deployment, promotion, backup and restore mechanisms.
- `ASSURANCE_ARCHITECTURE.md` — the verdict that precedes release.
- `DEVELOPER_ARCHITECTURE.md` — incident-derived work items and autonomy level 3.
- `AI_ARCHITECTURE.md` — the turn pipeline that tracing must attribute time to; per-turn cost.
- `SECURITY_STANDARDS.md` — the controls that `M-10-6.1` monitors but does not replace.
- `DATA_OWNERSHIP.md` — §4 the entity list, §5 derived versus owned, §6 the `FeatureFlag` split.
- `ERROR_HANDLING.md` — the failure taxonomy that error telemetry classifies against.
