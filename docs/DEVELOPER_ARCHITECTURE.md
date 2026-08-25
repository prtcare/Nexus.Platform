# Developer Architecture

**Status:** TARGET — **no `Nexus.Developer` repository exists.** Every structure below is designed
and none of it is built. Each gap names the milestone that closes it
**Owner:** Durai
**Last updated:** 2026-08-21
**Layer:** 07 DEVELOPER — repository `Nexus.Developer`, schema `developer`
**Authoritative for:** the shape and boundaries of the DEVELOPER layer — its entity model and how
the pieces relate, the scope extension below `Subproject`, the dependency graph, the data that makes
parallel safety computable, the worker and run model, worktree allocation, how build and test
results enter the layer, review, controlled integration, derived progress, human governance, the
V1a/V1b split, and the autonomy levels beyond it.

**Not authoritative for:** when a work item may change state, work-item sizing, or the six-way
classification output — `DEVELOPMENT_WORKFLOW.md`. Branch naming, worktree commands and the merge
strategy — `GIT_WORKFLOW.md`. What "done" requires — `DEFINITION_OF_DONE.md`. Which layer owns which
entity — `DATA_OWNERSHIP.md`. Schema and migration rules — `DATABASE_STANDARDS.md`. What a reviewer
checks — `CODE_REVIEW_CHECKLIST.md`.

---

## 1. Purpose

DEVELOPER defines, plans and builds software. It builds the products, it builds the other eleven
layers, and it builds itself.

It exists because of one specific failure: **development state currently lives in conversation.**
What is being built, what depends on what, what was decided, what was proven and what is safe to
start next are all held in chat transcripts and markdown files. Both are lossy. A transcript ends
when the session ends. A markdown file records a decision at the moment it was written and then
drifts from the system it describes, silently, with nothing to detect the drift.

DEVELOPER replaces both with **structure that can be queried**. Not because prose is bad — the
document set you are reading is prose and belongs in DATA — but because coordination is not a
narrative task. You cannot ask a transcript whether two work items may run at the same time. You can
ask a graph.

The distinction that governs the whole layer:

| Prose describes | Structure decides |
|---|---|
| Why a decision was made | Whether B is blocked by A |
| How the architecture is shaped | Which three items may run simultaneously |
| What a milestone is for | Whether this build satisfies that work item |
| The narrative of an incident | Whether the milestone can report progress at all |

Documents stay in DATA (`M-02-2.1`). State comes here. A milestone in DEVELOPER holds a
`DocumentRef` to its specification; it does not hold the specification.

---

## 2. What this layer owns, and the four things it does not

The complete entity-to-layer mapping is `DATA_OWNERSHIP.md` §4 and is not repeated. The four
boundaries that are misunderstood most often:

| Not owned | Owner | What DEVELOPER holds instead |
|---|---|---|
| Product identity | 03 GOVERNANCE | A `ProductId` reference on `ProductDevelopment` |
| `Workspace`, `Project`, `Subproject` | 06 PRODUCT CORE | The extension *below* `Subproject` |
| The pipeline that produced a build | 08 DELIVERY | `BuildRecord` — its *interpretation* of a `PipelineRun` |
| Whether a requirement was satisfied | 09 ASSURANCE | The `Requirement`; ASSURANCE owns its `AcceptanceCriterion` |

That last row is the sharpest split in the system. DEVELOPER owns what someone wants. ASSURANCE
owns how anyone would know it was delivered. The layer that judges must own the criterion, or the
layer being judged marks its own homework.

Conversation storage is 11 EXPERIENCE. Runtime health is 10 OPERATIONS. Specification documents are
02 DATA.

---

## 3. Repository and project map — TARGET

**CURRENT:** none of this exists. `Nexus.Developer` is not a repository. The only development state
that exists in structured form today is `nexus-roadmap.yaml`, a hand-written file, and a `WorkItem`
aggregate stranded inside the Chat product in `Nexus.Experience`.

**TARGET** — the repository is created at `M-07-1.1`:

| Project | Holds |
|---|---|
| `Nexus.Developer.Contracts` | The types other layers may reference. No product type, ever |
| `Nexus.Developer.Core` | Aggregates — the work graph nodes, `Review`, `Requirement`, `Release`, `StatusHistory` |
| `Nexus.Developer.Graph` | `Dependency`, `ScopeDeclaration`, traversal, the parallel-safety rules, progress derivation |
| `Nexus.Developer.Orchestration` | `Worker`, `WorkerAssignment`, `DevelopmentRun`, branch and worktree coordination, the integration runner, and later the dispatcher |
| `Nexus.Developer.Infrastructure` | EF Core configurations, migrations, the roadmap importer, CI result ingestion |
| `Nexus.Developer.Api` | The HTTP surface |
| `Nexus.Developer.Client` | The work-graph view, and later the dashboard and conversation surface |

`Graph` and `Orchestration` are separate deliberately. Graph answers questions about structure and
is pure — it computes over declared data and has no side effects. Orchestration acts on the answers
and touches the filesystem, git and other processes. Keeping the analysis pure is what makes the
five safety rules unit-testable without a repository on disk.

The `developer` schema lives in the shared `NexusPlatform` database. It is the strongest candidate
of any layer to move to its own database at P3, because once autonomous runs begin it carries the
highest write volume in the system — see `DATABASE_ARCHITECTURE.md` §7.

---

## 4. The entity model

Twenty-one entities in six groups. Every one carries the `Id` / `Seq` / `Ref` pattern from
`DATABASE_STANDARDS.md`; the `Ref` prefixes below are the human-readable identifiers.

### 4.1 The work graph — `M-07-1.1`, `M-07-7.1`, `M-07-7.2`

| Entity | Is | Ref |
|---|---|---|
| `ProductDevelopment` | Everything being built for one GOVERNANCE `Product`. Hangs off a `ProductId`, never a name | `PDV-` |
| `Module` | A coherent division of a product — its own subsystem, not a folder | `MOD-` |
| `Requirement` | What someone wants. Traces down to the features and work items that satisfy it | `REQ-` |
| `Release` | A grouping of work carrying a **maturity** level, orthogonal to environment | `REL-` |
| `Milestone` | A dated, outcome-bearing unit. Carries phase, outcome and a DATA `DocumentRef` | `MST-` |
| `Feature` | A capability within a milestone | `FEA-` |
| `WorkItem` | **The unit of parallelism.** One worker, one branch, one worktree, one review | `WKI-` |
| `Task` | A step inside a work item | `TSK-` |
| `Subtask` | A step inside a task, where the depth is warranted | `SBT-` |

Depth is a function of phase, not importance: near work decomposes to subtask, distant work stops at
milestone or feature. Fabricating tasks for a system whose requirements are unknown produces a graph
that looks planned and is not.

`WorkItem` is the unit everything else in this layer is sized against. A work item that cannot be
owned by one worker in one worktree is not a work item — it is a feature that has not been broken
down. `DEVELOPMENT_WORKFLOW.md` §7 owns the sizing rules.

### 4.2 Analysis — `M-07-1.1`, `M-07-2.1`

| Entity | Is |
|---|---|
| `Dependency` | A typed edge between two nodes: `Blocking`, `Parallel` or `Informational` |
| `ScopeDeclaration` | What a work item will touch: projects, files, **schema contexts**, contracts |

`ScopeDeclaration` is built with the work graph at `M-07-1.1` rather than with the analysis feature
at `M-07-2.x`, and the sequencing is deliberate. Scope is what makes parallel safety computable, and
adding it later means backfilling a declaration onto every work item that already exists. A declared
scope is cheap on creation and expensive to reconstruct.

### 4.3 Execution — `M-07-3.1`

| Entity | Is |
|---|---|
| `Worker` | An execution identity with a capability profile: repositories, languages, permitted risk level. Kinds are `Human`, `CodingAgent`, `Autonomous` |
| `WorkerAssignment` | The binding of one worker to one work item, holding the branch and the worktree path. Unique on active worktree path |
| `DevelopmentRun` | One session of work: start, end, terminal state |

`DevelopmentRun` is deliberately not called `Session`. CORE owns `Session` — a signed-in user
session — and the Chat aggregate conflates the two. Two facts, two names.

### 4.4 Interpretation — `M-07-4.1`, `M-07-5.1`

| Entity | Is |
|---|---|
| `BuildRecord` | DEVELOPER's reading of a DELIVERY `PipelineRun`: did this build satisfy this work item |
| `TestRun` | Test counts and outcome attached to a run |
| `Review` | Reviewer, decision, reason. The reviewer is a CORE `User`, not a string |
| `IntegrationRun` | One merge of one work branch into its integration branch, and its verification |
| `DevelopmentResult` | The completed work item with its evidence references |

