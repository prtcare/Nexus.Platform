# Dependency Rules

> **RE-DERIVATION COMPLETE (Rebaseline R06.1, 2026-09-07):** the numbering
> re-derivation flagged as deferred below (originally raised 2026-09-05,
> reconfirmed still-open through R06) is now done. Section 4 below is the
> **current v2.3 ten-layer dependency matrix**, derived from the same
> dependency-rule principles this document has carried since R02 (reasoning
> preserved, only the layer numbering and the Developer/Products-outside-
> numbered-Platform split are new). Section 5 preserves the prior v2.1/v2.2
> twelve-layer matrix, contested-cell table and forbidden-reference list
> as a **historical appendix, unedited except for its heading** — read it
> as v2.1/v2.2-numbering history, not as current fact.

> **SUPERSEDED NUMBERING NOTICE (2026-09-05, historical context for Section 5):** the
> appendix's dependency direction, ownership lines, and 12x12 matrix are built on the v2.1
> twelve-layer model, in which 07 DEVELOPER was a numbered Platform layer and DELIVERY/ASSURANCE/
> OPERATIONS/EXPERIENCE were numbered 08/09/10/11. Per the approved v2.2 renumbering
> (`LAYER_MODEL.md` §2.2, §4a), Nexus Forge and Nexus Developer (the product) sit
> OUTSIDE the ten numbered Platform layers, and DELIVERY/ASSURANCE/OPERATIONS/EXPERIENCE
> are renumbered 07/08/09/10. The dependency reasoning below (who may depend on whom,
> the cross-cutting exception, the AI seam, product isolation) remains historically
> accurate engineering judgment and is not being discarded.

> **SHARED PLATFORM RENAME NOTICE (Rebaseline R02, 2026-09-07):** row/column 06, previously
> labelled PRODUCT CORE throughout this document, is renamed SHARED PLATFORM below. This
> document was re-read specifically to determine whether the layer's broadened conceptual
> role (Shared Functions/UI/Tools/Product Services/Gateways/SDK, not just scope/entitlement
> primitives) requires any actual dependency-rule change. It does not: every `●`/`—`/`▲`/`○`
> cell for row and column 06 is unchanged, the forbidden-reference list for 06 is unchanged,
> and the "assembly placement does not bypass architectural dependency restrictions" principle
> this whole document exists to enforce applies identically under the new name. This is a
> terminology-only update — see `architecture/NEXUS_V2_REBASELINE_R01_REPORT.md` and
> `_R02_REPORT.md`.

**Status:** ACTIVE — the v2.3 ten-layer matrix (Section 4) is the current rule set; enforcement
exists in three test files that no pipeline runs, because there is no pipeline
**Owner:** Nexus Developer (product, outside numbered Platform) defines; ASSURANCE (Layer 08) proves;
DELIVERY (Layer 07) gates
**Last updated:** 2026-09-07 (R06.1)
**Layer:** cross-cutting
**Authoritative for:** what may depend on what — the layer dependency direction, the cross-cutting
exception, the no-shared-kernel rule, product isolation, the AI seam, the no-product-branching rule,
the current ten-layer dependency matrix, the forbidden-reference list per layer, and which rule has
a test today versus which is TARGET.

Not authoritative for: what each layer *is* (`LAYER_MODEL.md`), which layer owns an entity
(`DATA_OWNERSHIP.md`), how architecture tests are written and qualified
(`ASSURANCE_STANDARDS.md` §5.4), or when a work item may be worked on (`DEVELOPMENT_WORKFLOW.md`).

---

## 1. The sentence

> **A layer may depend only on layers below it. DELIVERY, ASSURANCE and OPERATIONS are cross-cutting
> and depend on nothing above CORE. Nothing shared ever references a product, and no product ever
> references another. No numbered Platform layer ever references Nexus Forge or a Product.**

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
because most layers do not exist. Most of the ten have no code. The rules exist now so that they
are built correctly rather than corrected later.

---

## 3. The seven rules

### Rule 1 — Downward only

A layer may reference only layers with a lower number. `01 CORE` references nothing. Numbering is a
label, not an authorization: a lower or higher number does not by itself permit a dependency in
either direction — the matrix in Section 4 is what grants or forbids a reference, never the number
alone.

