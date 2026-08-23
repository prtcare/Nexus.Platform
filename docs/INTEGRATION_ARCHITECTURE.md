# Integration Architecture

**Status:** TRANSITION — **five integration points exist today and all five work**; everything else in
this document is the shape the sixth onward must take. Each gap names the milestone that closes it
**Owner:** Durai; each layer owns the contract it publishes
**Last updated:** 2026-08-21
**Layer:** cross-cutting
**Authoritative for:** how anything in Nexus talks to anything else — the mechanism table, layer-to-
layer and product-to-layer integration, database boundaries as integration boundaries, HTTP and
contract compatibility, external API integration, AI provider integration, tool integration, the
machine integration boundary, failure isolation, timeouts, retries and contract versioning.

Not authoritative for: which layer may reference which — `DEPENDENCY_RULES.md`; HTTP surface
conventions, status codes and Problem Details — `API_STANDARDS.md`; events — `EVENT_ARCHITECTURE.md`;
schema mechanics — `DATABASE_STANDARDS.md`; the failure taxonomy — `ERROR_HANDLING.md`; the machine
domain itself — `MACHINE_DEVELOPMENT_GUIDE.md`.

---

## 1. The integration points that exist today

Nine, of which five are runtime integrations and four are build- or developer-time. This is the whole
list. Nothing else in Nexus currently talks to anything else.

| # | From | To | Mechanism | Contract | State |
|---|---|---|---|---|---|
| 1 | `Nexus.Web.Client` | `Nexus.Products.Chat.Api` | HTTP, `/api/v1` | `ApiClient.ts`, `ApiError.ts`, TanStack Query hooks | **Works.** `http://localhost:5299` |
| 2 | `Nexus.Products.Chat.Api` | `Nexus.Intelligence.Api` | HTTP, **`/intelligence/v1`** | `IIntelligenceClient`, `IntelligenceTurnRequest`/`Response` | **Works.** Port not verified — do not state one |
| 3 | `Nexus.Intelligence.*` | OpenAI | **Through CORE's gateway** — `IModelGateway` → `RoutingModelGateway` → `OpenAIModelGateway` | `Nexus.Platform.Contracts/Models/` (13 types) | **Works.** The only working provider |
| 4 | `Nexus.Products.Chat.Infrastructure` | Azure SQL / LocalDB | EF Core, `NexusChatDbContext` | `IEntityTypeConfiguration`, migrations | **Works** for `Workspace`; ten aggregates still on Dataverse |
| 5 | `Nexus.Products.Chat.Infrastructure` | Dataverse | `Microsoft.PowerPlatform.Dataverse.Client` | Ten aggregate implementations | **TRANSITION — being deleted at `M-02-1.4`** |
| 6 | `Nexus.Web` | LocalNuGet | NuGet restore via `nuget.config` | `Nexus.Platform.*`, `Nexus.Intelligence.*` packages | Works on one machine. **Blocks CI** |
| 7 | `Nexus.Int` | LocalNuGet | Same | `Nexus.Platform.*` packages | Same |
| 8 | `NexusAI`, `Nexus.Int` | LocalNuGet | `pack-local.ps1` | — | Publishes to `C:\Personal\LocalNuGet` |
| 9 | Developer machine | The OpenAI key | `set-openai-key.ps1` | — | **TARGET — `ISecretResolver`, `M-01-5.1`** |

**Rows 6 to 8 are one problem.** `C:\Personal\LocalNuGet` is a directory. It has no integrity
checking, no provenance and no access control beyond the filesystem, and **it is unreachable from any
build agent**. That is why `M-08-1.1 Package feed reachable from CI` is P0 with no dependencies —
nothing else in DELIVERY can proceed while packages resolve only from a path that exists on one
computer. Its acceptance criterion is exact: *a clean machine with no `C:\Personal\LocalNuGet`
restores every solution.*

**Row 3 is the one to study.** Intelligence reaches OpenAI, and it does so without holding a
credential. That indirection is §8, and it is the most consequential integration decision in the
system.

---

## 2. The six mechanisms, and how to choose

