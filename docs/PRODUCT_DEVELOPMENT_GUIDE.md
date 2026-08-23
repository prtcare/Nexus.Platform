# Product Development Guide

**Status:** TARGET — one product exists (Chat) and it predates this path; every step below is
composition rather than construction, and the milestone that makes each step real is named
**Owner:** PRODUCTS (Layer 12), with GOVERNANCE (03) owning registration and PRODUCT CORE (06)
owning the reusable half
**Last updated:** 2026-08-21
**Layer:** 12 PRODUCTS
**Authoritative for:** how a new product — Vault, Trips, an ERP module, a game, a machine
application — is stood up without hard-coding product identity into any shared layer: registration,
Product Core, capability selection, its own database, the five profiles, release lifecycle, product
modules, and the ordered checklist from registration to running.

Not authoritative for: how to add a module, endpoint, entity or migration once the product exists —
`NEW_MODULE_GUIDE.md`; where the repository sits — `REPOSITORY_STRUCTURE.md`; database mechanics —
`DATABASE_STANDARDS.md`; what proof is required — `ASSURANCE_STANDARDS.md`; the machine-specific
form of all of this — `MACHINE_DEVELOPMENT_GUIDE.md`.

---

## 1. The proposition

> A product is a **composition of declared capability plus its own domain**. It is not a
> construction project, and standing up the second product should cost a fraction of the first.

Nexus has one product today — Chat, in `Nexus.Web` — and it was built before this path existed. It
is therefore a **reference, not a template**: it holds patterns worth copying (aggregate folders,
endpoint files, the `ContextBundle` mapper) inside a repository shape that is being split apart
(`REPOSITORY_STRUCTURE.md` §3.4).

**TARGET — M-12-1.1 Product template and integration checklist.** Its acceptance criteria are
exact and they are the outline of this document: one documented path from registration to running,
no step invented per product, a reference product stood up end to end with the elapsed time recorded
as a baseline, and — the criterion that governs everything else — **no step requires modifying a
layer below 12.**

---

## 2. What a product is made of

```
Product
├── Registration            GOVERNANCE (03) — the fact that it exists and is ours
├── Product Core            PRODUCT CORE (06) — identity, membership, profile, settings, state
├── Capability packs        declared, not coded — what of layers 01–11 this product consumes
├── Its own database        one database per product
├── Product modules         PRODUCTS (12) — the actual domain
└── Profiles               architecture · stack · security · assurance · deployment
```

**The split that matters.** Everything common — authentication, membership, subscriptions,
entitlements, onboarding, settings, scope hierarchy — comes from layers 01 and 06 and is **never
duplicated per product**. What a product owns is its domain and its users' experience of that
domain, and nothing else.

---

## 3. The anti-pattern, stated first

```csharp
// FORBIDDEN. Anywhere. In any layer. Including a test helper.
if (product == "Vault") { ... }
if (Product == ProductKind.Trips) { ... }
switch (productName) { case "Chat": ... }
```

**There is no product-identity branching anywhere in Nexus.** Not in CORE, not in DATA, not in
AI, not in EXPERIENCE, not in ASSURANCE, not in a shared helper, not "temporarily".

**An architecture test forbids it** — that is an explicit acceptance criterion of **M-12-1.2
Capability pack composition**: *an architecture test fails any branch on product identity across the
whole solution*, and *adding a capability to a product is a declaration change, not a code change*.
The same criterion is repeated independently for PRODUCT CORE at **M-06-1.2** (*no if-product-equals
branching exists anywhere in Layer 06*) and for ASSURANCE at **M-09-7.1** (*an architecture test
forbids branching on product identity inside ASSURANCE*). Three layers state it separately because
it is the rule most likely to be broken by a small, reasonable-looking change.

**Why it is fatal rather than untidy.** The first `if (Product == Vault)` is one line in one shared
service. It works. It is followed by a second, then by a shared service that cannot be understood
without knowing every product, then by a change to Vault that breaks Trips. At that point the layers
below 12 are no longer shared infrastructure; they are a single application with several personalities,
and no amount of refactoring recovers the boundary cheaply.

