# Experience Architecture

**Status:** TRANSITION — a working conversation implementation exists, in the wrong place. It is a
product (`Nexus.Products.Chat`) and must become a layer (`Nexus.Experience`). No `Nexus.Experience.*`
project exists yet. Each gap names the milestone that closes it
**Owner:** Durai
**Last updated:** 2026-08-21
**Layer:** 11 EXPERIENCE — repository `Nexus.Experience`, schema `experience`
**Authoritative for:** the shape and boundaries of the EXPERIENCE layer — the conversation engine and
its entity model, `ScopeRef` and its opacity, `IScopeResolver` and the context handoff, scope kind
registration, the reusable interaction surfaces, commands, approvals and notification UX, and the
future scope of voice and realtime interaction.

**Not authoritative for:** the `ContextBundle` and `ContextItem` types, ranking, prompt assembly or
anything the AI layer does with what it receives — `AI_ARCHITECTURE.md` and
`AI_DEVELOPMENT_STANDARDS.md`. `Workspace`, `Project` and `Subproject` — PRODUCT CORE. The work-graph
structure a DEVELOPER conversation is held against — `DEVELOPER_ARCHITECTURE.md`. Frontend code
rules — `TYPESCRIPT_REACT_STANDARDS.md`. Which layer owns which entity — `DATA_OWNERSHIP.md`.

---

## 1. Purpose, and the sentence the layer is built around

EXPERIENCE provides reusable human and system interaction capability — above all the conversation
engine — so every layer and product gets chat without rebuilding it.

> **Conversation is universal. Structure is contextual.**

Both halves are load-bearing, and the second half is the one that is usually lost.

**Conversation is universal.** A message, a participant, a thread, an attachment and a session are
the same shape whether the subject is a milestone, a purchase order, a machined part or nothing in
particular. Building them three times produces three divergent implementations, three migration
paths and three sets of bugs.

**Structure is contextual.** What a conversation is *about* differs completely between consumers, and
that difference must not enter the engine. The moment the conversation core learns what a `Milestone`
is, it has acquired a dependency on DEVELOPER; the moment it learns what a purchase order is, it has
acquired one on an ERP; and a layer that depends on its own consumers is not reusable, it is a
distribution mechanism for coupling.

The failure this prevents has a name in the roadmap: **conversation becoming the architecture.** It
happens gradually. A conversation needs to show which project it belongs to, so a `ProjectId` is
added. Then a milestone. Then a work item. Each addition is individually reasonable and the sum is a
conversation table that is a join table for every domain in the system, which nothing can change
without touching everything.

The mechanism that prevents it is §3.

---

## 2. Current state — a product that must become a layer

**CURRENT.** `Nexus.Web` contains `Nexus.Products.Chat`, and the conversation implementation inside
it works:

| Exists today | Where |
|---|---|
| `Conversation`, `ConversationMessage` aggregates | `Nexus.Products.Chat.Domain` |
| `Conversations`, `ConversationMessage`, `Chat` endpoints | `Nexus.Products.Chat.Api/Endpoints` |
| `ChatPanel.tsx`, `MessageThread.tsx`, `ConversationList.tsx`, `CreateConversationForm.tsx` | `Nexus.Web.Client/src/features/chat/` |
| `CitationsPanel.tsx`, `citationTargets.ts`, `useCitationTarget.ts` | same |
| `ChatTelemetryContext.tsx` | same |
| `ChatContextBundleMapperTests.cs` | `Nexus.Products.Chat.Tests` — **one of only two behaviour tests in the entire system** |

That is a real, working chat surface with a citations panel, and it is in the wrong layer. It is a
*product*, which means every other consumer that wants a conversation would either depend on a
product — forbidden — or build its own.