| Mechanism | Use when | Coupling created |
|---|---|---|
| **Direct call via DI** | Same process, downward direction, caller needs the result | Compile-time on the callee's contract |
| **HTTP** | Different hosts, downward direction, caller needs the result | Runtime on a URL and a wire shape |
| **Package reference** | A contract or implementation is consumed across repositories | Build-time, versioned |
| **Event** | The emitter must not know the consumer exists **and** does not need the outcome | On the event contract only. **`EVENT_ARCHITECTURE.md` — there is no bus today** |
| **Job (AUTOMATION)** | Durable, retryable work that may outlive a request | On a queue name and a payload shape |
| **Polymorphic reference** | Pointing at a row in a layer you may not reference | None — layer, type, id as plain columns |

**Two rules govern the choice before preference does.**

1. **The direction must be legal.** `DEPENDENCY_RULES.md` §4 decides that, and it decides it before
   any of the six is available. An illegal direction is not fixed by choosing a looser mechanism —
   an HTTP call upward is the same violation as a project reference upward, with the compiler no
   longer able to catch it.
2. **Adjacency is not permission.** Two schemas in one database and two projects in one solution are
   as separate as two services. The integration is through the owning context's contract surface
   either way.

---

## 3. Layer-to-layer integration

### 3.1 The default is a direct call downward

Most of Nexus is one process calling itself in the correct direction, and that is not a compromise.
`TurnPipeline` invoking `IntentClassifier`, `PolicyGate`, `ContextSelector`, `AgentSelector`,
`ModelSelector`, `PromptStep`, `ModelStep`, `ToolLoop` and `ResponseComposer` in order is the whole
integration story of the AI layer, and it is the right one.

### 3.2 The four legitimate ways to go upward

You will need to. The reference is still forbidden. `DEPENDENCY_RULES.md` §8 owns the list; the
integration-side detail:

| Shape | The integration it produces | Live instance |
|---|---|---|
| **Invert the dependency** | The lower layer publishes an interface; the upper implements and registers it | EXPERIENCE publishes `IScopeResolver`; DEVELOPER implements it — `M-11-2.1`, `M-07-6.1` |
| **Flatten to a neutral type** | The upper layer converts its model into something the lower already knows | A consumer flattens `Milestone` into `ContextItem`s; AI never learns the type |
| **Polymorphic reference** | Layer, type and id as plain columns, no foreign key | ASSURANCE's `TraceabilityLink` pointing at a DEVELOPER work item |
| **Emit an event** | Neither side references the other's implementation | `PipelineCompleted` — **`M-01-8.1`, P2. Not available today** |

**Registration happens in the composition root.** A host wires the implementation to the interface;
neither layer's assembly needs to name the other's if the root does it. That is what makes the
07 → 11 cell decidable without either layer taking a dependency — one of three clean shapes named in
`DEPENDENCY_RULES.md` §5, and the decision is an ADR (next: **ADR-016**) due before `M-07-6.1`.

### 3.3 The two integrations that carry the most weight

**DELIVERY → DEVELOPER.** DELIVERY produces a build; DEVELOPER decides whether it satisfied a work
item. The contract is `M-08-1.3`'s result artifact: a **versioned** JSON document carrying branch,
commit, outcome and test counts, **retrievable by branch name**. DEVELOPER ingests it —
*a CI result is ingested and linked to the correct run without manual entry* (`M-07-4.1`). DELIVERY
knows nothing of milestones. The version field exists so DEVELOPER's ingestion can evolve without a
lockstep release.

**Consumer → AI.** The consumer supplies a `ContextBundle` and an opaque `ScopeRef`; AI returns an
`IntelligenceTurnResponse` with `Citation`s and a `DecisionTrace`; the consumer resolves citations
back through its own identifiers. Three parties change independently because none of them shares a
vocabulary with the others. `AI_ARCHITECTURE.md` §6 owns the mechanism.

---

## 4. Product-to-layer integration

A product integrates with layers 01–11 by **declaring capability packs**, and the layer never learns
which product declared it. `PRODUCT_ARCHITECTURE.md` owns the shape; the integration-side rules:

| Direction | Rule |
|---|---|
| Product → layer | Through the layer's published contract. A product may reference layers 01–06, 08, 10, 11 |
| Layer → product | **Never.** No layer names a product type, holds a product's `DbContext`, or branches on product identity |
| Product → product | **Never** — not by project reference, not by database, not by shared table. Where two products need the same fact, it belongs to a layer below them |
| Product → another product's data | Through that product's **HTTP API only**, as an ordinary external caller |

