# Architecture Overview

**Status:** Active — describes the target architecture and the current system side by side, and
never silently conflates them
**Owner:** Durai, with each layer's owner responsible for its own row
**Last updated:** 2026-08-21
**Layer:** cross-cutting
**Authoritative for:** what Nexus is, the problem it solves, the twelve layers at one line each, the
governing sentences, the honest current state, and which document to read next. It is **not**
authoritative for any layer's detail — `LAYER_MODEL.md` owns that.

This is the first document a new reader — human or agent — should finish. Ten minutes. When you are
done you should be able to take any piece of work anyone hands you and say which layer it belongs
to, or say precisely why it is ambiguous.

---

## 1. The problem

Nexus is a personal AI platform that is intended to become the substrate for many products: an ERP,
consumer applications, business systems, eventually machines. The naive version of that is one
application that grows features until nobody can change it. The problem Nexus is designed against is
therefore not "how do we add AI to an app" but:

> **How do you build the tenth product as cheaply as the second, without any of them contaminating
> the others or the foundation they stand on?**

Four failure modes follow from getting that wrong, and every structural decision in this
architecture is a defence against one of them:

| Failure mode | What it looks like | The defence |
|---|---|---|
| Every product reinvents the foundation | Three user models, three permission checks, no way to answer "who did this" | Layers 01–06 are product-neutral and shared |
| Products grow into each other | `if (Product == Vault)` in shared code; a foreign key between two products' tables | No product branching; one database per product |
| The AI learns the product's shape | The model pipeline knows what a `Workspace` is and breaks when a product changes | The `ContextBundle` / `ScopeRef` seam |
| Development state lives in conversation | Roadmap, progress and rationale in chat transcripts and markdown; lost between sessions | DEVELOPER as a structured system of record |

The fourth is the one being solved right now, and this document set is part of the evidence that it
had not been solved before.

---

## 2. What Nexus is

Twelve layers of responsibility, built across five repositories, sharing one platform database and
giving every product its own. Each layer owns a defined set of facts and a defined set of
capabilities, may depend only on layers beneath it, and is separated by project boundaries that
architecture tests can check mechanically rather than by convention that a reviewer has to remember.

Layers are **responsibility**, not chronology. No layer is built to completion before the next
begins — CORE alone has work in four of the six phases. A layer with milestones in P0, P1, P3 and
P5 and nothing in P2 is expected, not a defect.

---

## 3. The governing sentences

The original rule was *Intelligence decides, Platform executes, products own the data and the
experience.* Extended across twelve layers, one clause per layer, in short-name form:

> **CORE is the ground everything stands on. DATA remembers. GOVERNANCE records what is true.
> AI reasons. AUTOMATION executes. PRODUCT CORE gives every product its reusable half.
> DEVELOPER builds. DELIVERY ships. ASSURANCE proves. OPERATIONS runs. EXPERIENCE is how a human
> talks to any of it. PRODUCTS own their domain and their users.**

Three of those clauses do the most work in practice, because they are the ones people collapse:

- **AI reasons, AUTOMATION executes.** A workflow that approves a purchase order does not know what
  a purchase order is, and the model that suggested the approval does not run it. Separating them is
  what makes reliability a property of the system rather than of whichever feature needed it first.
- **DELIVERY ships, ASSURANCE proves, OPERATIONS runs.** A green build proves code compiles and some
  assertions held. It does not prove a requirement was met, and it says nothing about the thing that
  is actually running. Three different questions, three different owners.
- **DEVELOPER builds, GOVERNANCE records what exists.** GOVERNANCE says a product exists and is
  ours; DEVELOPER says what is being built in it. The two are routinely confused and they belong in
  different schemas.

---

## 4. The twelve layers, one line each

Short names are primary and are used everywhere. `LAYER_MODEL.md` carries the long names, the
projects, the schemas and the mapping from the old names still present in code.