**What to do instead, in order of preference:**

| Instead of branching | Do this |
|---|---|
| A product needs a capability others do not | Declare the capability pack. The layer reads a declaration; it never learns which product it is serving |
| A product needs different behaviour in a shared algorithm | Inject a strategy the product registers. The layer holds the interface, the product holds the implementation |
| A product needs a different hierarchy of things | Register a scope kind and an `IScopeResolver` — **M-06-1.2**. A machine-domain consumer registers an entirely different hierarchy with no code change in layer 06 |
| A product needs different proof | Select an assurance profile — §9. A profile is data |
| A product needs a setting | Scoped settings — **M-06-5.1** — not a constant in a shared class |
| The capability is genuinely universal | It belongs in layer 06, implemented once, for everyone |

If none of these fits, the requirement is telling you the boundary is in the wrong place. Move the
boundary; do not add the branch.

---

## 4. Step 1 — Register the product in GOVERNANCE

GOVERNANCE says a product **exists and is ours**. DEVELOPER says what is being **built** in it. They
are deliberately separate registries.

1. Create the `Product` record — id, name, owner, classification, lifecycle state. **M-03-1.1
   Product registry.**
2. Record ownership and accountability. "Who is accountable for Vault" must have one answer.
3. Record the classification — consumer product, internal business system, tooling, machine
   application. Classification drives scheduling: consumer products are planned and scheduled;
   internal business systems are pulled in when the business needs them, and their phase means
   *eligible from*, not *scheduled for*.
4. Register brand and domains if it presents externally — **M-03-3.1**, **M-03-3.2**.
5. Register compliance obligations and any data-residency requirement — **M-03-4.1**, **M-03-4.2**.
6. Register the technology versions it uses — **M-03-2.2 Product technology usage**, so that an
   end-of-life version resolves to the list of affected products.

**CURRENT.** `IProductRegistry` exists as an interface in `Nexus.Platform.Contracts/Identity/` with
no implementation. There is no `Product` record anywhere. Registration today is a documentation act
— write it down in `docs/` — and it becomes a real record at M-03-1.1.

**The minimum that must exist before anything else can.** DEVELOPER's work graph hangs off a
`ProductId`. A product with no id cannot have work planned against it, cannot have releases, and
cannot have evidence attributed to it.

---

## 5. Step 2 — Define Product Core

Product Core is the product's identity **inside** the platform: who its members are, what profile
they have there, what settings it carries, and what state it is in. It is layer 06 capability that
the product configures, not code the product writes.

| Concern | Comes from | Milestone |
|---|---|---|
| Product profile and membership — one Nexus identity, many product contexts | Layer 06 | **M-06-2.1** |
| Product-scoped roles — admin in one product, reader in another | Layer 06 | **M-06-2.2** |
| Plans and subscriptions | Layer 06 | **M-06-3.1** |
| Entitlements and feature access | Layer 06 | **M-06-3.2** |
| Scoped settings | Layer 06 | **M-06-5.1** |
| Scope hierarchy — Workspace → Project → Subproject | Layer 06 | **M-06-1.2** |

**The scope hierarchy is the extension point, not a fixed shape.** PRODUCT CORE owns
Workspace → Project → Subproject. DEVELOPER extends it downward: Subproject → Release → Milestone →
Feature → WorkItem → Task. A product registers **its own** scope kinds the same way — and the
acceptance criterion for M-06-1.2 is explicit that a machine-domain consumer can register an
entirely different hierarchy without a code change in layer 06.

The same mechanism is what lets EXPERIENCE hold one conversation engine for every product:
**conversation is universal; structure is contextual.** A consumer registers a scope kind and an
`IScopeResolver`; the engine passes the resulting `ContextBundle` through untouched.

**Removing product membership must not affect the Nexus identity.** That is an M-06-2.1 acceptance
criterion, and it is the test of whether Product Core has been kept separate from CORE identity.

---

## 6. Step 3 — Select capabilities

