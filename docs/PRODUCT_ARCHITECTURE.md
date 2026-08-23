# Product Architecture

**Status:** TARGET — **one product exists (Chat, in `Nexus.Web`) and it predates this shape.** No
`Product` record exists anywhere, no capability pack is declared, and no product state model exists.
Each gap names the milestone that closes it
**Owner:** PRODUCTS (Layer 12), with GOVERNANCE (03) owning registration and PRODUCT CORE (06) owning
the reusable half
**Last updated:** 2026-08-21
**Layer:** 12 PRODUCTS
**Authoritative for:** the **shape** of a product — its five structural parts, what each part owns,
how capability is composed by declaration rather than construction, why the same foundation serves an
ERP, a vault, a travel app, a game and a machine application without any shared layer branching on
product identity, and the eight-dimension product state model.

Not authoritative for: the **procedure** for standing one up — `PRODUCT_DEVELOPMENT_GUIDE.md` owns the
ordered twenty-two-step checklist, the five profiles as steps, and the anti-pattern catalogue; how to
add a module or endpoint once it exists — `NEW_MODULE_GUIDE.md`; where the repository sits —
`REPOSITORY_STRUCTURE.md`; database mechanics — `DATABASE_STANDARDS.md`; the machine-specific form —
`MACHINE_DEVELOPMENT_GUIDE.md`.

**The split in one line:** that document is the path; this one is the destination's shape.

---

## 1. The proposition

> A product is a **composition of declared capability plus its own domain**. Standing up the second
> product should cost a fraction of the first, and standing up the fifth should change nothing below
> layer 12.

The test of whether that claim is real is not how quickly a product is built. It is whether adding
one requires editing anything underneath it. **`M-12-1.1`'s governing acceptance criterion is exactly
that: no step requires modifying a layer below 12.**

---

## 2. The standard shape

Every product has the same five parts. Four of them are configuration of layers below; only one is
code the product writes.

```
Nexus.Products.<Name>
│
├─ 1  IDENTITY               GOVERNANCE (03)      a Product row. Not code
│                            id · name · slug · owner · classification · lifecycle
│
├─ 2  PRODUCT CORE           PRODUCT CORE (06)    configuration, not code
│                            membership · profile · roles · plan · entitlement
│                            settings · onboarding · registered scope kinds
│
├─ 3  CAPABILITY             declared packs       data, not code
│     INTEGRATIONS           which of layers 01–11 this product consumes
│
├─ 4  DOMAIN MODULES         PRODUCTS (12)        ← THE ONLY CODE THE PRODUCT OWNS
│     .Domain  .Application  .Infrastructure  .Api  .Client
│     aggregates · endpoints · its own business rules
│
└─ 5  PRODUCT DATABASE       its own database     Nexus<Name>, never a platform schema
```

| Part | Owned by | Form | Milestone |
|---|---|---|---|
| 1 Identity | GOVERNANCE (03) | A registry row | `M-03-1.1`, `M-03-1.3` |
| 2 Product Core | PRODUCT CORE (06) | Configuration + registrations | `M-06-1.2`, `M-06-2.1`, `M-06-5.1` |
| 3 Capability integrations | PRODUCTS (12) | **Declared packs** | `M-12-1.2` |
| 4 Domain modules | PRODUCTS (12) | **Code** | — the product's own work |
| 5 Product database | PRODUCTS (12) | Its own database | — per `DATABASE_STANDARDS.md` |

**The split that carries the whole design.** Authentication, membership, subscriptions, entitlements,
onboarding, settings and the scope hierarchy come from layers 01 and 06 and are **never duplicated per
product**. A product owns its domain and nothing else.

---

## 3. Part 1 — Identity in GOVERNANCE

GOVERNANCE says a product **exists and is ours**. DEVELOPER says what is being **built** in it. Two
registries, deliberately separate, joined by a `ProductId` that GOVERNANCE issues and DEVELOPER holds.

| Property | |
|---|---|
| The minimum that must exist first | **DEVELOPER's work graph hangs off a `ProductId`.** A product with no id cannot have work planned against it, cannot have releases, and cannot have evidence attributed to it |
| Uniqueness | A duplicate slug in the same tenant returns a conflict, not a second row — `M-03-1.1` |
| Tenanted | Every `Product` row carries the enforced `TenantId`, with a filter test proving cross-tenant reads return empty |
| Lifecycle is guarded | Retired → Active is rejected; Active → Sunsetting succeeds. Every accepted transition writes one audit entry with actor, from-state, to-state and timestamp — `M-03-1.3` |
| Classification drives scheduling | Consumer products are **planned and scheduled**; internal business systems are **pulled** when the business needs them, and their phase means *eligible from*, not *scheduled for* |