**TARGET.** The layer absorbs it. `M-11-1.1` moves `Conversation` and `ConversationMessage` out of the
Chat domain, renames `ConversationMessage` to `Message` in the layer namespace, and migrates the
schema from `conversation` to `experience` — *preserving existing rows; this is a rename, not a
rebuild.* Chat's other aggregates disperse: `Workspace` and `Project` to PRODUCT CORE, `WorkItem`,
`Adr`, `Branch`, `Snapshot`, `Artifact` and `Session` to DEVELOPER, DELIVERY and DATA per
`DATA_OWNERSHIP.md` §7.

And the naming rule that survives the move:

> **The universal conversation engine is never called a Chat product.**

An optional consumer-facing Nexus Chat *application* may later exist under PRODUCTS (layer 12) and
would **consume** this engine. That is a product with a UI and a market position. This is a layer with
a contract. Conflating them is what put the code in the wrong place to begin with.

---

## 3. The mechanism — `ScopeRef`, `IScopeResolver`, and the handoff

Three pieces. Together they are how one engine serves consumers with unrelated structure.

### 3.1 `ScopeRef` — an opaque handle

A conversation carries a `ScopeRef` and **nothing more about its context**. A scope kind and an
identifier. The engine does not parse it, does not branch on it, and does not resolve it itself.

`ScopeRef` already exists, in `Nexus.Intelligence.Contracts/Turns/`. That placement is not an
accident: the same opaque handle travels from the conversation, through the engine, into the AI turn,
and is opaque at every stop. **Intelligence never parses or branches on a `ScopeRef` either** — that
is one of the three AI invariants.

### 3.2 `IScopeResolver` — the consumer's side

`M-11-2.1`. A consumer registers a scope kind and supplies a resolver:

```
IScopeResolver:  ScopeRef  →  ContextBundle
```

Four properties, each an acceptance criterion:

| Property | Why |
|---|---|
| **The contract mentions no consumer type** | If it named one, the layer would depend on that consumer |
| Resolution is **tenant-scoped and permission-checked** | The resolver is a read path into a consumer's data; it is a security boundary |
| Two consumers with different hierarchies are served **simultaneously** by one engine | The whole claim of the design, tested rather than asserted |
| An unregistered scope kind produces **a clear error, not an empty bundle** | An empty bundle looks like "no context available" and is indistinguishable from a misconfiguration |

`ScopeKindBinding` is the stored registration — scope kind to resolver.

### 3.3 The handoff — one sentence, and it is the whole design

> **The engine calls the resolver and passes the resulting `ContextBundle` through untouched.**

Untouched means untouched. Not enriched with conversation metadata, not filtered, not reordered, not
inspected to decide which agent to use. The engine is a courier. Selection, ranking and prompt
assembly all happen inside AI, which is where the machinery for them lives.

That single rule is what produces **zero shared types** between consumers. DEVELOPER knows
`Milestone`. AI knows `ContextItem`. EXPERIENCE knows neither, and the three never need a common
vocabulary because the handoff is a flattening, not a translation.

`M-11-1.2` enforces it as a test: an architecture test fails if the conversation core references any
layer 06, 07 or 12 assembly, and the forbidden-type list is explicit and reviewed. The forbidden set
in `M-11-1.1` is named outright — `Workspace`, `Project`, `Milestone`, `WorkItem`, `Adr`, `Build`,
`Release`, `Repository`, `Worker`.

---

## 4. The three-consumer worked example

One engine, three consumers, three completely different structures, zero shared types.

### Consumer 1 — DEVELOPER (`M-07-6.1`, P2)

Scope kinds: `Milestone`, `Feature`, `WorkItem`, `Task`, registered with PRODUCT CORE at `M-07-1.1`.
The trunk runs the full depth:

```
Workspace → Project → Subproject → Release → Milestone → Feature → WorkItem → Task → Subtask
```

A conversation held against a `Milestone` resolves to `ContextItem`s carrying that milestone's
outcome, its dependencies, its work items and its linked DATA document. The mapping is declared in
`T-07-6.1.1.1`: milestone outcome → `Kind` `Objective`; a blocking dependency → `Kind` `Constraint`;
a `DevelopmentResult` → `Kind` `Outcome`; and `TrustLevel` derives from record age and review state.