### 4.5 Derived state — `M-07-5.2`

| Entity | Is |
|---|---|
| `ProgressState` | Computed completion. Never entered by hand |
| `StatusHistory` | Append-only transitions with actor and reason |

`ProgressState` is a materialisation of a derivation, not a fact. §12 governs when it is allowed to
produce a number at all.

---

## 5. The scope trunk and where DEVELOPER attaches

PRODUCT CORE owns the reusable trunk. DEVELOPER extends it downward and owns nothing above the
attachment point:

```
Workspace          06 PRODUCT CORE
└── Project        06 PRODUCT CORE
    └── Subproject 06 PRODUCT CORE   ← the boundary
        └── Release        07 DEVELOPER
            └── Milestone  07 DEVELOPER
                └── Feature
                    └── WorkItem
                        └── Task
                            └── Subtask
```

The trunk is reusable because it is not development's. A CRM opportunity pipeline, a machine build
and a document workspace all hang off `Workspace → Project → Subproject` without inheriting a
single development concept. If DEVELOPER owned `Project`, every product would import development
structure to get a project, and the trunk would stop being reusable the day it was built.

At `M-07-1.1` DEVELOPER registers `Milestone`, `Feature`, `WorkItem` and `Task` as PRODUCT CORE
**scope kinds** (`M-06-1.2`). Registration is what later lets a conversation be held against a
milestone without EXPERIENCE learning what a milestone is — see §15 and
`EXPERIENCE_ARCHITECTURE.md`.

---

## 6. The dependency graph — `M-07-2.1`

Three edge kinds, one traversal, two hard rules.

| Kind | Meaning |
|---|---|
| `Blocking` | The target cannot start until the source completes |
| `Parallel` | Related, and explicitly safe to run together |
| `Informational` | Worth knowing, constrains nothing |

**Transitivity is computed, not stored.** If A blocks B and B blocks C, then C is blocked by A, and
that must be derivable without anyone having declared it. A dependency model that only answers
direct edges will report a work item as ready when it is two hops from its blocker, which is the
failure mode that makes parallel scheduling actively dangerous rather than merely inefficient.

**Cycles are rejected at write time, with the cycle path named.** Not detected at read time, not
reported as a warning. A cycle in a dependency graph makes every downstream answer meaningless, and
an error that says "cycle detected" without naming the path is an error nobody can act on.

`Blocked` is derived from unmet blocking dependencies. It is never entered by hand. A human-entered
blocked flag disagrees with the graph the moment the blocker clears, and nothing detects it.

---

## 7. Parallel safety — the data that makes it computable

The five rules are stated in `_FACTS.md` and their workflow consequences in
`DEVELOPMENT_WORKFLOW.md` §4. What belongs here is the **architectural** question: what data each
rule reads, and therefore what must be declared for the rule to be answerable at all.

| Rule | Reads | Fails when |
|---|---|---|
| 1 — no dependency path | `Dependency`, transitively closed | Any blocking path exists between the two, at any depth |
| 2 — no file or project overlap | `ScopeDeclaration.Projects`, `.Files` | The intersection is non-empty |
| 3 — **no shared schema mutation** | `ScopeDeclaration.SchemaContexts` | Both declare the same DbContext |
| 4 — no shared contract mutation | `ScopeDeclaration.Contracts` | Both mutate a shared boundary assembly |
| 5 — not both high risk | Work item risk, declared | Both are high |

**Rule 3 is the one most often got wrong, and it is worth understanding why.** The intuition is that
two migrations touching different tables cannot conflict, because they modify disjoint objects. That
intuition is wrong. EF Core migrations do not conflict on tables — they conflict on the **model
snapshot**, a single generated file per `DbContext` describing the entire model. Two migrations
generated concurrently against one context each rewrite that snapshot from their own view of the
model, and the second one to merge silently drops the first one's changes from the snapshot while
leaving its migration file in place. The build stays green. The next migration generated is computed
against a snapshot that no longer matches the database. The failure surfaces two or three work items
later, in code nobody touched.

Which is why `SchemaContexts` is a **distinct field** on `ScopeDeclaration` rather than being
inferred from the project list. A work item can touch `Nexus.Developer.Infrastructure` without
generating a migration, and one that does generate a migration must say so explicitly. The
`schema_conflict_group` marker on milestones in `nexus-roadmap.yaml` is the same rule applied one
level up.