A product declares which of layers 01–11 it consumes. **The declaration is data.** The layer being
consumed never learns which product declared it.

| Capability | From | Typically declared by |
|---|---|---|
| Identity, tenancy, authorization, audit, secrets | 01 CORE | Every product |
| Documents, knowledge, search | 02 DATA | Products with content |
| Registries — technology, brand, compliance, licence | 03 GOVERNANCE | Every product |
| Turns, agents, memory, context | 04 AI | Products with a conversational or reasoning surface |
| Workflows, jobs, approvals, escalation | 05 AUTOMATION | Products with processes |
| Membership, profiles, subscriptions, entitlements, settings | 06 PRODUCT CORE | Every product |
| Work graph, releases, requirements | 07 DEVELOPER | Products that are themselves built by Nexus |
| Pipelines, environments, deployment, backup | 08 DELIVERY | Every deployable product |
| Criteria, evidence, verdicts, inspection | 09 ASSURANCE | Every product |
| Health, telemetry, incidents, cost | 10 OPERATIONS | Every running product |
| Conversation engine, chat and form UX, design primitives | 11 EXPERIENCE | Every product with a UI |

**TARGET — M-12-1.2 Capability pack composition.** `CapabilityPack` becomes a real record and the
architecture test lands with it.

**A warning against premature packs.** Define a capability pack when a **second** product actually
needs the same capability. Two data points beat one guess, and a pack designed around one product is
that product's implementation with a general-sounding name.

---

## 7. Step 4 — Create the product's database

**One database per product.** Layers 01–11 share the `NexusPlatform` database with a schema each;
every product gets its own database.

Why, stated as three independent reasons, any one of which would be sufficient:

1. **Lifecycle.** Product data is created, retained and deleted on the product's schedule, not the
   platform's.
2. **Residency and retention.** A compliance obligation registered against one product must be
   satisfiable without touching another's data.
3. **Removability.** A product should be removable. A product whose tables are interleaved with the
   platform's cannot be removed; it can only be abandoned.

Mechanics — `DATABASE_STANDARDS.md`:

1. Create the database. Name it for the product.
2. `Nexus.Products.<Name>.Infrastructure/Sql/<Name>DbContext.cs`, plus a design-time factory so
   `dotnet ef` can build it without the host — `NexusChatDbContext` and `NexusChatDbContextFactory`
   are the pattern.
3. Schemas **inside** the product database are the product's own. Do not reuse a layer schema name,
   and never use `org` — that is the legacy schema of the single existing migration, renamed at
   **M-02-1.5**.
4. Every entity follows the Id/Seq/Ref pattern and the §12 checklist in `DATABASE_STANDARDS.md`.
   Register `Ref` prefixes that no other entity uses.
5. **Cross-product queries do not exist.** Products never reference each other — not by project
   reference, not by database, not by endpoint. Where two products need the same fact, it belongs to
   a layer below them.
6. Connection string per environment — `CONFIGURATION_STANDARDS.md`; credentials never in Git.

---

## 8. Step 5 — The five profiles

A profile is a **declaration** that selects behaviour from a shared layer. Every one of the five is
data. None of them is a code branch. This is the mechanism that replaces `if (Product == X)`
throughout the system.

### 8.1 Architecture profile

Which shape the product takes: which of `.Domain`, `.Application`, `.Infrastructure`, `.Api`,
`.Client` it has, which scope kinds it registers, whether it exposes a conversational surface, and
which `ContextBundle` mapper it supplies.

A product with no UI has no `.Client`. A product with no reasoning surface registers no scope
resolver with EXPERIENCE and supplies no mapper. Neither absence requires a change anywhere below
layer 12.

### 8.2 Stack profile

Which approved technologies the product uses, and at which versions — `TECHNOLOGY_STACK.md` and
`STACK_VERSION_POLICY.md`.

**A product does not choose its own stack freely.** It selects from the approved set; introducing
anything new requires an ADR (next: **ADR-016**), a `TECHNOLOGY_STACK.md` entry in the same pull
request, and a version pin. The stack profile is recorded as `ProductTechnologyUsage` at
**M-03-2.2**, which is what makes "which products are affected by this end-of-life version?" a query
rather than an archaeology exercise.