*Changing the milestone changes the conversation's context without a code change.* That is the
acceptance criterion, and it is what "contextual" means operationally.

### Consumer 2 — A plain conversation (`WI-11-2.1.2`, P2)

Scope kinds: `Workspace`, `Project`. The trunk **stops at `Project`**:

```
Workspace → Project
```

There is no milestone, no work item, no development structure at all — because the consumer is not
developing anything. It is a conversation about a project. The resolver returns the project's own
context and nothing else.

This is deliberately the **simplest possible resolver**, and it doubles as the worked example in the
roadmap. If the plain resolver needs anything the DEVELOPER resolver needs, the contract has leaked.

### Consumer 3 — Machine work (P5, gated; **no machine domain exists**)

Scope kinds of its own entirely. A machine is a PRODUCT (layer 12) with its own database and its own
aggregates — `Machine`, `Configuration`, `Characteristic`, `Measurement`, `IoPoint`,
`BillOfMaterials`. Its hierarchy shares **nothing** with DEVELOPER's:

```
Machine → Configuration (one physical unit) → Characteristic → Measurement
```

A conversation held against a `Characteristic` resolves to nominal value, tolerance, unit,
measurement history, and the calibration state of the instrument that produced each measurement. Not
one of those concepts exists anywhere in the DEVELOPER hierarchy, and the engine needs no change to
serve it.

### What the three have in common

| | Trunk | Depth | Shared types with the others |
|---|---|---|---|
| DEVELOPER | Workspace → … → Subtask | Nine levels | **None** |
| Plain conversation | Workspace → Project | Two levels | **None** |
| Machine | Machine → … → Measurement | Its own, entirely | **None** |

They share `ScopeRef`, `ContextBundle` and `IScopeResolver` — three neutral shapes owned by two
layers that know nothing about any of them. That is the entire coupling surface, and it does not grow
when a fourth consumer arrives.

---

## 5. The entity model

Thirteen entities in four groups. `DATA_OWNERSHIP.md` §4 holds the canonical list.

### 5.1 Conversation core — `M-11-1.1`

| Entity | Is | Ref |
|---|---|---|
| `Conversation` | A thread, carrying an opaque `ScopeRef` and nothing else about its context | `CNV-` |
| `Message` | One turn in the thread. Renamed from `ConversationMessage` on the move | `MSG-` |
| `Participant` | A CORE `User` or an AI `Agent` taking part | |
| `Attachment` | A file attached to a message | |
| `ConversationSession` | A bounded stretch of interaction within a conversation | |

`ConversationType` and `ConversationVisibility` are **gone from the core**, or expressed as
consumer-registered metadata. They are the clearest example of the failure in §1 — both are
classifications that only a consumer can define, and holding them in the core means the core has an
opinion about what kinds of conversation exist.

### 5.2 References out — `WI-11-1.1.2`

| Entity | Points at |
|---|---|
| `MemoryReference` | An AI `MemoryRecord` |
| `KnowledgeReference` | A DATA `KnowledgeItem` |
| `ToolUsage` | A tool invocation |
| `ResultReference` | An outcome produced elsewhere |

**All are opaque ids plus a kind. No cross-layer foreign keys.** These four types are the whole
design in miniature: the conversation core *points at* memory, knowledge, tool results and outcomes
without owning or interpreting any of them. A foreign key would couple the schemas and their
migrations; an opaque id plus a kind couples nothing.

### 5.3 Extensibility and interaction surface

| Entity | Is | Milestone |
|---|---|---|
| `ScopeKindBinding` | Scope kind → resolver registration | `M-11-2.1` |
| `CommandDefinition` | A registered action reachable from the command palette | `M-11-4.1` |
| `NotificationDelivery` | A delivered notification and its channel | `M-11-5.2` |
| `UIPreference` | Per-scope interface preference | `M-11-6.2` |

---

## 6. Reusable interaction surfaces

The conversation engine is the largest reusable surface but not the only one. Everything here is P3
and later, and each exists because it is a pattern every product otherwise reinvents.