The analysis is required to reproduce the parallelisation matrix from declared data alone. If the
answer needs a human to remember something that is not in a `ScopeDeclaration`, the declaration is
incomplete, and the rule engine is guessing.

Every negative verdict names the rule and the conflicting node. "Cannot run together" is not an
answer; "cannot run together: rule 3, both declare `NexusPlatformDbContext`" is.

---

## 8. The worker model — `M-07-3.1`

A `Worker` is an execution identity, not a person and not a process. Three kinds:

| Kind | Is | Governance |
|---|---|---|
| `Human` | A person working in their own checkout | Reviews their own work's peer review only |
| `CodingAgent` | An AI agent working under a human's direction | Every integration needs a human `Review` |
| `Autonomous` | An agent dispatched by DEVELOPER itself | `M-07-3.2` and later; still needs a human `Review` |

The capability profile declares repositories, languages and **permitted risk level**. Risk is on the
profile rather than decided per assignment because an agent that may modify a security boundary is a
different agent from one that may rename a variable, and that distinction must be a property of the
worker, not a judgement made under time pressure at assignment time.

A `WorkerAssignment` binds one worker to one work item and carries the branch and worktree path.
Uniqueness is enforced on the **active worktree path**: two assignments cannot claim the same
directory. That constraint is what makes three simultaneous workers a checkable property rather than
a hope — the GATE A acceptance test asserts three distinct paths with overlapping timestamps.

---

## 9. Worktree isolation — architecture, not preference

The commands and lifecycle are `GIT_WORKFLOW.md` §5. The architectural rule and the reason it is
architectural:

> **A worktree is allocated as a sibling of the repository. Never nested inside it, never inside
> another worktree, never inside an agent's working directory.**

```
C:\Personal\Nexus.Experience\                        the repository
C:\Personal\Nexus.Experience.work\WI-07-1.1.1-a\     worker A
C:\Personal\Nexus.Experience.work\WI-07-1.1.1-b\     worker B
```

This is a lesson from **2026-08-20**, not a style choice. Windows holds a lock on the working
directory of a running process. A worktree nested inside a directory an agent is operating from
cannot be renamed or removed while that agent runs, and the failure presents as a permissions error
rather than as a lock — which sends whoever is debugging it to file ACLs instead of to process
handles. The cleanup then fails silently in automation, orphaned worktrees accumulate, and
`git worktree prune` cannot reclaim a path that is still held.

Sibling allocation is therefore a **property of the path allocator in
`Nexus.Developer.Orchestration`**, tested at `T-07-3.1.2.2`, not a convention in a document. The
acceptance criterion on `M-07-3.1` states it directly: a worktree path is allocated as a sibling of
the repository, never nested inside it.

Isolation has six dimensions and the one most often missed is the database — two workers running
`dotnet ef database update` against one LocalDB instance interleave, and the second one's migration
history disagrees with its snapshot. That is rule 3 arriving from a different direction.
`GIT_WORKFLOW.md` §5.3 holds the full table.

---

## 10. Build and test references — DELIVERY produces, DEVELOPER interprets

The same event produces two rows in two layers, and the split is the point.

| Layer | Row | Question it answers |
|---|---|---|
| 08 DELIVERY | `PipelineRun`, `BuildArtifact` | What ran, on what commit, with what outcome |
| 07 DEVELOPER | `BuildRecord`, `TestRun` | Does that run satisfy *this work item* |

DELIVERY publishes a versioned, machine-readable result artifact at `M-08-1.3` — branch, commit,
outcome, test counts. DEVELOPER ingests it at `M-07-4.1`. **The join key is the branch name**,
because the branch is derived from the work item id (`work/<id>` off `integration/<milestone>`), and
that makes the correspondence mechanical rather than a lookup someone maintains.

Three consequences, each an acceptance criterion:

1. **A result whose branch matches no active assignment is rejected**, not stored speculatively. An
   orphan build record attached to nothing is a fact with no owner.
2. **Three concurrent runs produce three distinct build records on three branches.** No shared
   "latest build" state exists, because latest is meaningless when three workers are active.
3. **A failing build blocks its own work item and no other.** Failure isolation is verified by
   deliberately failing worker B and confirming A and C complete unaffected — an integration test at
   `T-07-4.1.2.1`, and one of the nine assertions in the GATE A acceptance test.