**The registration path.** A product registers scope kinds with PRODUCT CORE (`M-06-1.2`), supplies an
`IScopeResolver` to EXPERIENCE if it has a conversational surface (`M-11-2.1`), supplies a
`ContextBundle` mapper for AI, adds a DELIVERY pipeline (`M-08-1.2`) and emits OPERATIONS telemetry.
Five integrations, all of them registrations, **none of them a modification to a layer below 12**.

**CURRENT.** One product exists and it predates this path. `Nexus.Products.Chat` holds
`ChatProductModule.cs` as its single registration point — the pattern `<Name>ProductModule.cs`
follows — and `ChatContextBundleMapper` as its AI integration, which is one of only two things in the
system with a behaviour test.

---

## 5. Database boundaries are integration boundaries

### 5.1 The shape

| | |
|---|---|
| Layers 01–11 | **One database, `NexusPlatform`, one schema each** — `core`, `data`, `governance`, `ai`, `automation`, `product_core`, `developer`, `delivery`, `assurance`, `operations`, `experience`. **TARGET — `M-02-1.5`** |
| Layer 12 | **One database per product.** Not a schema in the shared database |

**CURRENT: the only migration that exists created `[org].[Workspace]`.** `org` is not one of the
eleven layer schemas — it is a pre-convention name in running, proven code. It is correct in
`Nexus.Products.Chat.Infrastructure` and wrong everywhere else. **Do not add a second table to it.**

### 5.2 Cross-schema access

| Situation | Rule |
|---|---|
| Reading another layer's table directly | **Forbidden.** Go through the owning layer's repository or API |
| A foreign key crossing schemas | **Allowed only where `DEPENDENCY_RULES.md` §4 allows a reference** — downward, or to a cross-cutting layer |
| A foreign key the direction forbids | **Polymorphic and constraint-free** — layer, type, id, no FK. Integrity enforced in application code and proven by test |
| A query joining two layers' tables | Exactly as a foreign key: downward only, otherwise compose in application code |
| Writing outside your layer's schema | **Forbidden.** An architecture test fails any `IEntityTypeConfiguration` writing outside its assembly's schema — `M-02-1.5` |

### 5.3 Cross-product access — forbidden, and physically so

Cross-product foreign keys and joins **cannot exist**, because different databases. A product never
receives another product's connection string. A platform table copied into a product database is the
boundary being broken, not an optimisation, and a product table inside `NexusPlatform` is the same
error mirrored. `DATABASE_ARCHITECTURE.md` §§5–6 owns all of it.

**Why the physical split is the enforcement.** Rules 3 and 4 are conventions in code and facts in the
database. Two products that cannot share a database cannot share a foreign key however much someone
wants to, and no review is needed to notice.

---

## 6. HTTP integration and contract compatibility

`API_STANDARDS.md` owns routes, methods, status codes, Problem Details, pagination and OpenAPI. The
integration-side rules — what a *caller* may rely on:

| Rule | |
|---|---|
| Version in the path | `/api/v1`, `/intelligence/v1`. A breaking change is a new version, never a silent reshape |
| Additive changes only within a version | Add optional fields. Never remove, rename or change the meaning of one |
| The killer is meaning | A field whose type is stable and whose semantics changed breaks callers with no compile error and no failed request |
| A caller tolerates unknown fields | A client that rejects a field it does not know turns every additive change into an outage |
| Correlation flows | `X-Correlation-Id` accepted, generated at the edge if absent, echoed on every response — **TARGET `M-10-1.1`** |
| Contract published | **TARGET — `M-08-1.2` publishes OpenAPI output.** Swashbuckle is present; nothing publishes the artefact |

**CURRENT: no correlation id exists on any host.** A request cannot presently be followed from the
frontend through the Chat API into Intelligence and out to OpenAI. Across three integration hops that
is the single largest gap in operability, and it is why `M-10-1.1` sits in P1.

**Package contracts version the same way.** `Nexus.Platform.Contracts` and
`Nexus.Intelligence.Contracts` are consumed across repository boundaries as NuGet packages, which
makes every public type in them a published contract. Adding a member to an interface those packages
expose breaks every implementer at build time — new capability goes on a new interface.