**CURRENT.** `IProductRegistry` exists as an interface in `Nexus.Platform.Contracts/Identity/`, in the
wrong layer, with no implementation. `M-03-1.2` relocates it to GOVERNANCE; its acceptance criterion
is that no `IProductRegistry.cs` remains under that path. Registration today is a documentation act.

---

## 4. Part 2 — Product Core

Product Core is the product's identity **inside** the platform. It is layer 06 capability the product
configures, not code the product writes.

| Concern | Milestone |
|---|---|
| Profile and membership — one Nexus identity, many product contexts | `M-06-2.1` |
| Product-scoped roles — admin in one product, reader in another | `M-06-2.2` |
| Plans and subscriptions | `M-06-3.1` |
| Entitlements and feature access | `M-06-3.2` |
| Quotas | `M-06-4.1` |
| Scoped settings — member overrides workspace overrides product default | `M-06-5.1` |
| Onboarding state, resumable after sign-in | `M-06-6.1` |
| **Registered scope kinds** | **`M-06-1.2`** |

**The scope hierarchy is an extension point, not a fixed shape.** PRODUCT CORE owns
`Workspace → Project → Subproject` and nothing below it. Every consumer registers its own kinds
underneath:

```
Workspace → Project → Subproject          layer 06 — fixed, three kinds, owned by nobody else
                          ├── DEVELOPER    Release → Milestone → Feature → WorkItem → Task → Subtask
                          ├── a plain conversation      stops at Project
                          └── a machine product         Machine → Configuration → Characteristic → Measurement
```

`M-06-1.2`'s acceptance criterion is the one that proves the design: **a machine-domain consumer
registers an entirely different hierarchy without a code change in Layer 06**, and *no
if-product-equals branching exists anywhere in Layer 06.*

**The test that Product Core stayed separate from CORE identity:** removing a product membership must
not affect the Nexus identity — `M-06-2.1`. If it does, layer 06 has absorbed layer 01's job.

---

## 5. Part 3 — Capability integrations, declared

A product declares which of layers 01–11 it consumes. **The declaration is data, and the layer being
consumed never learns which product declared it.**

That sentence is the whole mechanism. What it means operationally:

| The layer reads | The layer never reads |
|---|---|
| "This scope kind is registered, with this resolver" | "This is Vault" |
| "This assurance profile requires Inspection and Validation" | "Machine products need inspection" |
| "This entitlement is absent for this subscription" | "Free plans hide this feature" |
| "This capability pack includes Offline Sync" | "Trips is offline-capable" |

The left column is a lookup. The right column is a branch, and every entry in it is one line away from
being `if (Product == X)`.

**A warning against premature packs.** Define a pack when a **second** product actually needs the same
capability. A pack designed around one product is that product's implementation with a general-sounding
name — which is why `M-12-1.2` is P3 rather than earlier.

---

## 6. Part 4 — Domain modules

The only code a product owns.

| Rule | |
|---|---|
| Projects | `.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Client`. **Not every product has all five** — a product with no UI has no `.Client`, and that absence requires no change anywhere below layer 12 |
| Aggregates | One folder each: `<Name>.cs`, `<Name>Id.cs`, `<Name>Status.cs`, `I<Name>Repository.cs`. `.Domain` never references `.Infrastructure` |
| One registration point | `<Name>ProductModule.cs`, following `ChatProductModule.cs`, called once from `Program.cs` |
| Never reference another product | Not by project, not by database, not by shared type |
| Conversational surface, if any | Supply a `ContextBundle` mapper flattening aggregates into `ContextItem`s. `ChatContextBundleMapper` is the reference — and one of only two things in the system with a behaviour test |

**`ScopeRef` stays opaque to AI.** The mapper is the boundary, and the boundary is the point. A
product's structure never reaches the AI layer.

---

## 7. Part 5 — The product database