DEVELOPER never runs a build. If it needs a build it needs a pipeline, and the pipeline belongs to
DELIVERY. See `DELIVERY_ARCHITECTURE.md`.

---

## 11. Review and controlled integration — `M-07-5.1`

**Review.** A `Review` carries a reviewer, a decision and a reason. The reviewer is a CORE `User`.
A rejected review returns the work item to its worker with the reason recorded — it does not delete
the work, and it does not leave the rejection in a comment thread that the next session cannot see.

The structural rule: **a run cannot integrate without a recorded human decision.** Not "should not".
The integration runner reads for a `Review` and refuses without one. This holds at every autonomy
level in §14, including the ones where DEVELOPER dispatches its own work.

**Controlled integration.** Parallel work merges deliberately, in order, each merge verified:

```
work/<id>-a ─┐
work/<id>-b ─┼─→ integration/<milestone> ─→ main
work/<id>-c ─┘   sequential, each merge verified green
```

Three properties make this "controlled" rather than "merged":

| Property | Why |
|---|---|
| **Sequential** into the integration branch | Three simultaneous merges produce a conflict resolution nobody reviewed |
| **Each merge verified green** before the next | Otherwise a red integration build cannot be attributed to a merge |
| **The batch halts on a red integration build** | Continuing produces a second failure that masks the first |

Optimistic merging is the alternative and it is worse in exactly the case this layer exists for:
three parallel work items whose individual builds were all green. Individually green and jointly
broken is the normal outcome of parallel work, and it is only cheap to diagnose if the merges were
ordered.

At `M-09-1.3` the ASSURANCE quality gate is consulted before integration completes: a work item
cannot integrate while a mandatory acceptance criterion is unverified, and the gate names the
failing criterion rather than returning a bare false. See `ASSURANCE_ARCHITECTURE.md`.

`DevelopmentResult` closes the loop — every completed work item carries one, with references to the
evidence that supports it.

---

## 12. Derived progress and the `BreakdownComplete` honesty rule — `M-07-5.2`

Progress is computed from structured work: completed children over total children, weighted by
estimate where an estimate exists. It is never typed in.

The rule that matters more than the arithmetic:

> **A parent not marked `BreakdownComplete` reports "not estimable", not a percentage.**

A milestone with three declared work items out of an eventual twenty reports 33% and means nothing —
and it means nothing in the most damaging possible way, because 33% is a number, numbers get put in
reports, and a report is where a schedule commitment comes from. Derived progress on an incomplete
breakdown is worse than no progress at all, because it looks authoritative.

`BreakdownComplete` is a human declaration: *I have finished decomposing this node.* Until it is set,
the node is honest about not knowing. The unit test at `T-07-5.2.1.2` asserts exactly this — three of
twenty declared children reports not estimable, not 15 percent.

The same principle governs the rest of the derived set. `Blocked` is derived from dependencies.
`DevelopmentStage` is derived from milestone states. `DevelopmentHealth` is derived from blocked
items and failing builds. A manual override of any of them is possible, and it **records who set it
and why** — an override with no author is indistinguishable from a bug.

`DATA_OWNERSHIP.md` §5 holds the full derived-versus-owned table.

---

## 13. Human governance

DEVELOPER is designed so that autonomy increases without the human decision points moving. Four are
structural and do not relax at any autonomy level:

| Decision | Why it stays human |
|---|---|
| **Integration approval** | A `Review` is required before any merge. §11 |
| **`BreakdownComplete`** | Declaring a decomposition finished is a judgement about unknown work. §12 |
| **Waiving a quality gate** | Requires a recorded `Deviation` with reason, approver and expiry — ASSURANCE, `M-09-1.3` |
| **Accepting a proposed work item** | Every proposal from `M-07-9.1` requires human approval before entering the graph |

And two absolutes inherited from ASSURANCE, restated here only because DEVELOPER is where an agent
would attempt them: **no agent may create, modify or waive a safety-critical acceptance criterion**,
and a safety-critical criterion cannot be waived by the ordinary deviation path at all
(`M-09-7.2`).

Everything else — analysis, dispatch, build interpretation, progress — is designed to run without a
human, because the value of the layer is that it removes the coordination work, not the judgement
work.

---

## 14. V1a and what comes after

This is the most important distinction in this document, and it is the one most likely to be
softened by wishful reading.