---

## 7. External API integration

**Everything outside Nexus is untrusted, slow and occasionally wrong.** The rules follow from that
rather than from any specific vendor.

| Rule | |
|---|---|
| One adapter project per external system | The SDK is referenced by exactly one project. `Nexus.Platform.Providers.OpenAI` is the pattern |
| The adapter maps to Nexus types at its edge | An external DTO never travels inward. If it does, the vendor owns your domain model |
| Credentials through `ISecretResolver` | Never a constructor parameter carried through layers, never appsettings — **`M-01-5.1`** |
| Every call has an explicit timeout | §11 |
| Every call has a bounded retry policy | §12 |
| Failure is contained | §10 |
| Registered in GOVERNANCE | `ExternalService` and `ExternalServiceDependency` — `M-03-5.2`. A service marked as receiving personal data cannot be linked to a product lacking a privacy requirement |
| Content is untrusted | Retrieved content carries `TrustLevel`. It is data to be reasoned about, **never instructions to be followed** |

That last row is the defence against prompt injection and it only works if it is honoured. An agent
that treats retrieved text as an instruction has been compromised by whoever wrote that text.

**CURRENT: exactly one external system is integrated — OpenAI.** There is no HTTP resilience library,
no circuit breaker and no vulnerability scanning of any dependency.

---

## 8. AI provider integration — credentials never leave CORE

> **Provider credentials never leave CORE. There is no API key anywhere in `Nexus.Intelligence`.**

This is the system's most consequential integration invariant, and it looks wrong at first reading:
the AI layer does not own model access.

```
Nexus.Intelligence.Core/Turns/ModelStep
        │  holds IModelGateway — an interface, nothing more
        ▼
Nexus.Platform.Contracts/Models/IModelGateway
        ▼
Nexus.Platform.Core/Models/RoutingModelGateway     selects a named gateway
        ▼
INamedModelGateway
        ▼
Nexus.Platform.Providers.OpenAI/OpenAIModelGateway  ← the ONLY place OpenAI.dll is referenced
        │  credential resolved here, via ISecretResolver
        ▼
    OpenAI
```

**Two consequences, and the second is the one usually missed.** If AI held credentials, every consumer
of AI would transitively hold a path to them and the blast radius of any AI-layer defect would include
the provider account. And because the gateway is in CORE, **model access is available to layers that
are not AI** — a layer needing one completion does not take a dependency on reasoning, agents, context
and memory to get it.

| Rule | |
|---|---|
| A direct provider SDK call outside `Nexus.Platform.Providers.<Vendor>` | **Forbidden** |
| A second provider | A new project, never a branch inside an existing one — `M-01-6.2` |
| Model selection versus model access | AI selects (`ModelSelector`, `ModelRoute`); CORE calls |
| Usage recorded | `ModelUsage`, `UsageRecord` — who, which model, token counts, cost. **Never the prompt body** |
| Timeout | Longer than an ordinary outbound call, bounded, and **always explicit — never infinite** |

**CURRENT.** `OpenAIModelGateway` works. `AnthropicModelGateway` is a **306-byte stub**.
`set-openai-key.ps1` is the live credential path and `ISecretResolver` is a contract with no
implementation — `M-01-5.1` closes that, and `M-01-6.1` verifies the OpenAI path end to end.
`Azure.Identity` and `Azure.Core` are present but arrived with Dataverse; **do not treat them as a
chosen credential path** until something selects them.

---

## 9. Tool integration

A tool is how AI reaches anything that is not a model. It is the highest-risk integration surface in
the system and it is gated before it runs, not after.

| Rule | |
|---|---|
| Invoked only through `IToolGateway`, resolved from `IToolCatalog` | Never called directly |
| Declares its `SideEffectClass` before registration | Read-only · reversible write · irreversible write · external effect |
| Permission-checked per invocation | The invoking principal's permissions, evaluated every time |
| Bounded | Every invocation has a timeout and a bounded result size |
| Audited | Every invocation with side effects, with arguments and outcome — `M-01-7.1` |
| Approved | Irreversible and external effects require **explicit human approval** — `M-05-5.1` |
| The AI never exceeds the user | A turn carries the user's identity and permissions, never elevated ones |

`SECURITY_STANDARDS.md` §10 owns the class definitions and their requirements.