| # | Layer | One line | Repository (TARGET) | Schema |
|---|---|---|---|---|
| 01 | **CORE** | The universal technical foundation — identity, tenancy, authorization, audit, secrets, model and tool gateways | `Nexus.Platform` | `core` |
| 02 | **DATA** | Stores, governs and retrieves information, documents and knowledge — and provides the persistence discipline every layer uses | `Nexus.Platform` | `data` |
| 03 | **GOVERNANCE** | Records what products, technologies, brands, domains, licences and obligations exist, and who owns them | `Nexus.Platform` | `governance` |
| 04 | **AI** | Reasons — model access, context assembly, memory, agents, planning, evaluation — without ever learning a product's shape | `Nexus.Intelligence` | `ai` |
| 05 | **AUTOMATION** | Executes reliable repeatable processes: workflows, schedules, jobs, queues, retries, approvals | `Nexus.Platform` | `automation` |
| 06 | **PRODUCT CORE** | The reusable half of every product — the `Workspace → Project → Subproject` scope trunk, membership, subscriptions, entitlements, settings | `Nexus.Platform` | `product_core` |
| 07 | **DEVELOPER** | Defines, plans, builds, reviews and coordinates software, and is the structured system of record for development state | `Nexus.Developer` | `developer` |
| 08 | **DELIVERY** | Moves source into reproducible running systems — git, CI, artifacts, environments, deployment, backup, infrastructure | `Nexus.Platform` + per-repo pipelines | `delivery` |
| 09 | **ASSURANCE** | Proves that requirements were satisfied — acceptance criteria, verification and validation, evidence, verdicts, quality gates | `Nexus.Platform` | `assurance` |
| 10 | **OPERATIONS** | Runs and observes what is deployed — logs, metrics, traces, health, incidents, cost, capacity | `Nexus.Platform` | `operations` |
| 11 | **EXPERIENCE** | Reusable human and system interaction, above all the conversation engine, with structure supplied by the consumer | `Nexus.Experience` | `experience` |
| 12 | **PRODUCTS** | Solve real business and customer problems by composing everything above | `Nexus.Products.<Name>` | own database per product |

Two of those lines carry the two largest structural decisions in the architecture, and both are
easy to get wrong from the table alone:

**Conversation is a layer, not a product.** EXPERIENCE owns the conversation engine. A conversation
carries an opaque `ScopeRef` and nothing else about its context; a consumer registers a scope kind
and an `IScopeResolver`, the engine calls it, receives a `ContextBundle` and passes it through
untouched. DEVELOPER, a plain conversation and machine work each need entirely different structure
around identical conversation mechanics — which is impossible if conversation belongs to one
product. A standalone end-user Chat application, if it is ever released, is a Layer 12 **product**
that consumes EXPERIENCE. The engine is never called a Chat product.

**The scope trunk is shared and extended, not redefined.** PRODUCT CORE owns
`Workspace → Project → Subproject`. DEVELOPER extends downward:
`Subproject → Release → Milestone → Feature → WorkItem → Task → Subtask`. A plain conversation stops
at Project. Machine work registers a different trunk entirely. Nobody redefines `Workspace`.

---

## 5. The rules that hold it together

Five invariants. `DEPENDENCY_RULES.md` states them precisely, gives the full 12×12 matrix, and
records which have a test today.

1. **A layer may depend only on layers below it.** DELIVERY, ASSURANCE and OPERATIONS are
   cross-cutting: anything may emit to them, they may depend on nothing above CORE.