### 8.3 Security profile

Which classification the product's data carries, which roles exist within it, what tenant isolation
it requires, which PII it holds, and what must be audited — `SECURITY_STANDARDS.md`.

> **CURRENT, and it must be said in every product conversation: there is no authentication and no
> authorization in Nexus today.** Identity is a 240-byte stub, `PermissiveQuotaPolicy` enforces
> nothing, and `ChatTurnIdentity` returns a hardcoded tenant. A product's security profile can be
> *declared* now; it cannot be *enforced* until **M-01-1.2** and **M-01-3.1**. Do not design a
> product whose safety depends on a check that does not exist.

### 8.4 Assurance profile

Which verification methods are **mandatory** for this product — `ASSURANCE_STANDARDS.md` §7.

| Profile | Mandatory methods on top of the base |
|---|---|
| **Software** | Unit, integration, contract and architecture verification |
| **AI** | Evaluation and citation checking |
| **ERP** | Process validation and user acceptance |
| **Machine** | Inspection characteristics, measurement and validation |
| **Consumer** | Usability and accessibility validation |

A product may hold more than one — an ERP module with a conversational surface is ERP **and** AI; a
machine application with an operator UI is Machine **and** Consumer.

**TARGET — M-09-7.1**, whose third acceptance criterion is the one that keeps profiles honest:
selecting a profile is a declaration, not a code change, and ASSURANCE never knows which product it
is looking at.

**Safety-critical criteria** — **M-09-7.2** — cannot be waived by the ordinary deviation path and
**may not be created, modified or waived by any agent**. Any product with a physical or safety
consequence must read `MACHINE_DEVELOPMENT_GUIDE.md` before its assurance profile is settled.

### 8.5 Deployment profile

Which environments the product has, how it is promoted between them, what its backup and restore
requirements are, and what health signals it emits.

> **TARGET, entirely.** There are no environments, no pipelines, no infrastructure as code and no
> deployment of any kind — `.github/workflows/` in `NexusAI` exists and is empty, and the other two
> repositories have no `.github` directory at all. The sequence is **M-08-1.1** (feed reachable from
> CI) → **M-08-1.2** (pipelines) → **M-08-4.1** (environment model) → **M-08-4.2** (provisioning) →
> **M-08-5.1** (automated deployment) → **M-08-5.2** (release promotion). A deployment profile
> written before M-08-4.1 is a wish list; write it anyway, but do not schedule work against it.

---

## 9. Step 6 — Release lifecycle

**TARGET — M-12-1.3 Product state model.** Product state is **eight independent dimensions**, not
one ambiguous status field, and each is marked derived or manual with every derived one computed
rather than entered:

| Dimension | Answers |
|---|---|
| `ProductLifecycleState` | Is this product proposed, active, sunsetting, retired? |
| `DevelopmentStage` | How mature is what is being built? |
| `CurrentRelease` | What is the latest release? |
| `CurrentProductionRelease` | What is actually in production? |
| `DevelopmentHealth` | Is the work going well? |
| `DeploymentState` | Is it deployed, and where? |
| `OperationalHealth` | Is the running system healthy? |
| `ComplianceState` | Are its registered obligations satisfied? |

Collapsing these into one "status" field is the specific failure this milestone prevents: a product
can be actively developed, healthy in development, deployed to no environment, and non-compliant,
all at once, and a single field forces a lie.

Releases and maturity are **M-07-7.2**; release qualification — the decision that a release has been
adequately proven — is **M-09-5.1**, and regression qualification is **M-09-5.2**. Tags and release
branches are `GIT_WORKFLOW.md` §13.

---

## 10. Step 7 — Build the product modules

The domain itself. Follow `NEW_MODULE_GUIDE.md` §2 for modules, §4 for entities, §3 for endpoints,
§6 for frontend features. The product-specific rules:

1. **Projects:** `Nexus.Products.<Name>.Domain`, `.Application`, `.Infrastructure`, `.Api`,
   `.Client`. Not every product has all five.
2. **`.Domain` holds aggregates**, one folder each with `<Name>.cs`, `<Name>Id.cs`,
   `<Name>Status.cs`, `I<Name>Repository.cs`. `.Domain` never references `.Infrastructure`.
3. **One product module class** — `<Name>ProductModule.cs`, following `ChatProductModule.cs` — where
   services and endpoints are registered, called once from `Program.cs`.
4. **Routes under `/api/v1`**, plural, lowercase — `API_STANDARDS.md` §3.
5. **Never reference another product.** The architecture test enforces it.
6. **If the product has a conversational surface**, supply a `ContextBundle` mapper that flattens
   its aggregates into `ContextItem` values. `ChatContextBundleMapper` is the reference, and it is
   one of only two things in the system with a behaviour test. Add mappings **one field at a time
   and measure each** — nine mappings shipped together cannot be individually evaluated; two can.
7. **`ScopeRef` stays opaque to AI.** The mapper is the boundary, and the boundary is the point —
   `AI_DEVELOPMENT_STANDARDS.md` §5.

---

## 11. Product categories — what differs, and what does not

| Category | What is different | What is identical |
|---|---|---|
| **Consumer** (Vault, Trips) | Consumer assurance profile; usability and accessibility are mandatory; brand and domains registered | Registration, Product Core, capability declaration, own database, the five profiles |
| **Internal business system** (ERP) | ERP assurance profile — process validation and user acceptance; pulled in when the business needs it rather than scheduled | Same |
| **Game** | Almost certainly a different stack profile, and an experience layer it does not share | Same registration and governance. **A game is not an excuse to bypass GOVERNANCE** |
| **Machine application** | Machine assurance profile; deterministic controllers own real-time behaviour; safety-critical criteria apply | Same — plus everything in `MACHINE_DEVELOPMENT_GUIDE.md` |
| **Tooling** | Often consumes DEVELOPER (07) heavily; may have no consumer UI | Same |

The row that is easiest to get wrong is the game: an unfamiliar stack makes the platform feel
irrelevant, and the product gets built outside the model. It still needs a `Product` record, an
owner, a licence position, a technology usage record and an assurance profile. **What is different
is the stack profile, not the governance.**

---

## 12. The ordered checklist — registration to running

Every step names its owner and its milestone. Steps marked **CURRENT possible** can be done today;
the rest are declarations until their milestone lands.

| # | Step | Layer | Milestone | Today |
|---|---|---|---|---|
| 1 | Decide the product exists; name it; assign an owner | 03 | M-03-1.1 | **CURRENT possible** — as a document |
| 2 | Classify it: consumer / internal / tooling / machine / game | 03 | M-03-1.1 | CURRENT possible |
| 3 | Register brand, domains, compliance obligations, licence position | 03 | M-03-3.1, M-03-4.1, M-03-5.1 | Documented |
| 4 | Create the repository `Nexus.Products.<Name>` | 08 | — | **CURRENT possible** |
| 5 | Declare the stack profile; record technology usage | 03 | M-03-2.2 | Documented |
| 6 | Declare capability packs | 12 | M-12-1.2 | Documented |
| 7 | Define Product Core: membership, profile, roles, settings | 06 | M-06-2.1, M-06-2.2, M-06-5.1 | Not yet possible |
| 8 | Register scope kinds and an `IScopeResolver` | 06 / 11 | M-06-1.2 | Not yet possible |
| 9 | Create the product database and its `DbContext` | 02 / 12 | — | **CURRENT possible** |
| 10 | Model the domain: aggregates, Id/Seq/Ref, configurations, migrations | 12 | — | **CURRENT possible** |
| 11 | Build `.Application` and `.Api`; register one product module class | 12 | — | **CURRENT possible** |
| 12 | Supply the `ContextBundle` mapper, if there is a conversational surface | 12 → 04 | — | **CURRENT possible** |
| 13 | Build the client, if there is one | 11 | — | **CURRENT possible** |
| 14 | Declare the security profile; wire tenant isolation | 01 | M-01-1.2, M-01-3.1 | Declared only |
| 15 | Select the assurance profile; write acceptance criteria | 09 | M-09-7.1, M-09-1.1 | Declared only |
| 16 | Add architecture tests for the product's boundaries | 09 | — | **CURRENT possible** |
| 17 | Add a DELIVERY pipeline | 08 | M-08-1.2 | Not yet possible |
| 18 | Define environments and the deployment profile | 08 | M-08-4.1, M-08-4.2 | Not yet possible |
| 19 | Add OPERATIONS instrumentation: health, telemetry, cost | 10 | M-10-2.1, M-10-1.1, M-10-4.1 | Not yet possible |
| 20 | Register the product state model's eight dimensions | 12 | M-12-1.3 | Not yet possible |
| 21 | Qualify the first release | 09 | M-09-5.1 | Not yet possible |
| 22 | Deploy | 08 | M-08-5.1 | Not yet possible |