### 14.1 V1a — the GATE A minimum

**V1a has no conversation surface.** It has an API (`M-07-1.2`) and a work-graph view. A person
interacts with DEVELOPER by calling endpoints and reading a structural view of the graph. This is
deliberate and it was a change made in v2.2: EXPERIENCE contributes **nothing** to GATE A, so
DEVELOPER cannot have a chat surface at GATE A without building the conversation engine first, which
is precisely the heavy foundation GATE A exists to get in front of.

| V1a capability | Milestone |
|---|---|
| Work graph aggregates, `ScopeDeclaration`, roadmap import | `M-07-1.1` |
| Graph query and mutation API, subtree retrieval, re-parenting | `M-07-1.2` |
| Dependency graph with transitive closure and cycle rejection | `M-07-2.1` |
| The five parallel-safety rules and the six-way classification | `M-07-2.2` |
| `Worker`, `WorkerAssignment`, `DevelopmentRun`, branch and worktree coordination | `M-07-3.1` |
| Build and test record ingestion, failure isolation | `M-07-4.1` |
| `Review` and controlled integration | `M-07-5.1` |
| Derived progress and `StatusHistory` | `M-07-5.2` |
| **GATE A acceptance** — three items planned, isolated, built, tested, evidenced, reviewed, integrated simultaneously | `M-07-5.3` |

`M-07-5.3` is where GATE A closes for the whole system. Nine assertions, and the one that states the
purpose most plainly: *no step required a human to read a log and retype a result.*

### 14.2 V1b and later — everything else

| Capability | Milestone | Phase / Gate |
|---|---|---|
| Autonomous dispatch — the V1a→V1b transition | `M-07-3.2` | P2 / GATE B |
| Model assignment and run cost | `M-07-3.3` | P2 / GATE B |
| Scope resolver for the work graph | `M-07-6.1` | P2 / GATE B |
| **Developer conversation surface** | `M-07-6.2` | P2 / GATE B |
| Developer dashboard | `M-07-6.3` | P2 / GATE B |
| `Requirement` and coverage | `M-07-7.1` | P2 / GATE B |
| `Release` and maturity | `M-07-7.2` | P2 / GATE B |
| Product and module designer | `M-07-8.1` | P3 |
| Schema and API definition | `M-07-8.2` | P3 |
| Capability packs and technology profiles | `M-07-8.3` | P3 |
| Outcome-driven work proposal | `M-07-9.1` | P5 |

Two notes on the P3 designer group. `M-07-8.3` is where **capability packs** land, and it carries
the architecture test that forbids branching on product identity anywhere in the solution — *Vault
equals Web plus Mobile plus Desktop plus Documents plus AI plus Security plus Offline Sync, declared
not coded*. And `M-07-7.2` carries the maturity rule: release maturity uses Idea through End of
Life, never Dev/Test/Prod, and a Beta release running in Production must be expressible rather than
contradictory. `DELIVERY_ARCHITECTURE.md` holds the environment half of that pair.

---

## 15. Boundaries with the sibling layers

| Layer | The seam |
|---|---|
| 03 GOVERNANCE | `ProductDevelopment` references a `ProductId`. DEVELOPER never names a product |
| 06 PRODUCT CORE | DEVELOPER extends below `Subproject` and registers its scope kinds |
| 08 DELIVERY | DELIVERY produces `PipelineRun`; DEVELOPER interprets it as `BuildRecord`. §10 |
| 09 ASSURANCE | DEVELOPER owns `Requirement`; ASSURANCE owns `AcceptanceCriterion` and blocks integration |
| 04 AI | `DeveloperAgent` (`M-04-3.1`) receives `ContextItem`s only. It holds **no** DEVELOPER type |
| 11 EXPERIENCE | DEVELOPER implements `IScopeResolver` (`M-07-6.1`); EXPERIENCE learns nothing about milestones |
| 02 DATA | A `Milestone` holds a `DocumentRef`. Specifications live in DATA |

The AI and EXPERIENCE rows are the same architectural move made twice: DEVELOPER flattens its
structure into a neutral shape (`ContextItem`, `ContextBundle`) and hands it over. The receiving
layer stays ignorant of what a milestone is. `M-07-6.1` asserts it as a test — EXPERIENCE contains no
DEVELOPER type, and `Nexus.Intelligence.*` contains no DEVELOPER type.

---

## 16. Future autonomy levels

