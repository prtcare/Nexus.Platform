# Dependency Rules

**Status:** TRANSITION — the rules are settled; enforcement exists in three test files that no
pipeline runs, because there is no pipeline
**Owner:** DEVELOPER (Layer 07) defines; ASSURANCE (Layer 09) proves; DELIVERY (Layer 08) gates
**Last updated:** 2026-08-21
**Layer:** cross-cutting
**Authoritative for:** what may depend on what — the layer dependency direction, the cross-cutting
exception, the no-shared-kernel rule, product isolation, the AI seam, the no-product-branching rule,
the full 12×12 allowed-dependency matrix, the forbidden-reference list per layer, and which rule has
a test today versus which is TARGET.

Not authoritative for: what each layer *is* (`LAYER_MODEL.md`), which layer owns an entity
(`DATA_OWNERSHIP.md`), how architecture tests are written and qualified
(`ASSURANCE_STANDARDS.md` §5.4), or when a work item may be worked on (`DEVELOPMENT_WORKFLOW.md`).

---

## 1. The sentence

> **A layer may depend only on layers below it. DELIVERY, ASSURANCE and OPERATIONS are cross-cutting
> and depend on nothing above CORE. Nothing shared ever references a product, and no product ever
> references another.**

Everything in this document is that sentence made checkable.

---

## 2. Why the direction is one-way

An upward reference is not a style problem. It has four specific costs, and each has already
happened somewhere in this codebase or is one commit away:

| Cost | Concretely |
|---|---|
| The lower layer stops being reusable | If `Nexus.Platform.Contracts` mentions `Workspace`, no product that does not have workspaces can use CORE |
| Change ripples upward *and* downward | A product rename forces a platform release |
| The build graph acquires a cycle | Two layers referencing each other cannot be compiled independently, and NuGet packaging stops working |
| The seam stops being testable | You cannot assert "AI does not know about products" once AI references a product assembly |

The reason this is worth stating: today the direction is **correct almost everywhere by accident**,
because most layers do not exist. Nine of the twelve have no code. The rules exist now so that the
nine are built correctly rather than corrected later.

---

## 3. The seven rules

### Rule 1 — Downward only

A layer may reference only layers with a lower number. `01 CORE` references nothing.

**Enforced:** partially. `PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs` and `BoundaryTests.cs`
use NetArchTest and are the mechanism. They can only assert about assemblies their own repository
references, so today they cover the boundaries inside three repositories, not the full twelve-layer
graph — which is unassertable while nine layers have no assemblies to name.
**TARGET — `M-08-1.4` Branch protection and architecture gate** makes the existing tests a hard
pipeline gate. Coverage of the remaining layers arrives with each layer.

### Rule 2 — DELIVERY, ASSURANCE and OPERATIONS are cross-cutting

Anything may *record to* them. Nothing takes an assembly reference *on* them in order to do so.
Records reach them through a CORE-owned abstraction — logging, audit, events — or through their own
ingestion API. The one genuine consumer relationship is DEVELOPER reading DELIVERY's build records
to decide whether a work item is satisfied, and that is a declared downward-style dependency
(§4, row 07).

**Enforced:** no. **TARGET** — the test becomes writable when the first of the three layers has an
assembly. There is no milestone that adds it; add one when `Nexus.Delivery.Contracts` is created.

### Rule 3 — No shared kernel

`Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never reference product types. There
is no "common" or "shared" assembly that products and the platform both extend. A type that two
products both need belongs to the lowest layer that legitimately owns it, or it is two types.

**Enforced:** yes, this is the rule the three existing test files most plausibly cover, and it is
**currently true on disk**. `Nexus.Platform.Contracts` contains `Governance/`, `Identity/`,
`Models/`, `Secrets/` and `Tools/` and mentions no product concept; `Nexus.Intelligence.Contracts`
contains `Turns/`, `Context/`, `Results/` and `Client/` and mentions none either. Keep it true.

### Rule 4 — Products never reference each other

Chat cannot see Vault's types and vice versa. This is enforced **physically** by one database per
product: two products cannot share a foreign key if they cannot share a database. Data crosses
between products through the owning product's API, never through its database.

**Enforced:** structurally, and untestable in a useful way today because exactly one product exists.
`BoundaryTests.cs` in `Nexus.Products.Chat.Architecture.Tests` is the natural home when a second
product arrives. **TARGET — the second product.**

### Rule 5 — AI never sees product structure

AI receives a `ContextBundle` of `ContextItem`s that a consumer flattened. `ScopeRef` is opaque to
it. AI returns an `IntelligenceTurnResponse` with citations and a `DecisionTrace`, and the consumer
resolves the citations back through its own IDs.

This is the seam that lets all three parties change independently: the EXPERIENCE engine knows
`Conversation` and `ScopeRef` but not what a `ScopeRef` points at; the consumer knows its own
hierarchy but not how conversation is stored; AI knows `ContextItem` and neither of the others.

**Enforced:** by construction today, and it is the best-designed boundary in the codebase.
`BoundaryRuleTests.cs` in `Nexus.Intelligence.Architecture.Tests` is where the assertion belongs —
no `Nexus.Intelligence.*` assembly may reference any `Nexus.Products.*` or
`Nexus.Experience.*` assembly. `ChatContextBundleMapperTests.cs` on the Chat side is the only
behaviour test covering the flattening.

### Rule 6 — No product branching

No `if (Product == X)` anywhere in a shared layer. Capability packs are **declared**, not coded. A
product that needs different behaviour registers a different implementation; it does not add a case
to a switch in a layer that is supposed to be product-neutral. The same rule bans branching on
product identity inside PRODUCT CORE and inside ASSURANCE.

**Enforced:** no. **TARGET — `M-12-1.2` Capability pack composition** makes it a build failure
across the solution. **TARGET — `M-09-7.1` Profile definition and selection** adds the ASSURANCE
variant. `M-11-1.2` adds the EXPERIENCE variant: the conversation core fails the build if it
references any PRODUCT CORE, DEVELOPER or PRODUCTS assembly.

### Rule 7 — Structure is not conversation

When something can be modelled as structure or as conversation, model it as structure and let
conversation reference it. A milestone's dependency is a `Dependency` row that a conversation can
discuss — never a sentence in a transcript that has to be re-derived. This is a dependency rule
because the failure mode is a layer reaching into conversation storage for a fact it should own.

**Enforced:** partially, by Rule 6's EXPERIENCE variant (`M-11-1.2`). The positive half — that the
fact exists as a row somewhere — is a review question, not a test. `CODE_REVIEW_CHECKLIST.md`.

---

## 4. The allowed-dependency matrix

Rows depend on columns. Read a row as "what may this layer reference".

- **●** may reference
- **○** records flow this way without an assembly reference (cross-cutting)

> **07 → 11 is resolved, not contested.** `NEXUS_MASTER_ARCHITECTURE.md` §4.2.1: DEVELOPER depends on `Nexus.Experience.Contracts` **only**, never `.Core`. Contracts assemblies depend on nothing, so this creates no upward coupling — EXPERIENCE defines `IScopeResolver`, DEVELOPER implements it, and EXPERIENCE discovers it through DI and calls *down*. A DEVELOPER reference to `Nexus.Experience.Core` is a defect. The other contested cells below (12 → 07, 12 → 09, 07 → 09) remain open pending ADR-016.

- **▲** contested or undeclared — see §5
- **—** forbidden

| ↓ depends on → | 01 CORE | 02 DATA | 03 GOV | 04 AI | 05 AUTO | 06 PROD CORE | 07 DEV | 08 DELIVERY | 09 ASSUR | 10 OPS | 11 EXP | 12 PRODUCTS |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **01 CORE** | — | — | — | — | — | — | — | ○ | ○ | ○ | — | — |
| **02 DATA** | ● | — | — | — | — | — | — | ○ | ○ | ○ | — | — |
| **03 GOVERNANCE** | ● | ● | — | — | — | — | — | ○ | ○ | ○ | — | — |
| **04 AI** | ● | ● | — | — | — | — | — | ○ | ○ | ○ | — | — |
| **05 AUTOMATION** | ● | ● | ● | — | — | — | — | ○ | ○ | ○ | — | — |
| **06 PRODUCT CORE** | ● | ● | ● | — | — | — | — | ○ | ○ | ○ | — | — |
| **07 DEVELOPER** | ● | ● | ● | ● | ● | ● | — | ● | ▲ | ○ | ▲ | — |
| **08 DELIVERY** | ● | — | ● | — | — | — | — | — | ○ | ○ | — | — |
| **09 ASSURANCE** | ● | ● | ● | — | — | — | — | ○ | — | ○ | — | — |
| **10 OPERATIONS** | ● | — | — | — | — | — | — | ○ | ○ | — | — | — |
| **11 EXPERIENCE** | ● | ● | — | ● | — | ● | — | ○ | ○ | ○ | — | — |
| **12 PRODUCTS** | ● | ● | ● | ● | ● | ● | ▲ | ● | ▲ | ● | ● | — |

Three properties are worth checking against the matrix, because they are what make it a design
rather than a table:

1. **The upper triangle is empty of ●** except for the cross-cutting column 08 and the two contested
   cells. That is Rule 1.
2. **Column 12 is entirely `—`.** Nothing in Nexus may reference a product, including another
   product. That is Rules 3 and 4.
3. **Row 01 has no ●.** CORE is the bottom and stays there.

---

## 5. The contested cells

These are not defects to route around. They are decisions that have not been made, and the matrix
marks them ▲ rather than guessing.

| Cell | The situation | What would decide it |
|---|---|---|
| **07 → 11** — DEVELOPER on EXPERIENCE | `nexus-roadmap.yaml` declares `07 depends_on: [… 11]`; `NEXUS_MASTER_ARCHITECTURE.md` §7.7 does not. DEVELOPER implements `IScopeResolver`, which EXPERIENCE publishes — implementing it needs a reference to whichever assembly holds it. The clean shapes are: the contract lives in `Nexus.Experience.Contracts` and DEVELOPER references only that; or the contract lives lower and both reference it; or registration happens in the composition root and neither references the other | An ADR, before `M-07-6.1` Scope resolver for the work graph. The next ADR number is ADR-016 |
| **07 → 09** — DEVELOPER on ASSURANCE | Neither source declares it, yet `M-09-1.3` Quality gate V1 blocks integration while a mandatory criterion is unverified, and integration is DEVELOPER's `IntegrationRun`. Something has to consult something | `M-09-1.3`. Decide whether ASSURANCE gates by responding to a DEVELOPER query, or by emitting a verdict DEVELOPER already has |
| **12 → 07** — PRODUCTS on DEVELOPER | Declared in the roadmap's `depends_on` for layer 12. A product referencing the work graph is surprising: DEVELOPER builds products, products do not read their own build state | Worth challenging before the first new product. If it is real, it is the DEVELOPER *contracts* only and should say so |
| **12 → 09** — PRODUCTS on ASSURANCE | The roadmap's layer 12 `depends_on` omits 09, which reads as an oversight from ASSURANCE's late insertion rather than a decision | Correct the roadmap, or record why a product is exempt |

Until each is decided, **do not write the reference**. An undeclared dependency that ships is a
decision made by whoever was typing.

---

## 6. Forbidden references, per layer

Assembly-level statements. Each is the thing a NetArchTest rule for that layer should assert.

| Layer | Must not reference |
|---|---|
| **01 CORE** | Anything. `Nexus.Platform.Contracts` and `.Core` reference no other Nexus layer, no `Nexus.Products.*`, no `Nexus.Intelligence.*`, no EF Core in Contracts |
| **02 DATA** | Any layer above 01 · any `Nexus.Products.*` · any product `DbContext`. Its migration discipline is used *by* products; it does not know them |
| **03 GOVERNANCE** | 04–12 · any product type. `Product` here is a registry row, never a product's domain type |
| **04 AI** | Any `Nexus.Products.*` · `Nexus.Experience.*` · `Nexus.Developer.*` · 03, 05–12 generally. **The hard one:** no AI type may name `Workspace`, `Project`, `Milestone`, `WorkItem` or any other consumer concept |
| **05 AUTOMATION** | 04 and above · any product type. It runs a process; it does not know what the process means |
| **06 PRODUCT CORE** | 07–12 · any product type · **any branch on product identity**, tested rather than reviewed |
| **07 DEVELOPER** | 12 PRODUCTS · a product's `DbContext` · a product database connection string. It references a `ProductId` from GOVERNANCE, never a product |
| **08 DELIVERY** | 02, 04–07, 09–12. A pipeline knows repositories and artifacts; it does not know what a milestone is |
| **09 ASSURANCE** | 04–08, 10–12 · **any branch on product identity** (`M-09-7.1`). It holds a polymorphic reference to what it verifies, not a typed one |
| **10 OPERATIONS** | 02–09, 11, 12. It observes a running process; it does not know what built it |
| **11 EXPERIENCE** | 03, 05, 07, 08, 09, 10, 12 · **`Workspace`, `Project`, `Milestone`, `Feature`, `WorkItem`, `Task`, `Adr`, `Build`, `Release`, `Repository`, `Worker`** or any other consumer concept in the conversation core (`M-11-1.2`) |
| **12 PRODUCTS** | **Any other `Nexus.Products.*` assembly** · any other product's database · a platform table in its own database |

**The cross-schema equivalent.** A foreign key may cross schemas inside `NexusPlatform` only where
this matrix allows a reference. Where it does not — ASSURANCE pointing at a DEVELOPER work item is
the standing example — the link is polymorphic and constraint-free: layer, type and id as plain
columns, referential integrity enforced in application code and proven by test.
`DATABASE_STANDARDS.md` §5.4 owns the mechanics; `DATABASE_ARCHITECTURE.md` owns why.

---

## 7. Enforcement inventory

**The honest position: three test files exist, and nothing runs them.**

| Test project | File | Repository | What it can see | Status |
|---|---|---|---|---|
| `Nexus.Platform.Architecture.Tests` | `PlatformBoundaryTests.cs` | NexusAI | `Nexus.Platform.*` assemblies | Exists. Runs only when a developer remembers |
| `Nexus.Intelligence.Architecture.Tests` | `BoundaryRuleTests.cs` | Nexus.Int | `Nexus.Intelligence.*` assemblies | Exists. Runs only when a developer remembers |
| `Nexus.Products.Chat.Architecture.Tests` | `BoundaryTests.cs` | Nexus.Web | `Nexus.Products.Chat.*` assemblies | Exists. Runs only when a developer remembers |

Two consequences follow, and both matter more than the file count:

**A NetArchTest suite can only assert about assemblies its own repository references.** That is why
no test today can assert a cross-repository rule in the forbidden direction — the very absence of
the reference is what makes it unassertable from inside. The rule is therefore held by whichever
repository *would* commit the violation, which is why each of the three repositories has its own
architecture test project rather than one central one.

**A test nobody runs is not enforcement.** `NexusAI\.github\workflows\` is empty and the other two
repositories have no `.github` directory at all. Until `M-08-1.4` wires NetArchTest into a pipeline
as a hard gate, every rule below is enforced by review.

### 7.1 Rule status

| Rule | Test today | Target |
|---|---|---|
| 1. Downward only | Partial — within three repositories | `M-08-1.4` gates it; per-layer coverage arrives with each layer |
| 2. Cross-cutting layers depend on nothing above CORE | **None** | Writable when `Nexus.Delivery.Contracts` exists. No milestone assigned |
| 3. No shared kernel | Partial, and **currently true on disk** | `M-08-1.4` |
| 4. Products never reference each other | **None** — one product exists | The second product; `BoundaryTests.cs` is the home |
| 5. AI never sees product structure | Partial — `BoundaryRuleTests.cs` is the home; true by construction | `M-08-1.4` |
| 6. No product branching | **None** | `M-12-1.2` platform-wide · `M-11-1.2` conversation core · `M-09-7.1` ASSURANCE |
| 7. Structure is not conversation | Half — the negative half only, via `M-11-1.2` | Review, per `CODE_REVIEW_CHECKLIST.md` |
| Schema ownership — a configuration writes only its layer's schema | **None** | `M-02-1.5` |
| Domain references no EF Core assembly | Partial | `M-08-1.4` |
| Value converters only in Infrastructure | Partial — `StronglyTypedIdConverters.cs` is correctly placed today | `M-08-1.4` |

---

## 8. When you need to go upward

You will. The need is real and the reference is still forbidden. Four legitimate shapes, in order of
preference:

| Shape | Use when | Example |
|---|---|---|
| **Invert the dependency** | The lower layer needs behaviour the upper layer has | EXPERIENCE publishes `IScopeResolver`; DEVELOPER implements it. The engine calls down into an interface it owns |
| **Flatten to a neutral type** | The lower layer needs *data*, not types | A consumer flattens `Milestone` into `ContextItem`s and hands AI a `ContextBundle`. AI never learns the type |
| **Polymorphic reference** | You need to point at a row in a layer you may not reference | ASSURANCE stores layer + type + id for what it verified, with no foreign key |
| **Emit an event** | The upper layer needs to know something happened | `PipelineCompleted` from DELIVERY; DEVELOPER consumes it. Neither references the other's implementation |

Two shapes that look like solutions and are not: a "shared" or "common" assembly that both sides
reference — that is a shared kernel by another name, banned by Rule 3; and a `switch` on product
identity in a shared layer — banned by Rule 6, and the reason capability packs exist.

---

## 9. What a reviewer checks

Five questions, in order. Any "yes" is a rejection regardless of what the change achieves.

1. Does this add a project reference that the §4 matrix marks `—` or `▲`?
2. Does a Contracts assembly now name a product, an experience or a development type?
3. Does an `IEntityTypeConfiguration` write to a schema its assembly does not own?
4. Is there a branch on product identity anywhere outside a product?
5. Does an AI type mention a consumer concept by name?

`CODE_REVIEW_CHECKLIST.md` owns the full review; these five are the architectural subset and they
are the ones that are cheap now and expensive later.

---

## 10. References

- `LAYER_MODEL.md` — what each layer is, and what it owns at capability level.
- `DATA_OWNERSHIP.md` — which layer owns which entity, and why an upward reference usually means a
  misplaced fact.
- `DATABASE_ARCHITECTURE.md` — the schema and database separation that makes product isolation
  physical.
- `DATABASE_STANDARDS.md` §5.4, §5.5 — cross-schema foreign keys and cross-product access.
- `ASSURANCE_STANDARDS.md` §5.4 — how architecture tests are written, qualified and evidenced.
- `DEVELOPMENT_WORKFLOW.md` §11 — these invariants as work-item acceptance conditions.
- `AI_DEVELOPMENT_STANDARDS.md` §5 — the context seam in detail.
- `CODE_REVIEW_CHECKLIST.md` — what a reviewer checks and in what order.
- `../nexus-roadmap.yaml` — each layer's declared `depends_on`.