**One database per product.** Layers 01–11 share `NexusPlatform` with a schema each; every product
gets its own database. Three independent reasons, any one sufficient: **lifecycle** (product data is
created, retained and deleted on the product's schedule), **residency and retention** (an obligation
registered against one product must be satisfiable without touching another's data), and
**removability** (a product whose tables are interleaved with the platform's cannot be removed, only
abandoned).

The architectural consequence matters more than the reasons: **product isolation stops being a
convention and becomes a fact.** Two products cannot share a foreign key because they cannot share a
database. Schemas *inside* a product database are the product's own — never reuse a layer schema
name, and never use `org`, the legacy schema of the single existing migration.

---

## 8. Five products, one foundation

The claim under test: an ERP, a vault, a travel app, a game and a machine application compose from the
same layers, and **no shared layer contains `if (Product == X)`**.

Base packs every product declares — `Identity`, `Tenancy`, `Authorization`, `Audit`, `Secrets` (01);
`Registry` (03); `Membership`, `Settings` (06); `Pipelines` (08); `Assurance` (09); `Telemetry` (10) —
are omitted below. Only what **differs** is shown, which is the point.

| Pack | **ERP** `F-12-8` | **Vault** `F-12-2` | **Trips** `F-12-3` | **Game** `F-12-7` | **Machine** `F-12-15` |
|---|---|---|---|---|---|
| `Web` (11) | ● | ● | ● | — own client | ● operator UI |
| `Mobile` | — | ● | ● | ● | — |
| `Desktop` | — | ● | — | ● | ● on-site |
| `OfflineSync` | — | ● | ● | — | ● intermittent link |
| `Conversation` (11) | ● | ● | — | — | ● |
| `DesignSystem` (11) | ● | ● | ● | **—** | ● |
| `Commands` (11) | ● | ● | — | — | — |
| `Documents` (02) | ● | ● | ○ receipts | — | ● drawings, procedures |
| `Knowledge` (02) | ● | — | — | — | ● fault history |
| `Search` (02) | ● | ● | ○ | — | ● |
| `AI` (04) | ● | ● | ○ | — | ● **propose and diagnose only** |
| `Agents` (04) | ● | — | — | — | ○ |
| `Memory` (04) | ● | — | — | — | — |
| `Workflow` (05) | **● dominant** | — | — | — | ● maintenance |
| `Jobs` (05) | ● | ● sync | ● | ● | ● |
| `Approvals` (05) | **● dominant** | — | — | — | **● mandatory** |
| `Subscriptions` (06) | — internal | ● | ● | ● | — |
| `Entitlements` (06) | ○ by role | ● | ● | ● | — |
| `Quotas` (06) | ○ | ● storage | — | ● | — |
| `Onboarding` (06) | ● | ● | ● | ● | ○ |
| `Compliance` (03) | ● | ● PII | ○ | ○ | **● safety obligations** |
| `Licence` (03) | ○ | ○ | ○ | **● engine, assets** | ● |
| `Brand` (03) | — | ● | ● | ● | ○ |
| `Environments` (08) | ● | ● | ● | ● | ● |
| `Backup` (08) | ● | **● critical** | ● | ● | ● |
| `Cost` (10) | ● | ● | ● | ● | ○ |
| **Assurance profile** | **ERP** | Software + Consumer | Software + Consumer | Consumer | **Machine + safety carve-out** |

● declared · ○ minimal · — not declared

**Vault's composition is the roadmap's own, verbatim.** `M-07-8.3`: *Vault equals Web plus Mobile plus
Desktop plus Documents plus AI plus Security plus Offline Sync, **declared not coded***, with a second
criterion that *an architecture test fails any branch on product name across the whole solution.*

### 8.1 What each column proves

**ERP** proves that a system with almost no consumer surface uses the same shape. Its distinctive
weight is in `Workflow` and `Approvals`, and its assurance profile makes **validation dominate
verification**: an ERP module whose every test passes and whose process does not match how the business
actually works is a failed module. Its evidence is `Attestation` and `Document` far more than
`PipelineRun`. It is also the recommended first business system — `F-12-8`, eligible immediately at
GATE A.

**Vault** proves that a heavily client-side product adds capability without adding platform code.
`Mobile`, `Desktop` and `OfflineSync` are three declarations; none of them is a case in a shared layer.

**Trips** proves the negative half. It declares no `Conversation` and minimal `AI`, and those absences
cost nothing anywhere: a product with no reasoning surface registers no scope resolver and supplies no
`ContextBundle` mapper. **Neither absence requires a change below layer 12.**

**The game is the row most likely to be got wrong.** An unfamiliar stack makes the platform feel
irrelevant and the product gets built outside the model. What is different is the **stack profile**,
not the governance. A game still needs a `Product` record, an owner, a licence position — engine and
asset licensing is the one place its `Licence` needs exceed everyone else's — a technology usage
record, and an assurance profile. It declares almost nothing from EXPERIENCE because it has its own
client entirely; declaring nothing from a layer is a supported composition, not an escape.

**The machine application** proves the hardest case, and it proves it by what the packs **cannot**
grant. `AI` here means plan, diagnose, document and propose parameters — nothing else. **No capability
pack grants control authority.** Deterministic controllers own real-time motion, interlocks, emergency
stop and hard limits; no Nexus component is ever in that loop; and no agent may create, modify or waive
a safety-critical acceptance criterion (`M-09-7.2`, absolute, no exception path).
`INTEGRATION_ARCHITECTURE.md` §10 owns the boundary; `MACHINE_DEVELOPMENT_GUIDE.md` §1 owns the full
division of authority.

### 8.2 What is identical across all five

Registration, `ProductId`, tenancy, authorization, membership, its own database, the scope trunk, the
declaration mechanism, the eight state dimensions, and the rule that nothing below layer 12 changes
when any of them is added.

---

## 9. Why there is no `if (Product == X)`

`PRODUCT_DEVELOPMENT_GUIDE.md` §3 owns the anti-pattern and the substitution table. The architectural
half — *what makes the substitution possible at all*:

**A shared layer never receives product identity as an input to a decision.** It receives a
declaration, a registration or a profile, and those are data it looks up. The product's *name* reaches
GOVERNANCE, appears in a `ProductId` foreign key, and goes no further into any decision path.

| Variation needed | Mechanism | Milestone |
|---|---|---|
| A different capability set | Capability pack | `M-12-1.2` |
| A different hierarchy of things | `ScopeKindRegistration` + `IScopeResolver` | `M-06-1.2`, `M-11-2.1` |
| Different behaviour in a shared algorithm | An injected strategy the product registers | — |
| Different proof requirements | Assurance profile | `M-09-7.1` |
| A different value | Scoped setting | `M-06-5.1` |
| A genuinely universal capability | It belongs in layer 06, implemented once, for everyone | — |

If none fits, **the requirement is telling you the boundary is in the wrong place. Move the boundary;
do not add the branch.**

**Three layers state the ban independently**, because it is the rule most likely to be broken by a
small, reasonable-looking change:

| Milestone | Scope of the test |
|---|---|
| **`M-12-1.2`** | *An architecture test fails any branch on product identity **across the whole solution*** |
| `M-06-1.2` | No if-product-equals branching anywhere in Layer 06 |
| `M-09-7.1` | An architecture test forbids branching on product identity inside ASSURANCE |
| `M-11-1.2` | The conversation core fails the build if it references any layer 06, 07 or 12 assembly |
| `M-07-8.3` | An architecture test fails any branch on product name across the whole solution |

**Why it is fatal rather than untidy.** The first `if (Product == Vault)` is one line in one shared
service and it works. Then a second. Then a shared service that cannot be understood without knowing
every product. Then a change to Vault that breaks Trips. At that point the layers below 12 are one
application with several personalities, and no refactoring recovers the boundary cheaply.

**Enforcement is TARGET.** `M-12-1.2` is P3 and no test exists today. Until then the rule is held by
review — `DEPENDENCY_RULES.md` §9, question 4.

---

## 10. Release lifecycle, and the three profiles that shape operation

`PRODUCT_DEVELOPMENT_GUIDE.md` §8 owns the five profiles as steps. Their architectural consequences:

**Release lifecycle.** Maturity runs **Idea through End of Life** and **never Dev/Test/Prod** —
`M-07-7.2`. Maturity and environment are orthogonal (`M-08-4.1`), which is what allows *a Beta release
recorded as running in Production* without contradiction. Release qualification is `M-09-5.1`, derived
from the criteria in its scope and **never entered by hand**, and it blocks Production promotion when
unmet.

**Assurance profile.** A declaration selecting which verification methods are *mandatory*, never which
are possible. Software → unit, integration, contract, architecture. AI → evaluation and citation
checking, criteria carrying **minimum scores** rather than binary outcomes. ERP → demonstration,
process validation, user acceptance. Machine → inspection with characteristics, tolerances and units,
plus validation and the safety carve-out. A product may hold more than one.

**Deployment profile.** Which environments exist, how promotion works, and backup and restore
requirements. The artifact is **built once and promoted, never rebuilt per environment**, and
production promotion requires a recorded human approval.
**Operations profile.** What the product emits and what "healthy" means for it: correlation on every
request (`M-10-1.1`), health checks (`M-10-2.1`), metrics and tracing (`M-10-2.2`), deployment health
attributed to a deployment record (`M-10-2.3`), cost attribution (`M-10-4.1`). An incident may
**produce** a DEVELOPER work item (`M-10-3.2`); it may never hold development state.

> **TARGET, entirely, for deployment and operations.** There are no environments, no pipelines, no
> infrastructure as code and no deployment of any kind. A deployment profile written before
> `M-08-4.1` is a wish list — write it anyway, but do not schedule work against it.

---

## 11. The eight-dimension product state model — `M-12-1.3`

Product state is **eight independent dimensions, not one status field**, and each is marked derived or
manual with every derived one **computed, never entered**.

| # | Dimension | Answers | Derived / manual | Computed from | Owner | Milestone |
|---|---|---|---|---|---|---|
| 1 | `ProductLifecycleState` | Proposed, Active, Sunsetting, Retired? | **MANUAL** | A human decision, transition-guarded and audited | GOVERNANCE 03 | `M-03-1.3` |
| 2 | `DevelopmentStage` | How mature is what is being built? | **DERIVED** | The maturity of its most advanced release | DEVELOPER 07 | `M-07-7.2` |
| 3 | `CurrentRelease` | What is the latest release? | **DERIVED** | The release records | DEVELOPER 07 | `M-07-7.2` |
| 4 | `CurrentProductionRelease` | What is actually in production? | **DERIVED** | Deployment records for the Production environment | DELIVERY 08 | `M-08-5.1` |
| 5 | `DevelopmentHealth` | Is the work going well? | **DERIVED** | Progress, blocked items, failing builds, awaiting review | DEVELOPER 07 | `M-07-5.2` |
| 6 | `DeploymentState` | Is it deployed, and where? | **DERIVED** | Deployment records per environment | DELIVERY 08 | `M-08-5.1` |
| 7 | `OperationalHealth` | Is the running system healthy? | **DERIVED** | Health checks and deployment health | OPERATIONS 10 | `M-10-2.1`, `M-10-2.3` |
| 8 | `ComplianceState` | Are its registered obligations satisfied? | **DERIVED** | Obligation links, attestations, residency declaration | GOVERNANCE 03 | `M-03-4.1`, `M-03-4.2` |

**Seven of eight are derived, and that ratio is the design.** Exactly one dimension is a human
judgement — whether the business intends this product to exist and in what posture. Everything else is
an observation, and an observation someone types is an observation that will be wrong within a week.

**The failure this prevents.** A product can be actively developed, healthy in development, deployed
to no environment and non-compliant **all at once**. A single "status" field forces a lie, and the lie
is always the reassuring one.

**Derivation rules that come from the owning layers and apply here unchanged:** a parent not marked
`BreakdownComplete` reports *not estimable* rather than a percentage; `Blocked` is derived from unmet
blocking dependencies and never entered by hand; a manual override records **who set it and why**
(`M-07-5.2`). A derived dimension with a silent manual override is a manual dimension wearing a
derived label.

**Derived/manual assignment status.** `M-12-1.3` requires that each dimension *be marked* derived or
manual; the roadmap does not itself state which. The assignment above is this document's, argued from
each dimension's owning layer, and it is **settled here unless an ADR revises it** — the next ADR
number is **ADR-016**.

---

## 12. What must never happen

| Never | Why |
|---|---|
| `if (Product == X)` in any layer, including a test helper | §9. `M-12-1.2` makes it a build failure |
| A product type in `Nexus.Platform.Contracts` or `Nexus.Intelligence.Contracts` | No shared kernel |
| A product referencing another product | Not by project, database or endpoint |
| A platform table copied into a product database | The boundary being broken, not an optimisation |
| A product table inside `NexusPlatform` | Including "just this one lookup table" |
| A product-specific field on a layer 01–11 table | The product owns its own data |
| A capability duplicated per product | Authentication, membership, subscriptions and settings come from 01 and 06 |
| Skipping GOVERNANCE registration for a prototype | Shadow IT with an internal sponsor |
| An unapproved technology without an ADR and a `TECHNOLOGY_STACK.md` entry in the same PR | A product does not choose its stack freely |
| A product's structure reaching AI | `ScopeRef` is opaque; the mapper is the boundary |
| **An agent creating or waiving a safety-critical criterion** | Absolute, no exception path — `M-09-7.2` |
| **A capability pack granting control authority over a machine** | No pack grants it, because none can — §8.1 |

---

## 13. Current reality

**One product exists, and most of what it holds is not its.** `Nexus.Products.Chat` in `Nexus.Web`
carries eleven aggregates: `Workspace` and `Project` belong to PRODUCT CORE (`M-06-1.1`);
`Conversation` and `ConversationMessage` belong to EXPERIENCE (`M-11-1.1`); `WorkItem` belongs to
DEVELOPER; `Branch` and `Snapshot` belong to DELIVERY; `Adr`, `Knowledge` and `Artifact` belong to
DATA. `DATA_OWNERSHIP.md` §7.1 carries the full migration matrix.

**It is a reference, not a template.** Worth copying: the aggregate folder shape, the
`<Name>Endpoint.cs` pattern, `<Name>ProductModule.cs`, `ChatContextBundleMapper`, the Id/Seq/Ref
migration proven on 2026-08-20. Not worth copying: the repository shape, and the layer
responsibilities it absorbed because no layer existed to hold them.

**Whether Chat is retrofitted onto this shape or grandfathered is not yet decided.** What is decided:
a standalone end-user chat application, if ever released, is a **PRODUCT at layer 12 consuming
EXPERIENCE**. The universal conversation engine is never called a Chat product.

**The one step to not skip while the rest is unavailable:** add architecture tests for the product's
boundaries. It is the only one of today's nine possible steps that keeps the other thirteen achievable
later — a product built now without boundary tests will have absorbed platform responsibilities by the
time the platform is ready to provide them.

---

## 14. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Which product is second | Business need. Internal systems are pulled, not scheduled; `F-12-8` ERP is the recommended first | **Not yet decided** |
| What the standard capability packs actually are | A second product needing the same capability | **Deliberately deferred — `M-12-1.2`.** The §8 vocabulary is illustrative, not ratified |
| Database naming convention across products | The second database | Not yet decided |
| Whether Chat is retrofitted or grandfathered | The `Nexus.Web` split | Not yet decided |
| Whether 12 → 07 DEVELOPER is a real dependency | It is declared in the roadmap and reads as surprising — a product reading its own build state | **Challenge before the first new product** — `DEPENDENCY_RULES.md` §5 |

---

## 15. References

- **`PRODUCT_DEVELOPMENT_GUIDE.md`** — the ordered path from registration to running, the five
  profiles as steps, and the anti-pattern catalogue. This document is the shape; that one is the path.
- `BOUNDED_CONTEXTS.md` §15 — layer 12's four contexts and their boundaries.
- `DEPENDENCY_RULES.md` — Rules 3, 4 and 6, and the contested 12 → 07 and 12 → 09 cells.
- `DATABASE_ARCHITECTURE.md` §§3, 6 — one database per product, and cross-product access.
- `DATA_OWNERSHIP.md` §7.1 — where each of Chat's eleven aggregates is going.
- `INTEGRATION_ARCHITECTURE.md` §4, §10 — product-to-layer integration and the machine boundary.
- `SECURITY_ARCHITECTURE.md` §11 — product isolation as a security boundary.
- `ASSURANCE_ARCHITECTURE.md` §11 — assurance profiles in full.
- `MACHINE_DEVELOPMENT_GUIDE.md` — the machine product form, and the safety rules that bound it.
- `NEW_MODULE_GUIDE.md`, `REPOSITORY_STRUCTURE.md`, `DATABASE_STANDARDS.md` — the mechanics inside
  part 4 and part 5.
- `../nexus-roadmap.yaml` — `F-12-2`, `F-12-3`, `F-12-7`, `F-12-8`, `F-12-15`; `M-12-1.1`, `M-12-1.2`,
  `M-12-1.3`, `M-07-8.3`.