Three levels. Each is gated by human approval, and the gate does not move as the level rises — what
rises is how much coordination work happens without one.

### Level 1 — Advisory (GATE A, V1a)

DEVELOPER answers questions and records facts. A human decides what to start, starts it, and
approves what merges. The system's contribution is that the answers are computed from structure
rather than recalled from a transcript: *these three are safe together, that one is blocked by a
dependency two hops away, this milestone cannot report progress because its breakdown is not
declared complete.*

### Level 2 — Dispatching (`M-07-3.2`, P2 / GATE B)

DEVELOPER starts, monitors and completes runs itself. It selects the next parallel-safe batch,
allocates worktrees, dispatches workers, ingests results and presents the batch for review. A hung
run is detected and escalated rather than blocking the batch; a failed dispatch retries under
AUTOMATION policy (`M-05-1.2`) and stops after a bounded count.

What has **not** changed: a human still approves every integration. Dispatch removes the
orchestration work, not the judgement.

### Level 3 — Proposing its own work (`M-07-9.1`, P5)

DEVELOPER derives candidate work items from evidence it already holds — a recurring build failure, an
OPERATIONS incident (`M-10-3.1`), an evaluation regression (`M-04-5.3`) — and proposes them with the
evidence attached.

The constraint is explicit in the acceptance criterion: **every proposal requires human approval
before entering the graph.** A system that can add work to its own backlog without approval has no
bounded scope, and an unbounded scope is not autonomy — it is an absence of governance. The
proposal, its evidence and the approval are all rows.

Level 3 requires the result loop (`M-04-5.3`), which links advice to the outcome it actually
produced. Proposing work without knowing whether previous proposals helped is guessing at scale.

---

## 17. Current state, told honestly

| Thing | State |
|---|---|
| `Nexus.Developer` repository | **Does not exist** |
| Any of the twenty-one entities | **None exist** |
| Structured development state | `nexus-roadmap.yaml`, hand-written, hand-validated |
| The one `WorkItem` that exists | Stranded in `Nexus.Products.Chat.Domain`, absorbed at `S-07-1.1.1.2.2` |
| CI to produce build records from | **None.** `.github/workflows` in Nexus.Platform is empty; the other two repositories have no `.github` at all |
| Behaviour tests in the whole system | **Two** |

`nexus-roadmap.yaml` is the transitional artifact this layer exists to replace. It is hand-written,
mechanically validated, and exists to be imported — `M-07-1.1`, work item `WI-07-1.1.3`, idempotent,
reporting any dependency id that does not resolve. Until that import runs, the roadmap is
authoritative for structured work and this document is authoritative for the shape it will be
imported into.

---

## 18. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Whether `developer` splits to its own database | Write volume once autonomous runs begin, at P3 | Not yet decided — `DATABASE_ARCHITECTURE.md` §7 |
| How a `Worker` capability profile is expressed | `M-07-3.1`; must be declarative, not code | Not yet decided |
| Estimate unit and whether estimates are mandatory | `M-07-5.2` weighting needs one; the honesty rule works without | Not yet decided |
| Whether `Module` is required or optional between product and milestone | First real `ProductDevelopment` will show it | Not yet decided |

---

## 19. References

- `DEVELOPMENT_WORKFLOW.md` — the state model, entry conditions, the four-layer test ownership
  split, work item sizing, the six-way classification.
- `GIT_WORKFLOW.md` — branch naming, worktree commands and lifecycle, the 2026-08-20 incident, merge
  strategy, worker isolation dimensions.
- `DELIVERY_ARCHITECTURE.md` — what produces the results this layer interprets.
- `ASSURANCE_ARCHITECTURE.md` — acceptance criteria, evidence and the gate that blocks integration.
- `EXPERIENCE_ARCHITECTURE.md` — `IScopeResolver` and the conversation surface DEVELOPER gains at P2.
- `AI_ARCHITECTURE.md` — the `ContextBundle` seam and `DeveloperAgent`.
- `DATA_OWNERSHIP.md` — §4 the entity list, §5 derived versus owned, §6 names that appear twice.
- `DEFINITION_OF_DONE.md` — what a `DevelopmentResult` must be able to claim.
- `DATABASE_STANDARDS.md` — Id/Seq/Ref, cascade rules, migration conventions.
- `LAYER_MODEL.md` — layer 07 in the context of the other eleven.