**CURRENT: no tool can be invoked at all.** `Nexus.Platform.Tools/ToolProvider.cs` is a 231-byte stub
and `Nexus.Intelligence.Api/Tooling/` contains `EmptyToolCatalog` and `EmptyToolGateway`. That
absence is the only reason the absence of tool permissions is not currently an exposure. `M-01-7.1`
is the milestone, **and its permission model must land with it, not after.**

---

## 10. The machine integration boundary

**TARGET — no machine domain exists in Nexus.** The boundary is written now because retrofitting it
is not possible: a system that has already been placed in a safety loop cannot be argued out of it.

> **Deterministic controllers own real-time motion, interlocks and emergency stop.** A PLC or a
> real-time motion controller holds every behaviour on which physical safety depends. **Nexus is not
> in that loop, and no Nexus component — service, agent, model or human-facing UI — is ever placed in
> it.**

> **AI may plan, diagnose, document and propose parameters.** That is the whole of its authority. It
> produces proposals.

> **AI must never bypass a hard limit, an emergency stop, an operator approval or validated control
> logic.** Not under any instruction, configuration, urgency or efficiency argument, and not
> "temporarily for a test". **There is no flag, no mode and no permission that enables it.** A system
> that can be talked out of a hard limit does not have one.

**As an integration, this is a direction rule.** Nexus reads from the machine and writes proposals;
the controller reads nothing from Nexus that has not passed a human approval and its own validation.
The integration is deliberately asymmetric:

| Direction | Allowed |
|---|---|
| Controller → Nexus | Telemetry, measurements, fault codes, state. **Read-only into Nexus** |
| Nexus → Controller | A **proposed** parameter inside a validated range, subject to human approval, applied by the controller which enforces its own limits |
| Nexus → interlock, E-stop, guarding, hard limit | **Never, by any path** |

**The mechanism already exists and is not machine-specific.** An AI turn produces a `ProposedAction`,
and a `ProposedAction` is executed by something else under policy. A machine command is an
irreversible external effect in the most literal sense available, which `SideEffectClass` already
classifies and already requires explicit human approval for. **The machine domain does not need a
special-case safety mechanism; it needs the general one to actually be implemented** — §9, `M-01-7.1`.

**No agent may create, modify or waive a safety-critical acceptance criterion.** Absolute, no
exception path — `M-09-7.2`. `MACHINE_DEVELOPMENT_GUIDE.md` §1 owns the full division of authority.

---

## 11. Failure isolation and timeouts

### 11.1 Isolation

| Rule | |
|---|---|
| A dependency's failure degrades one capability, not the host | A model provider outage must not take down the Chat API |
| A failing handler does not fail its publisher | `EVENT_ARCHITECTURE.md` §9 |
| A failing build blocks **its own work item and no other** | `M-07-4.1`, and it is proven at `M-07-5.3`: Worker B is deliberately failed; A and C complete unaffected |
| Worker isolation is physical | Three assignments, three distinct worktree paths, allocated as **siblings** of the repository, never nested inside it |
| A cross-tenant leak is not a degradation, it is a breach | It fails closed, always — `SECURITY_ARCHITECTURE.md` §6 |

**NOT SELECTED: no circuit breaker, bulkhead or resilience library exists.** Do not write one into a
design as though it were available. Where isolation is needed today it is an explicit timeout plus an
explicit bounded retry.

### 11.2 Timeouts

`API_STANDARDS.md` §13.2 owns the defaults — 30 s inbound, 30 s database command, 10 s outbound, model
invocation longer, bounded and always explicit. The integration-side semantics:

- **A timeout is an outcome, not the absence of one.** It is logged, counted and mapped.
- **An inner timeout is shorter than its outer timeout.** When the model-provider timeout exceeds the
  API request timeout, the client gives up while the server keeps burning provider cost — and both
  the log and the bill record a success.
- **A timeout on a non-idempotent operation leaves the outcome unknown, not failed.** Treating
  unknown as failed and retrying is how duplicates are created.
- Every outbound call has one. A call without a timeout inherits the platform default, which is
  effectively infinite, and one slow dependency then consumes the whole thread pool.

### 11.3 Retries