| Surface | Milestone | The property that makes it reusable |
|---|---|---|
| Chat components — thread, composer, list, citations | `M-11-3.1` | **Scope becomes a prop**, not a hardcoded project lookup |
| Command palette | `M-11-4.1` | A layer registers a command and it appears **without a client change** |
| Unified search | `M-11-4.2` | Results are **permission-filtered before ranking**, not after |
| Approval surface | `M-11-5.1` | A DEVELOPER integration approval and a workflow approval **render identically** |
| Notification centre | `M-11-5.2` | Per-channel preferences that **every producing layer honours** |
| Design tokens and primitives | `M-11-6.1` | A theme change propagates **without per-product edits**; every primitive keyboard-reachable with a visible focus state |
| Contextual capability UI | `M-11-6.2` | A capability the member is not entitled to is **absent, not disabled** |

Two of those deserve a note.

**Permission-filtered before ranking** (`M-11-4.2`) is a correctness requirement disguised as an
ordering detail. Ranking first and filtering after leaks existence: the result count, the pagination
and the relative ordering all reveal that something the user may not see exists.

**Absent, not disabled** (`M-11-6.2`) is the same principle in the interface. A disabled button for a
capability someone is not entitled to advertises the capability and invites a request; absence is the
correct rendering of "not yours".

`M-11-3.1` is a GATE B closer, and its extraction work is specific: generalise `ChatPanel`,
`MessageThread` and `ConversationList` so scope is a prop; keep `CitationsPanel` and the telemetry
context intact; expose conversation endpoints at `/experience/v1`; retire the Chat product API
surface. The acceptance criterion — *no component imports a Developer or product type* — is §3 applied
to the frontend.

---

## 7. The DEVELOPER conversation is P2, not GATE A

DEVELOPER gains a conversation surface at `M-07-6.2`, which depends on `M-07-6.1` (the work-graph
resolver) and `M-11-3.1` (the reusable components). All three are **P2 / GATE B**.

At GATE A, **DEVELOPER has an API and a work-graph view, and no conversation surface at all.**

What the surface adds when it arrives is worth stating, because it explains why the ordering does not
cost anything: asking *what is blocking this milestone* returns an answer citing dependency records,
and an approval given in conversation writes a real `Review`. Both are convenience over capability
that already exists. The graph answers the blocking question at `M-07-2.1`; the API accepts the review
at `M-07-5.1`. Conversation makes them pleasant, not possible.

That is the general shape of this layer's value and it is worth being clear-eyed about: **EXPERIENCE
makes things reachable, not achievable.**

---

## 8. EXPERIENCE contributes nothing to GATE A — deliberately

| Layer | In GATE A |
|---|---|
| **EXPERIENCE** | **NOTHING** |
| AUTOMATION | **NOTHING** |
| OPERATIONS | Structured logging with correlation only |

This is not an oversight and it is not a deferral of something needed. It was an explicit change in
v2.2: `M-11-1.1`, `M-11-1.2`, `M-11-2.1` and `M-11-3.1` were all moved out of GATE A into P2, along
with DEVELOPER's own scope resolver `M-07-6.1`.

Three reasons, in order of weight.

**1. GATE A is about starting business systems, and nothing here starts one.** The gate's purpose is
the earliest safe point at which internal business systems can begin. The property that makes that
safe is *three independent work items planned, isolated, built, tested, evidenced, reviewed and
integrated simultaneously*. A conversation surface contributes nothing to that sentence.

**2. The conversation core is genuinely heavy.** `M-11-1.1` is `parallel_safe: false` and involves a
schema migration of existing rows, aggregate extraction across a repository boundary, and the removal
of two enums from a working product. It is exactly the kind of well-scoped, valuable, non-urgent work
that a gate exists to get *in front of*.

**3. There is already a working chat surface.** It is in the wrong layer, but it works. Deferring the
extraction costs nobody a capability they have today; it only delays other consumers gaining one.