2. **No shared kernel.** `Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never
   reference product types. Currently true — keep it true.
3. **Products never reference each other.** Made physical by one database per product: two products
   cannot share a foreign key if they cannot share a database.
4. **AI never sees product structure.** It receives a `ContextBundle` of flattened `ContextItem`s;
   `ScopeRef` is opaque to it. This is the best-designed boundary in the codebase and no feature is
   allowed to be made easier by breaking it.
5. **No `if (Product == X)` anywhere.** Capability packs are declared, not coded.

And one rule about facts rather than references, which `DATA_OWNERSHIP.md` owns in full:

> **The domain owns the structured fact. DATA owns its document representation. Never both, never
> neither.**

DEVELOPER owns that milestone `M-07-1.1` exists and is blocked; DATA owns the specification document
describing it. ASSURANCE owns the verification result; DATA owns the formal test report. GOVERNANCE
owns trademark status; DATA owns the certificate PDF.

---

## 6. The current state, told honestly

Everything below was read from disk on 2026-08-21. `_FACTS.md` is the ground truth; this is the
summary a newcomer needs before believing anything else in the document set.

**Three repositories exist**, not five: `NexusAI` (TARGET `Nexus.Platform`), `Nexus.Int` (TARGET
`Nexus.Intelligence`) and `Nexus.Web` (TARGET `Nexus.Experience`). `Nexus.Developer` and
`Nexus.Products.<Name>` do not exist. `C:\Personal\LocalNuGet` is a package feed on disk, not a
repository.

**Seven of the twelve layers have no project at all.** What exists:

| Area | State |
|---|---|
| Contract surfaces | **Real.** `Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` are genuinely designed — `ContextBundle`, `ScopeRef`, `IntelligenceTurnRequest`, `IModelGateway`, `IAuditLog` |
| AI turn pipeline | **Real and working.** `TurnPipeline` with ten steps, `Planner`, `ExecutionEngine`, `KeywordContextRanker`, `PromptAssembler`, `AgentRegistry` |
| Model access | **Real for OpenAI only.** `OpenAIModelGateway` behind `RoutingModelGateway`; `AnthropicModelGateway.cs` is a 306-byte stub |
| Everything stateful | **In memory.** `ConsoleAuditLog`, `InMemoryUsageMeter`, `PermissiveQuotaPolicy`, `InMemoryMemoryStore`, `InMemoryTurnTraceStore`, `InMemoryResultReportStore`. Nothing survives a restart |
| Identity | **A 240-byte stub.** `IdentityProvider.cs`. `ChatTurnIdentity` returns a hardcoded tenant and placeholder permissions |
| Persistence | **Mid-migration.** ADR-014 moves Chat from Dataverse to Azure SQL. One of eleven aggregates has a SQL configuration and repository; ten still run on Dataverse |
| Frontend | **Real.** React + TypeScript + Vite + TanStack Query, ~60 files, chat / projects / workspaces / system features |
| Tests | **Five files.** Three are NetArchTest boundary tests. **Exactly two behaviour tests exist in the entire system** — `KeywordContextRankerTests.cs` and `ChatContextBundleMapperTests.cs`. `Nexus.Platform.Tests` is a `.csproj` with zero `.cs` files |
| CI | **None, anywhere.** `NexusAI\.github\workflows\` exists and is empty; the other two repositories have no `.github` directory. No deployment pipeline, no infrastructure-as-code, no environment definitions |

**The one proven thing worth knowing.** The `Id`/`Seq`/`Ref` pattern works against a live database:
migration `20260820180802_InitialSqlSchema.cs` created `[org].[Workspace]`, and `api_run.log` at
2026-08-20 18:09 UTC records two successive inserts returning server-generated `Ref` and `Seq`.
The database allocates the reference, not C#. `DATABASE_STANDARDS.md` owns why.

**The one thing that should worry you.** On 2026-08-20 all three repositories lost `.git\objects`
simultaneously — consistent with antivirus quarantine of extensionless zlib blobs. They were
recovered by fresh clone and in-place `.git` swap; `.git-broken\` still sits in all three. The
antivirus exclusion for `C:\Personal` was recommended and **has never been confirmed**.
`GIT_WORKFLOW.md` carries the live lessons; `GIT_RECOVERY_2026-08-20.md` carries the narrative.

---

## 7. The Foundation Gate

The Foundation Gate is closed when **three independent work items can be planned, isolated, built,
tested, evidenced, reviewed and integrated simultaneously — every step recorded in structured
DEVELOPER data, against a system with real identity, a single persistence backend, automated build
verification, and a quality gate that blocks integration while a mandatory acceptance criterion is
unverified.** It is a *minimum capability* threshold, not layer completion: reading it as "finish
CORE, DATA, AI and DEVELOPER" would push it out by a year and delay every business system behind
it. Nine layers contribute a deliberately small slice — CORE contributes real identity and a durable
audit log, DATA contributes Azure SQL and a versioned `Document`, DELIVERY contributes CI and branch
protection (it cannot come after the gate, because the gate's own acceptance test requires
independent build and test across three workers), ASSURANCE contributes acceptance criteria and one
quality gate, EXPERIENCE contributes the conversation core and scope resolution, GOVERNANCE
contributes a `Product` record and nothing more, PRODUCT CORE contributes only the scope trunk, and
AUTOMATION contributes nothing at all, deliberately. The acceptance test is milestone `M-07-5.3`:
nine criteria, each with stored evidence, and one meta-criterion — **no step required a human to
read a log and retype a result.** Past the gate, business systems and Nexus's own continuation run
as two permanent parallel streams.

---

## 8. Placing a piece of work

The decision procedure, in order. Stop at the first line that answers.

| Ask | If yes, it belongs to |
|---|---|
| Is it a document, a specification, a manual, an ADR, a report, or retrieval over any of those? | **02 DATA** |
| Is it about *who* someone is, across all products — identity, tenant, role, permission, secret, audit, model or tool access? | **01 CORE** |
| Is it a registry fact — this product exists, this domain is ours, this licence applies, this trademark is granted? | **03 GOVERNANCE** |
| Does it reason, plan, rank context, remember, or call a model? | **04 AI** |
| Does it run a process on a schedule, retry it, queue it, or gate it on an approval? | **05 AUTOMATION** |
| Is it *who you are within a product*, or the shared `Workspace`/`Project`/`Subproject` trunk, or a plan, subscription, entitlement or quota? | **06 PRODUCT CORE** |
| Is it about what is being built — a milestone, a work item, a dependency, a worker, a review, derived progress? | **07 DEVELOPER** |
| Is it a repository, branch, commit, pipeline, artifact, environment, deployment or backup? | **08 DELIVERY** |
| Is it an acceptance criterion, a verification method, evidence, a verdict, a defect or a quality gate? | **09 ASSURANCE** |
| Is it about something already running — a log, metric, trace, health check, incident, alert or cost record? | **10 OPERATIONS** |
| Is it conversation, message, participant, attachment, notification delivery or a reusable UI surface? | **11 EXPERIENCE** |
| Is it meaningful only inside one product's domain? | **12 PRODUCTS** |

Four ambiguities account for most of the mistakes, and each has a fixed answer:

| It feels like… | It is actually |
|---|---|
| "The build" | **DELIVERY** produces the build record; **DEVELOPER** interprets whether it satisfies a work item; **ASSURANCE** decides whether the requirement was met |
| "The test" | **DEVELOPER** asks what must be proven; **DELIVERY** executes repeatable technical verification; **ASSURANCE** determines satisfaction; **OPERATIONS** proves the running system stays healthy |
| "The document" | The owning domain holds the structured fact; **DATA** holds the document about it — always both, never one |
| "The chat" | **EXPERIENCE** owns the engine; the consumer owns the structure; **AI** owns the reasoning; none of the three knows the others' types |

If two layers both plausibly own it, that is a signal you have one concept doing two jobs. `Session`
and `Artifact` are the existing examples — see `DATA_OWNERSHIP.md` §6.

---

## 9. Where to go next

| You want to… | Read |
|---|---|
| Understand a layer properly | `LAYER_MODEL.md` |
| Know what may reference what, and how it is enforced | `DEPENDENCY_RULES.md` |
| Find which layer owns an entity | `DATA_OWNERSHIP.md` |
| Understand databases, schemas and why they split that way | `DATABASE_ARCHITECTURE.md` |
| Write schema, migrations, keys and the Id/Seq/Ref pattern | `DATABASE_STANDARDS.md` |
| Get a machine building and running the system | `DEVELOPER_ONBOARDING.md`, then `LOCAL_DEVELOPMENT.md` |
| Know where a file goes | `REPOSITORY_STRUCTURE.md`, then `NAMING_STANDARDS.md` |
| Add anything — module, endpoint, entity, migration, agent, test, document | `NEW_MODULE_GUIDE.md` |
| Stand up a new product | `PRODUCT_DEVELOPMENT_GUIDE.md` |
| Know when something is finished | `DEFINITION_OF_DONE.md`, `ASSURANCE_STANDARDS.md` |
| See the full architecture and its reasoning | `../NEXUS_MASTER_ARCHITECTURE.md` |
| See the work breakdown | `../nexus-roadmap.yaml` — 614 nodes, authoritative for all structured work until `M-07-1.1` imports it |
| Find any document at all | `DOCUMENTATION_INDEX.md` |

---

## 10. What this document deliberately does not say

- **No layer detail.** Purpose, ownership, projects and gate scope per layer are `LAYER_MODEL.md`.
- **No dates or estimates.** Phase estimates live in `nexus-roadmap.yaml` and in the master
  architecture, and they move.
- **No technology choices.** `TECHNOLOGY_STACK.md` owns what is approved and what is explicitly not
  yet chosen — and several things a reader might assume are chosen are not, including any logging
  library, any container tooling and any UI test framework.
- **No history.** ADRs are the decision record. The next number is ADR-016.

---

## 11. References

- `LAYER_MODEL.md` — the twelve layers in detail, and the old-name mapping.
- `DEPENDENCY_RULES.md` — the dependency matrix and its enforcement.
- `DATA_OWNERSHIP.md` — which layer owns which structured fact, and the entity migration matrix.
- `DATABASE_ARCHITECTURE.md` — the physical database strategy.
- `DOCUMENTATION_INDEX.md` — every document, its owner and what it is authoritative for.
- `../NEXUS_MASTER_ARCHITECTURE.md` — the full architecture, current state and reasoning.
- `../nexus-roadmap.yaml` — the authoritative work breakdown.
- `_FACTS.md` — verified ground truth as of 2026-08-21.