**Step 16 is the one to not skip while the rest is unavailable.** Nine of the twenty-two steps are
possible today, and the architecture test is the only one of them that keeps the other thirteen
achievable later. A product built today without boundary tests will have absorbed platform
responsibilities by the time the platform is ready to provide them.

---

## 13. What must never happen when a product is added

| Never | Why |
|---|---|
| `if (Product == X)` in any layer | §3. An architecture test forbids it — M-12-1.2 |
| A product type in `Nexus.Platform.Contracts` or `Nexus.Intelligence.Contracts` | **No shared kernel** |
| A product referencing another product | Not by project, database or endpoint |
| A product writing to another product's database or to a layer schema | One database per product |
| A product-specific field added to a layer 01–11 table | The product owns its own data |
| A product bypassing GOVERNANCE registration because it is "just a prototype" | An unregistered product is shadow IT with an internal sponsor |
| A capability duplicated per product | Authentication, membership, subscriptions and settings come from 01 and 06 |
| A product choosing an unapproved technology | ADR + `TECHNOLOGY_STACK.md` entry in the same pull request |
| An agent creating or waiving a safety-critical acceptance criterion | Absolute, no exception path — M-09-7.2 |
| A product's structure reaching AI | `ScopeRef` is opaque; the mapper is the boundary |

---

## 14. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Which product is second | Business need. Vault and Trips are named in the roadmap; internal systems are pulled, not scheduled | **Not yet decided** |
| Whether each product gets its own repository or several share one | The second product. One product is not a pattern | Not yet decided |
| What the standard capability packs are | A second product needing the same capability. Defining them from one product produces that product's implementation with a general name | Deliberately deferred — M-12-1.2 |
| Whether Chat is retrofitted onto this path or grandfathered | The `Nexus.Web` split | Not yet decided |
| Whether a standalone end-user chat application is released as a product | Business decision | Not yet decided — but if it is, it is a **PRODUCT (layer 12) consuming EXPERIENCE**. The conversation engine is never called a Chat product |
| Database naming convention across products | The second database | Not yet decided |

---

## 15. References

- `REPOSITORY_STRUCTURE.md` — where `Nexus.Products.<Name>` sits and what belongs in it.
- `NEW_MODULE_GUIDE.md` — the procedures for everything inside the product once it exists.
- `DATABASE_STANDARDS.md` — the product database, Id/Seq/Ref, migrations, cross-database rules.
- `SECURITY_STANDARDS.md` — the security profile, and the absence of authentication today.
- `ASSURANCE_STANDARDS.md` §7 — assurance profiles and the safety-critical rules.
- `MACHINE_DEVELOPMENT_GUIDE.md` — the machine-product form of this guide.
- `AI_DEVELOPMENT_STANDARDS.md` — the `ContextBundle` mapper and the AI boundary.
- `TECHNOLOGY_STACK.md` / `STACK_VERSION_POLICY.md` — the stack profile.
- `DEVELOPMENT_WORKFLOW.md` — how the work to build any of this is planned and scheduled.