**Enforced:** partially. `PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs` and `BoundaryTests.cs`
use NetArchTest and are the mechanism. They can only assert about assemblies their own repository
references, so today they cover the boundaries inside three repositories, not the full ten-layer
graph — which is unassertable while most layers have no assemblies to name.
**TARGET — `M-08-1.4` Branch protection and architecture gate** makes the existing tests a hard
pipeline gate. Coverage of the remaining layers arrives with each layer.

### Rule 2 — DELIVERY, ASSURANCE and OPERATIONS are cross-cutting

Anything may *record to* them. Nothing takes an assembly reference *on* them in order to do so.
Records reach them through a CORE-owned abstraction — logging, audit, events — or through their own
ingestion API. The one genuine consumer relationship is the Nexus Developer product reading
DELIVERY's build records to decide whether a work item is satisfied, and that is a declared
downward-style dependency from a product onto a numbered layer (§4a), not a layer-to-layer one.

**Enforced:** no. **TARGET** — the test becomes writable when the first of the three layers has an
assembly. There is no milestone that adds it; add one when `Nexus.Delivery.Contracts` is created.

### Rule 3 — No shared kernel

`Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never reference product types. There
is no "common" or "shared" assembly that products and the platform both extend. A type that two
products both need belongs to the lowest layer that legitimately owns it, or it is two types. A
neutral, genuinely cross-cutting primitive may belong to `01 CORE` — but only when evidence supports
that it is truly neutral (no product/consumer concept in its shape), never by default just because it
is shared by more than one caller.

**Enforced:** yes, this is the rule the three existing test files most plausibly cover, and it is
**currently true on disk**. `Nexus.Platform.Contracts` contains `Governance/`, `Identity/`,
`Models/`, `Secrets/` and `Tools/` and mentions no product concept; `Nexus.Intelligence.Contracts`
contains `Turns/`, `Context/`, `Results/` and `Client/` and mentions none either. Keep it true.

### Rule 4 — Products never reference each other

Chat cannot see Vault's types and vice versa. This is enforced **physically** by one database per
product: two products cannot share a foreign key if they cannot share a database. Data crosses
between products through the owning product's API, never through its database. Products (Nexus
Developer, Nexus Business OS, Nexus Trips, and any future product) sit **outside** the ten numbered
Platform layers as consumers of them — never the reverse, and never of each other (§4a).

**Enforced:** structurally, and untestable in a useful way today because exactly one product exists.
`BoundaryTests.cs` in `Nexus.Products.Chat.Architecture.Tests` is the natural home when a second
product arrives. **TARGET — the second product.**

### Rule 5 — AI never sees product structure

AI receives a `ContextBundle` of `ContextItem`s that a consumer flattened. `ScopeRef` is opaque to
it. AI returns an `IntelligenceTurnResponse` with citations and a `DecisionTrace`, and the consumer
resolves the citations back through its own IDs. `04 AI` (`Nexus.Intelligence`) is a *consumer* of
`01 CORE`'s provider/model infrastructure, never the reverse — CORE must not depend on AI. No
dependency cycle between AI and SHARED PLATFORM/a product is permitted in either direction.

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
product identity inside SHARED PLATFORM (06, renamed from PRODUCT CORE -- Rebaseline R02, terminology
only, no dependency-substance change; see architecture/NEXUS_V2_REBASELINE_R02_REPORT.md) and inside
ASSURANCE. `06 SHARED PLATFORM` may **compose** lower-layer (01–05) capabilities for its own internal
use where the matrix in §4 grants the reference — but it must never become a dependency shortcut
that lets a higher layer or a product reach a lower layer's capability *through* 06 in place of a
direct, properly declared reference of its own.

**Enforced:** no. **TARGET — `M-12-1.2` Capability pack composition** makes it a build failure
across the solution. **TARGET — `M-09-7.1` Profile definition and selection** adds the ASSURANCE
variant. `M-11-1.2` adds the EXPERIENCE variant: the conversation core fails the build if it
references any SHARED PLATFORM, Nexus Developer product, or other Product assembly. `10 EXPERIENCE`
does not own the Nexus Developer product's structured development-record data model (Feature,
Milestone, WorkItem, Task, and the rest) — that data model belongs to Nexus Developer, not to
EXPERIENCE, regardless of which layer renders it.

### Rule 7 — Structure is not conversation

When something can be modelled as structure or as conversation, model it as structure and let
conversation reference it. A milestone's dependency is a `Dependency` row that a conversation can
discuss — never a sentence in a transcript that has to be re-derived. This is a dependency rule
because the failure mode is a layer reaching into conversation storage for a fact it should own.

**Enforced:** partially, by Rule 6's EXPERIENCE variant (`M-11-1.2`). The positive half — that the
fact exists as a row somewhere — is a review question, not a test. `CODE_REVIEW_CHECKLIST.md`.

---

## 4. CURRENT V2.3 RULES — the ten-layer dependency matrix

Derived (not invented) from the principles this document has carried since R02 (Sections 1–3 above,
unchanged in substance), applied to the current fixed ten-numbered-layer architecture
(`01 CORE · 02 DATA · 03 GOVERNANCE · 04 AI · 05 AUTOMATION · 06 SHARED PLATFORM · 07 DELIVERY ·
08 ASSURANCE · 09 OPERATIONS · 10 EXPERIENCE`), plus the additional principles fixed by the human
authority decision of Rebaseline R06.1 (2026-09-07):

- **Type dependencies matter, not merely project references.** A project reference that happens to
  compile is not evidence a dependency is allowed — what matters is whether a *type* from a
  higher/forbidden layer is actually named, constructed or returned. A project reference alone never
  excuses a forbidden type-level dependency.
- **Assembly placement cannot bypass the rules.** Moving a type into a different assembly, or into a
  "shared"/"neutral" project, does not launder a dependency the matrix forbids — the matrix governs
  the *architectural* relationship, not where a file happens to sit.
- **Neutral cross-layer primitives may belong to `01 CORE`** when evidence supports genuine
  neutrality (Rule 3) — never by default merely because more than one layer wants to use it.
- **Numbering is not authority.** A lower or higher layer number never by itself authorizes a
  dependency in either direction (Rule 1). Only an explicit `●`/`○` cell below does.
- **Products are consumers, outside numbered Platform.** Nexus Developer, Nexus Business OS, Nexus
  Trips and future products may depend downward on numbered-Platform layers' public contracts (as
  any consumer does); no numbered Platform layer may ever depend on a Product.
- **Nexus Forge is outside runtime Platform** (`development_plane:`, not numbered Platform) and must
  never become a Platform runtime dependency — no numbered layer may depend on Forge, in either
  direction, at runtime.
- **06 SHARED PLATFORM composes, it does not shortcut.** It may reference 01–05 where the matrix
  grants it, to compose capability for its own area — it must not become a bypass route that lets a
  higher layer or a product reach a lower layer through 06 instead of its own direct reference.
- **10 EXPERIENCE does not own the Developer work graph.** Nexus Developer's structured
  development-record data model belongs to the Nexus Developer product, not to EXPERIENCE.
- **01 CORE must not depend on 04 AI or on any Product.** CORE is lower-level provider/model
  infrastructure; AI (`Nexus.Intelligence`) and every Product are consumers of CORE, never the
  reverse.
- **No AI ↔ Shared Platform/Product dependency cycles**, in either direction.

Legend (unchanged from the historical matrix): **●** may reference · **○** records flow this way
without an assembly reference (cross-cutting) · **—** forbidden. There are **no `▲` (contested)
cells among the ten numbered layers** in this derivation — every historical contested cell (§5.2)
involved the old numbered-07 Developer layer or the old numbered-12 Products layer, both of which
move outside numbered Platform under v2.3 (see §4a). Where the historical record does not give
enough basis to state a cell with confidence even after applying the principles above, it is marked
`REQUIRES DECISION` rather than guessed — see the note beneath the table.

| ↓ depends on → | 01 CORE | 02 DATA | 03 GOV | 04 AI | 05 AUTO | 06 SHARED PLATFORM | 07 DELIVERY | 08 ASSURANCE | 09 OPERATIONS | 10 EXPERIENCE |
|---|---|---|---|---|---|---|---|---|---|---|
| **01 CORE** | — | — | — | — | — | — | ○ | ○ | ○ | — |
| **02 DATA** | ● | — | — | — | — | — | ○ | ○ | ○ | — |
| **03 GOVERNANCE** | ● | ● | — | — | — | — | ○ | ○ | ○ | — |
| **04 AI** | ● | ● | — | — | — | — | ○ | ○ | ○ | — |
| **05 AUTOMATION** | ● | ● | ● | — | — | — | ○ | ○ | ○ | — |
| **06 SHARED PLATFORM** | ● | ● | ● | — | — | — | ○ | ○ | ○ | — |
| **07 DELIVERY** | ● | — | ● | — | — | — | — | ○ | ○ | — |
| **08 ASSURANCE** | ● | ● | ● | — | — | — | ○ | — | ○ | — |
| **09 OPERATIONS** | ● | — | — | — | — | — | ○ | ○ | — | — |
| **10 EXPERIENCE** | ● | ● | — | ● | — | ● | ○ | ○ | ○ | — |

Three properties, carried forward from the historical matrix and re-checked against this table:

1. **The upper triangle is empty of ●** except the cross-cutting columns (07/08/09) and 10's
   reference to 06. That is Rule 1.
2. **Row 01 has no ●.** CORE is the bottom and stays there — including no ● toward 04 AI, per the
   explicit principle above.
3. **06 SHARED PLATFORM's row is unchanged in substance from its v2.2 predecessor** (01–03 only,
   not 04/05) — re-confirmed, not re-decided, per the R02 rename notice above.

### 4a. Products and Nexus Forge — outside the numbered matrix

Neither Products nor Nexus Forge is a numbered Platform layer, so neither is a row or column in the
10×10 table above. Their relationship to every numbered layer is stated once, here, rather than as
repeated matrix cells:

| Relationship | Rule |
|---|---|
| Any numbered layer (01–10) → any Product (Nexus Developer, Nexus Business OS, Nexus Trips, future products) | **Forbidden, always.** No numbered Platform layer may depend on a Product. |
| Any numbered layer (01–10) → Nexus Forge | **Forbidden, always.** Forge is `development_plane:`, outside runtime Platform; no numbered layer may take a runtime dependency on it. |
| A Product → a numbered layer (01–10) | **Allowed downward**, on that layer's public contracts, the same shape any consumer takes — subject to Rules 1–7 above (no shared kernel, no product branching in the layer it calls into, etc.). |
| A Product → Nexus Forge | Outside this document's numbered-layer scope; Forge governs its own consumers under its own configuration (`config/development-control-map.json` et al.), not under this matrix. |
| Nexus Developer product → 07 DELIVERY | **Allowed**, downward, contracts-only — this is the direct successor of the historical 07→11/07→09 relationships (§5.2), now read as "a Product depends on a numbered layer," not "layer 07 depends on layer 11/09." |
| Nexus Developer product → 08 ASSURANCE | **`REQUIRES DECISION`** — carried forward from the historical contested cell (§5.2, "07 → 09"): whether ASSURANCE gates by responding to a Nexus Developer query, or by emitting a verdict Nexus Developer already has, was never resolved under the old numbering and the human authority decision that produced v2.3 did not resolve it either. Pending an ADR (the historical record names ADR-016 as the next number), do not write this reference. |
| Nexus Developer product → 10 EXPERIENCE | **Allowed**, contracts-only — carried forward from the historical "07 → 11 is resolved, not contested" finding (§4, historical note): Nexus Developer depends on `Nexus.Experience.Contracts` only, never `.Core`; EXPERIENCE discovers the implementation through DI and calls down. |

---

## 5. HISTORICAL V2.2 MATRIX — appendix (v2.1/v2.2 numbering, superseded by Section 4)

**Everything in this Section 5 (5, 5.1, 5.2) is preserved unedited from the pre-R06.1 document**,
except this heading and the historical sub-headings that now say "(v2.2 numbering)" for clarity. Do
not use it as a current source — Section 4 above is current. It is kept because the underlying
engineering reasoning (who may depend on whom, the cross-cutting exception, the AI seam, product
isolation) remains historically accurate judgment, and because it documents the contested cells and
open questions Section 4 inherited and, in one case (Nexus Developer → ASSURANCE), still carries
forward as unresolved.

### 5.1 The allowed-dependency matrix (v2.1/v2.2 numbering)

Rows depend on columns. Read a row as "what may this layer reference".

- **●** may reference
- **○** records flow this way without an assembly reference (cross-cutting)

> **07 → 11 is resolved, not contested.** `NEXUS_MASTER_ARCHITECTURE.md` §4.2.1: DEVELOPER depends on `Nexus.Experience.Contracts` **only**, never `.Core`. Contracts assemblies depend on nothing, so this creates no upward coupling — EXPERIENCE defines `IScopeResolver`, DEVELOPER implements it, and EXPERIENCE discovers it through DI and calls *down*. A DEVELOPER reference to `Nexus.Experience.Core` is a defect. The other contested cells below (12 → 07, 12 → 09, 07 → 09) remain open pending ADR-016.

- **▲** contested or undeclared — see §5.2
- **—** forbidden

| ↓ depends on → | 01 CORE | 02 DATA | 03 GOV | 04 AI | 05 AUTO | 06 PROD CORE | 07 DEV | 08 DELIVERY | 09 ASSUR | 10 OPS | 11 EXP | 12 PRODUCTS |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **01 CORE** | — | — | — | — | — | — | — | ○ | ○ | ○ | — | — |
| **02 DATA** | ● | — | — | — | — | — | — | ○ | ○ | ○ | — | — |
| **03 GOVERNANCE** | ● | ● | — | — | — | — | — | ○ | ○ | ○ | — | — |
| **04 AI** | ● | ● | — | — | — | — | — | ○ | ○ | ○ | — | — |
| **05 AUTOMATION** | ● | ● | ● | — | — | — | — | ○ | ○ | ○ | — | — |
| **06 SHARED PLATFORM** | ● | ● | ● | — | — | — | — | ○ | ○ | ○ | — | — |
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

### 5.2 The contested cells (v2.2 numbering)

These are not defects to route around. They are decisions that have not been made, and the matrix
marks them ▲ rather than guessing.

| Cell | The situation | What would decide it |
|---|---|---|
| **07 → 11** — DEVELOPER on EXPERIENCE | `nexus-roadmap.yaml` declares `07 depends_on: [… 11]`; `NEXUS_MASTER_ARCHITECTURE.md` §7.7 does not. DEVELOPER implements `IScopeResolver`, which EXPERIENCE publishes — implementing it needs a reference to whichever assembly holds it. The clean shapes are: the contract lives in `Nexus.Experience.Contracts` and DEVELOPER references only that; or the contract lives lower and both reference it; or registration happens in the composition root and neither references the other | An ADR, before `M-07-6.1` Scope resolver for the work graph. The next ADR number is ADR-016. **Resolved as of §4/§4a: Nexus Developer → EXPERIENCE, contracts-only.** |
| **07 → 09** — DEVELOPER on ASSURANCE | Neither source declares it, yet `M-09-1.3` Quality gate V1 blocks integration while a mandatory criterion is unverified, and integration is DEVELOPER's `IntegrationRun`. Something has to consult something | `M-09-1.3`. Decide whether ASSURANCE gates by responding to a DEVELOPER query, or by emitting a verdict DEVELOPER already has. **Still open as of §4a: Nexus Developer → 08 ASSURANCE is `REQUIRES DECISION`.** |
| **12 → 07** — PRODUCTS on DEVELOPER | Declared in the roadmap's `depends_on` for layer 12. A product referencing the work graph is surprising: DEVELOPER builds products, products do not read their own build state | Worth challenging before the first new product. If it is real, it is the DEVELOPER *contracts* only and should say so |
| **12 → 09** — PRODUCTS on ASSURANCE | The roadmap's layer 12 `depends_on` omits 09, which reads as an oversight from ASSURANCE's late insertion rather than a decision | Correct the roadmap, or record why a product is exempt |

Until each is decided, **do not write the reference**. An undeclared dependency that ships is a
decision made by whoever was typing.

### 5.3 Forbidden references, per layer (v2.2 numbering)

Assembly-level statements. Each is the thing a NetArchTest rule for that layer should assert.

| Layer | Must not reference |
|---|---|
| **01 CORE** | Anything. `Nexus.Platform.Contracts` and `.Core` reference no other Nexus layer, no `Nexus.Products.*`, no `Nexus.Intelligence.*`, no EF Core in Contracts |
| **02 DATA** | Any layer above 01 · any `Nexus.Products.*` · any product `DbContext`. Its migration discipline is used *by* products; it does not know them |
| **03 GOVERNANCE** | 04–12 · any product type. `Product` here is a registry row, never a product's domain type |
| **04 AI** | Any `Nexus.Products.*` · `Nexus.Experience.*` · `Nexus.Developer.*` · 03, 05–12 generally. **The hard one:** no AI type may name `Workspace`, `Project`, `Milestone`, `WorkItem` or any other consumer concept |
| **05 AUTOMATION** | 04 and above · any product type. It runs a process; it does not know what the process means |
| **06 SHARED PLATFORM** | 07–12 · any product type · **any branch on product identity**, tested rather than reviewed |
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

## 6. Enforcement inventory

**The honest position: three test files exist, and nothing runs them.**

| Test project | File | Repository | What it can see | Status |
|---|---|---|---|---|
| `Nexus.Platform.Architecture.Tests` | `PlatformBoundaryTests.cs` | Nexus.Platform | `Nexus.Platform.*` assemblies | Exists. Runs only when a developer remembers |
| `Nexus.Intelligence.Architecture.Tests` | `BoundaryRuleTests.cs` | Nexus.Intelligence | `Nexus.Intelligence.*` assemblies | Exists. Runs only when a developer remembers |
| `Nexus.Products.Chat.Architecture.Tests` | `BoundaryTests.cs` | Nexus.Experience | `Nexus.Products.Chat.*` assemblies | Exists. Runs only when a developer remembers |

Two consequences follow, and both matter more than the file count:

**A NetArchTest suite can only assert about assemblies its own repository references.** That is why
no test today can assert a cross-repository rule in the forbidden direction — the very absence of
the reference is what makes it unassertable from inside. The rule is therefore held by whichever
repository *would* commit the violation, which is why each of the three repositories has its own
architecture test project rather than one central one.

**A test nobody runs is not enforcement.** `Nexus.Platform\.github\workflows\` is empty and the other two
repositories have no `.github` directory at all. Until `M-08-1.4` wires NetArchTest into a pipeline
as a hard gate, every rule below is enforced by review.

### 6.1 Rule status

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

## 7. When you need to go upward

You will. The need is real and the reference is still forbidden. Four legitimate shapes, in order of
preference:

| Shape | Use when | Example |
|---|---|---|
| **Invert the dependency** | The lower layer needs behaviour the upper layer has | EXPERIENCE publishes `IScopeResolver`; the Nexus Developer product implements it. The engine calls down into an interface it owns |
| **Flatten to a neutral type** | The lower layer needs *data*, not types | A consumer flattens `Milestone` into `ContextItem`s and hands AI a `ContextBundle`. AI never learns the type |
| **Polymorphic reference** | You need to point at a row in a layer you may not reference | ASSURANCE stores layer + type + id for what it verified, with no foreign key |
| **Emit an event** | The upper layer needs to know something happened | `PipelineCompleted` from DELIVERY; the Nexus Developer product consumes it. Neither references the other's implementation |

Two shapes that look like solutions and are not: a "shared" or "common" assembly that both sides
reference — that is a shared kernel by another name, banned by Rule 3; and a `switch` on product
identity in a shared layer — banned by Rule 6, and the reason capability packs exist.

---

## 8. What a reviewer checks

Five questions, in order. Any "yes" is a rejection regardless of what the change achieves.

1. Does this add a project reference that the §4 matrix marks `—`, or that §4a marks forbidden
   (any numbered layer toward a Product or toward Forge)?
2. Does a Contracts assembly now name a product, an experience or a development type?
3. Does an `IEntityTypeConfiguration` write to a schema its assembly does not own?
4. Is there a branch on product identity anywhere outside a product?
5. Does an AI type mention a consumer concept by name?

`CODE_REVIEW_CHECKLIST.md` owns the full review; these five are the architectural subset and they
are the ones that are cheap now and expensive later.

---

## 9. References

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
- `../nexus-roadmap.yaml` — each layer's declared `depends_on` (historical, v2.2-numbering — not yet
  re-derived to v2.3 in the roadmap YAML itself; see `DOCUMENTATION_INDEX.md` §0).