The consequence to accept honestly: **DEVELOPER V1a is used through an API and a structural view.**
Anyone expecting to talk to Nexus about its own development at GATE A will be disappointed, and that
is the correct trade — talking about the work is worth less than the work being coordinated
correctly, and the gate is drawn where the value actually is.

The GATE B rule applies in full: this work runs in parallel with business development and must never
pause or block it. A business system waiting on the conversation engine is a scheduling error.

---

## 9. Future scope

### 9.1 Future product conversations

Every product built after `M-11-2.1` gets conversation by registering a scope kind and writing a
resolver. No engine change, no schema change in this layer, no new components. The cost of the Nth
conversational surface is one resolver.

An ERP module registers a scope kind for its business objects and a conversation held against one
resolves that object's state, its history and its open approvals. That is the payoff of `M-11-2.1`,
and it is what "makes each additional product cheaper" means concretely.

### 9.2 Voice and realtime — P4

| Milestone | Delivers |
|---|---|
| `M-11-7.1` Streaming and realtime presence | Responses stream token by token; participants see each other |
| `M-11-7.2` Voice input and output | A conversation conducted without a keyboard |

`M-11-7.2` carries the criterion that keeps it from becoming a second architecture: **voice input
produces the same `ContextBundle` path as typed input.** Voice is an input and output modality, not a
different kind of conversation. A parallel voice pipeline with its own context handling would
duplicate the entire engine to change how characters arrive.

The driver is stated plainly in the roadmap and it is not consumer novelty: **hands-busy machine and
field work.** Someone with a measuring instrument in one hand and a part in the other cannot type,
and that is where the modality earns its cost.

---

## 10. Boundaries with the sibling layers

| Layer | The seam |
|---|---|
| 06 PRODUCT CORE | Owns `Workspace`, `Project`, `Subproject` and the scope kind registry (`M-06-1.2`). EXPERIENCE registers against it |
| 07 DEVELOPER | Implements `IScopeResolver` (`M-07-6.1`). EXPERIENCE learns nothing about milestones |
| 04 AI | Receives the `ContextBundle` untouched. `ScopeRef` is opaque to both layers |
| 02 DATA | Documents and knowledge are DATA's; EXPERIENCE holds a `KnowledgeReference` |
| 01 CORE | A `Participant` is a CORE `User` or an AI `Agent`; notification transport is `M-01-8.2` |
| 12 PRODUCTS | A Nexus Chat *application*, if ever built, is a product that consumes this layer |

---

## 11. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Whether `ConversationType`/`ConversationVisibility` become consumer metadata or disappear | `M-11-1.1` allows either | Not yet decided |
| Whether a conversation may have more than one `ScopeRef` | A real multi-scope need; none exists yet | Not yet decided — one is the default |
| Where `Attachment` bytes live | DATA is the likely home; EXPERIENCE would hold the reference | Not yet decided |
| Streaming transport | `M-11-7.1` | Not yet decided |
| Whether a consumer-facing Nexus Chat product is ever built | A market reason, not a technical one | Not yet decided — the layer does not depend on the answer |

---

## 12. References

- `AI_ARCHITECTURE.md` — `ContextBundle`, `ContextItem`, `ScopeRef` opacity, the turn pipeline that
  receives the handoff.
- `AI_DEVELOPMENT_STANDARDS.md` — §5, the context seam and how consumers flatten into `ContextItem`.
- `DEVELOPER_ARCHITECTURE.md` — §5 the scope extension, §14 why V1a has no conversation surface.
- `DATA_OWNERSHIP.md` — §4 the entity list, §6 the `Conversation` split, §7 the Chat migration matrix.
- `TYPESCRIPT_REACT_STANDARDS.md` — frontend structure, hooks, component rules.
- `LAYER_MODEL.md` — layer 11 in the context of the other eleven.
- `PRODUCT_DEVELOPMENT_GUIDE.md` — what a product that consumes this layer must do.
- `MACHINE_DEVELOPMENT_GUIDE.md` — the machine domain in consumer 3, and why none of it exists yet.