`API_STANDARDS.md` §13.3 owns the per-status table; `ERROR_HANDLING.md` §7 owns transient versus
permanent. Two rules are integration decisions rather than mechanism:

- **Retry only what is transient *and* idempotent.** Both, not either.
- **Never nest retries.** Retry at one layer. Three layers of three attempts is twenty-seven calls and
  an outage amplifier, and each layer's logs show a reasonable-looking three.

**CURRENT: no retry logic exists anywhere.** `M-05-1.2` Dispatch loop with retry and backoff is the
milestone that introduces the first real policy, and `M-05-1.3` adds dead-lettering behind it.

---

## 12. Contract versioning across every mechanism

One rule, four expressions of it.

| Mechanism | Version how | Breaking change is |
|---|---|---|
| HTTP | In the path — `/api/v1` | A new version path |
| NuGet package | Package version; contracts assemblies are published contracts | A new interface, never a new member on a shipped one |
| Event | **A new type** — `PipelineCompletedV2` | A required field, a rename, or a changed meaning |
| CI result artifact | An explicit schema version field — `M-08-1.3` | A new schema version DEVELOPER's ingestion selects on |

**Additive is safe; meaning is not.** A field whose type is unchanged and whose semantics changed
breaks every consumer silently, with no compile error and no failed request. That is the failure mode
worth designing against, and the only reliable defence is a new name.

---

## 13. What must never happen

| Never | Why |
|---|---|
| An HTTP call in a direction a project reference would be forbidden | Same violation, compiler no longer catches it |
| A product reading another product's database | Physically prevented, and forbidden regardless |
| A layer reading another layer's tables directly | Adjacency is not permission |
| A provider SDK referenced outside `Nexus.Platform.Providers.<Vendor>` | The credential boundary depends on it |
| An external DTO travelling inward past its adapter | The vendor owns your model from then on |
| An outbound call with no timeout | One slow dependency, one exhausted thread pool |
| A retry on a non-idempotent operation | Duplicates, created deliberately |
| A secret passed as a constructor parameter through layers | Resolved through `ISecretResolver`, never carried |
| An event bus built before `M-01-8.1` | Two mechanisms, one of them unowned |
| **Any Nexus component inside a machine safety loop** | §10. No flag, no mode, no permission |

---

## 14. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Whether DEVELOPER references `Nexus.Experience.Contracts` or registration happens in the composition root | **ADR-016**, before `M-07-6.1` | **Not yet decided** — `DEPENDENCY_RULES.md` §5 |
| Whether ASSURANCE gates DEVELOPER's integration by query or by verdict | `M-09-1.3` | Not yet decided |
| Resilience library — circuit breaker, bulkhead, retry policy | Nothing is deployed; the need is not yet measured | **NOT SELECTED** |
| Whether `Azure.Identity` becomes the chosen credential path | It arrived with Dataverse; nothing selected it | Not yet decided |
| Whether products are ever called by other products over HTTP | The second product | Not yet decided — the rule allows it, nothing needs it |
| Message broker technology | §6 of `EVENT_ARCHITECTURE.md` — no trigger is met | Deliberately deferred, **no milestone** |

---

## 15. References

- `DEPENDENCY_RULES.md` — which direction is legal, before any mechanism is chosen.
- `EVENT_ARCHITECTURE.md` — the asynchronous half, and why there is no bus today.
- `API_STANDARDS.md` — routes, status codes, Problem Details, idempotency keys, timeouts, retries.
- `ERROR_HANDLING.md` — the failure taxonomy, transient versus permanent, cancellation.
- `DATABASE_ARCHITECTURE.md` — one platform database, schema per layer, one database per product.
- `SECURITY_ARCHITECTURE.md` — the trust boundaries these integrations cross.
- `SECURITY_STANDARDS.md` §10 — `SideEffectClass` and tool permission requirements.
- `AI_ARCHITECTURE.md` §4, §5 — the model gateway in CORE and the provider abstraction.
- `MACHINE_DEVELOPMENT_GUIDE.md` §1 — the safety boundary in full.
- `BOUNDED_CONTEXTS.md` — the contexts on either end of each integration.
- `../nexus-roadmap.yaml` — `M-01-5.1`, `M-01-6.1`, `M-01-7.1`, `M-02-1.4`, `M-08-1.1`, `M-08-1.3`,
  `M-10-1.1`.
