# Development Workflow

> **SUPERSEDED NUMBERING NOTICE (2026-09-05):** This document's own header
> (`**Owner:** DEVELOPER (Layer 07)`) and its four-question ownership table
> (07 DEVELOPER / 08 DELIVERY / 09 ASSURANCE / 10 OPERATIONS) reflect the v2.1
> twelve-layer model, in which 07 DEVELOPER and 12 PRODUCTS were numbered
> Platform layers. Per the approved v2.2 renumbering (`LAYER_MODEL.md` §2.2,
> §4a), Nexus Forge and Nexus Developer (the product) now sit OUTSIDE the ten
> numbered Platform layers, and DELIVERY/ASSURANCE/OPERATIONS/EXPERIENCE are
> renumbered 07/08/09/10. The workflow questions themselves ("what must be
> proven", "did it run", etc.) remain valid engineering framing. Re-deriving
> this document's own header and table against the v2.2 model is
> Wave-D-adjacent decision work and is explicitly NOT done in this batch.

**Status:** Active
**Owner:** DEVELOPER (Layer 07)
**Last updated:** 2026-08-21
**Layer:** 07 DEVELOPER, with DELIVERY (08), ASSURANCE (09) and OPERATIONS (10) cross-cutting
**Authoritative for:** the path from requirement to operations, the state model and its entry
conditions, evidence required at each transition, the four-layer test ownership split, the
parallel-safety rules, and the six-way work classification.

Not authoritative for: branch mechanics and worktrees — `GIT_WORKFLOW.md`; what counts as proof —
`ASSURANCE_STANDARDS.md`; schema rules — `DATABASE_STANDARDS.md`; endpoint shape —
`API_STANDARDS.md`.

---

## 1. The path

```
Requirement → Architecture → Milestone → Work Item → Task → Worker → Branch → Worktree
   → Code → Build → Test → Assurance → Review → Integration → Delivery → Operations
```

Sixteen stages. Each has an owner, an entry condition and required evidence. Nothing skips a stage;
a stage may be trivially satisfied, but it is never absent.

| Stage | Owner | Produces |
|---|---|---|
| Requirement | DEVELOPER | A stated need with an acceptance criterion |
| Architecture | DEVELOPER | Layer, schema, contract and boundary decisions |
| Milestone | DEVELOPER | A dated, dependency-ordered unit of outcome |
| Work Item | DEVELOPER | A scoped, single-owner unit of change |
| Task | DEVELOPER | A step within a work item |
| Worker | DEVELOPER | An assignment of a work item to a human or agent |
| Branch | DELIVERY | A named line of change, joinable to the work item |
| Worktree | DELIVERY | An isolated filesystem for that branch |
| Code | Worker | The change |
| Build | DELIVERY | A compiled artefact and a build record |
| Test | DELIVERY | Executed tests and a test record |
| Assurance | ASSURANCE | A verdict against the acceptance criterion |
| Review | DEVELOPER | A recorded human decision |
| Integration | DEVELOPER | A verified sequential merge |
| Delivery | DELIVERY | A deployed artefact in an environment |
| Operations | OPERATIONS | A healthy running system |

### 1.1 Identifier convention

```
L-<nn>                      layer
F-<nn>-<n>                  feature
M-<nn>-<n>.<n>              milestone
WI-<nn>-<n>.<n>.<n>         work item
T-<nn>-<n>.<n>.<n>.<n>      task
S-<nn>-<n>.<n>.<n>.<n>.<n>  subtask
```

The first two digits are always the owning layer. `M-02-1.5` is DATA's milestone; `WI-08-2.1.1` is
DELIVERY's work item. A branch named `work/WI-02-1.5.1-a` is joinable to a work item by string
match, which is exactly how **M-07-4.1 Build and test records** attributes a CI result — and why it
rejects a result whose branch matches no active assignment.

---

## 2. The state model

An item advances only when its entry condition is met and its evidence exists. "Evidence" means an
artefact someone else could inspect, not an assertion.

### 2.1 State table

| # | State | May enter when | Required evidence | Owner |
|---|---|---|---|---|
| 1 | **Requirement stated** | A need is written in checkable language | Requirement record; at least one acceptance criterion that could fail | DEVELOPER |
| 2 | **Architecture decided** | The requirement names its layer, schema, contracts and dependency direction | Layer and schema assignment; contract changes listed; an ADR where the decision is durable | DEVELOPER |
| 3 | **Milestone defined** | Requirements are grouped into a stated outcome with dependencies resolved | Milestone with outcome, `depends_on`, acceptance criteria, `parallel_safe` flag | DEVELOPER |
| 4 | **Work item scoped** | The milestone is decomposed and each item has a single owner and a bounded scope | Scope: projects, schemas, contracts. Dependency edges. Conflict group | DEVELOPER |
| 5 | **Task decomposed** | The work item has ordered steps a worker can execute without further design | Task list; subtasks where the phase requires that depth | DEVELOPER |
| 6 | **Worker assigned** | The item is unblocked and passes the parallel-safety check against everything in flight | Assignment record; classification verdict (§4); the six-way class (§5) | DEVELOPER |
| 7 | **Branch created** | The worker is assigned and the branch name matches the work item id | Branch off the integration branch, named per `GIT_WORKFLOW.md` §4 | DELIVERY |
| 8 | **Worktree created** | The branch exists and a sibling directory is free | Worktree in a sibling directory; isolated build output; isolated database | DELIVERY |
| 9 | **Code complete** | Every task is done, it builds locally, and the change stays inside the declared scope | Commits with `Refs:`; a diff confined to the declared projects and schemas | Worker |
| 10 | **Build green** | The change compiles and the architecture tests pass | Build record: branch, commit, outcome. **TARGET — M-08-1.2, M-08-1.3** | DELIVERY |
| 11 | **Tests executed** | The build is green and every requested test has run | Test record: counts and outcome, matched to the work item by branch | DELIVERY |
| 12 | **Assurance verdict** | Every mandatory acceptance criterion has a verification run with linked evidence | VerificationRun with method, criterion, actor, timestamp, verdict. **TARGET — M-09-1.2** | ASSURANCE |
| 13 | **Reviewed** | Build green, tests executed, assurance verdict recorded | Review with reviewer (a real Layer 01 User), decision and reason. **TARGET — M-07-5.1** | DEVELOPER |
| 14 | **Integrated** | Review approved and the integration branch is green | Sequential merge, verified green after the merge, not before | DEVELOPER |
| 15 | **Delivered** | The integration branch merged to `main` and an artefact was produced and deployed | Artefact with retention; deployment record. **TARGET — M-08-3.1, M-08-5.1** | DELIVERY |
| 16 | **Operating** | The deployed system reports healthy and is observable | Health check passing; correlation-traceable request. **TARGET — M-10-1.1, M-10-2.1** | OPERATIONS |

### 2.2 Rules that govern every transition

| Rule | Statement |
|---|---|
| No skipping | A state is entered from its predecessor, never from further back |
| Evidence before entry | The evidence exists before the transition, not as a follow-up |
| Evidence is an artefact | A build record, a test result, a stored verdict — not a sentence in a PR |
| One direction for failure | A failure returns the item to state 9, with the reason recorded |
| A rejection is recorded | **M-07-5.1**: a rejected review returns the work item to its worker with the reason recorded |
| No self-approval | The reviewer in state 13 is never the worker from state 9 |
| Unverified criterion blocks | A work item cannot integrate while a mandatory acceptance criterion is unverified |

That last rule is the Foundation Gate exit criterion, stated verbatim. It is the difference between
"the build is green" and "the requirement is satisfied", and it is the whole reason ASSURANCE is a
layer rather than a habit.

### 2.3 Reverse transitions

| From | To | Trigger |
|---|---|---|
| 10 Build green | 9 Code complete | Build red |
| 11 Tests executed | 9 Code complete | A test fails |
| 12 Assurance verdict | 9 Code complete | Verdict is Fail |
| 13 Reviewed | 9 Code complete | Review rejected |
| 14 Integrated | 9 Code complete | The integration build goes red — the batch halts |
| 16 Operating | New work item | A production defect is new work, not a reopened item |

An integrated item that later fails does not "un-integrate". The merge is reverted — which is why
work branches squash-merge, per `GIT_WORKFLOW.md` §10 — and the fix is the same work item returning
to state 9.

### 2.4 What the state model looks like today

**CURRENT: states 1 to 9 are performed by people, unrecorded. States 10 to 16 do not exist as
mechanisms.** There is no CI, so there is no build record and no test record. There is no ASSURANCE
schema, so there is no verdict. There is no deployment pipeline, so nothing is delivered. There is
no correlation id, so nothing is observable end to end.

This document describes the target discipline and names what closes each gap. A developer reading it
should be able to work correctly today — commit to a branch, build locally, run the two tests that
exist, request review — while knowing precisely which parts of the machinery are still to be built:

| Missing mechanism | Milestone |
|---|---|
| Work graph aggregates (Requirement → Task) | M-07-1.1, M-07-7.1 |
| Dependency graph | M-07-2.1 |
| Parallel-safety evaluation | M-07-2.2 |
| Worker, assignment and run | M-07-3.1 |
| CI pipelines | M-08-1.2 |
| Machine-readable results | M-08-1.3 |
| Build and test records ingested | M-07-4.1 |
| Acceptance criteria and verification methods | M-09-1.1 |
| Evidence and verdict | M-09-1.2 |
| Quality gate | M-09-1.3 |
| Review and controlled integration | M-07-5.1 |
| Deployment | M-08-5.1 |
| Observability | M-10-1.1, M-10-2.1 |

---

## 3. Test ownership — four layers, four questions

Testing is not one activity owned by one group. Four layers each answer a different question, and
confusing them is how a system ends up with a green build and an unsatisfied requirement.

| Layer | Question | Owns |
|---|---|---|
| **07 DEVELOPER** | *What must be proven?* | What needs testing, which requirement it belongs to, development test state, the test request, the development result |
| **08 DELIVERY** | *Did it run, repeatably?* | Build, unit test execution, integration test execution, pipeline run, artifacts, environment deployment |
| **09 ASSURANCE** | *Was the requirement satisfied?* | Test plan, acceptance criteria, verification, validation, inspection, pass/fail qualification, evidence, quality gates, release acceptance |
| **10 OPERATIONS** | *Does it stay healthy?* | Health, availability, performance, runtime alerts, production incidents, SLO/SLA evidence |

Stated as one sentence: **DEVELOPER asks what must be proven. DELIVERY executes repeatable technical
verification. ASSURANCE determines whether the requirement has actually been satisfied. OPERATIONS
proves the running system remains healthy.**

### 3.1 Why the split matters in practice

DELIVERY can report "247 tests passed" while ASSURANCE reports "the requirement is unverified",
and both are correct. The tests proved the code does what it was written to do; nothing proved the
code does what was asked for. Only ASSURANCE can close that gap, and only if there is an acceptance
criterion to close it against.

The inverse also happens: ASSURANCE can accept on the basis of a demonstration while DELIVERY has no
automated test at all. That is legitimate for a criterion whose verification method is
Demonstration — but it must be recorded as such, so nobody later assumes a regression test exists.

`ASSURANCE_STANDARDS.md` is authoritative on methods, evidence and verdicts. This document is
authoritative on when the verdict is required.

### 3.2 The handoffs

| Handoff | What crosses |
|---|---|
| DEVELOPER → DELIVERY | A test request: what must be proven, on which branch |
| DELIVERY → DEVELOPER | A machine-readable result, joined by branch name (M-08-1.3 → M-07-4.1) |
| DELIVERY → ASSURANCE | Evidence: a pipeline run reference, not a log a human retypes |
| ASSURANCE → DEVELOPER | A verdict, and a gap report for criteria with no method |
| DELIVERY → OPERATIONS | A deployment record |
| OPERATIONS → DEVELOPER | An incident, which becomes a new requirement |

The join key from DELIVERY to DEVELOPER is the branch name. This is why branch naming is a hard rule
in `GIT_WORKFLOW.md` §4 rather than a style preference.

---

## 4. Parallel safety

Two work items may run simultaneously only if **all five** conditions hold. Failing any one means
they are sequenced, not negotiated.

| # | Condition | Why |
|---|---|---|
| 1 | No dependency path between them, **transitively** | A depends on B depends on C means A and C are not independent |
| 2 | No file or project scope overlap | Two workers editing one file produce a conflict, not progress |
| 3 | **No shared schema mutation** | Two EF migrations on one DbContext conflict on the model snapshot **even when they touch different tables** |
| 4 | No contract mutation on a shared boundary | Two changes to `Nexus.Platform.Contracts` collide on the type both consume |
| 5 | Not both high risk | Two simultaneous high-risk changes make attribution of a failure impossible |

### 4.1 Rule 3 is the one that is got wrong

It reads like a scope-overlap rule and it is not. Two work items can touch entirely different tables,
in entirely different schemas, with no shared file — and still conflict, because EF Core maintains
**one model snapshot per DbContext**. Both migrations rewrite it. Git cannot merge the result into
anything meaningful, and the recovery is to discard one migration and regenerate it
(`GIT_WORKFLOW.md` §11.2).

The mechanical check: does the work item's scope declare a `schemas` entry? If two in-flight items
declare the same schema — or the same DbContext across different schemas — they share a conflict
group and must be sequenced.

The roadmap encodes this as `schema_conflict_group`, e.g. `schema:assurance`, `schema:core`. Two
items in the same group never run together, regardless of what else is true about them.

### 4.2 Rule 5 and risk

An item is high risk when any of these apply: it changes a shared contract; it changes the schema;
it touches authentication, authorization or tenant isolation; it is the first use of a technology;
or the worker has not done work of this kind before. Two such items in flight at once means a red
integration build cannot be attributed to either without bisecting.

### 4.3 Isolation is necessary but not sufficient

`GIT_WORKFLOW.md` §5.3 lists the isolation dimensions — branch, worktree, build output, database,
package cache, configuration. Isolation prevents workers from tripping over each other's *files*.
Parallel safety prevents them from tripping over each other's *model*. A perfectly isolated worker
can still produce an unmergeable migration.

### 4.4 The proof

**M-07-5.3 Foundation Gate acceptance** requires three workers, isolated, evidenced, reviewed and
integrated — simultaneously. **M-07-4.1** requires three concurrent runs producing three distinct
build records on three branches, with a failing build blocking its own work item and no other.

Until that passes, parallel work is a claim.

---

## 5. The six-way classification

Every candidate work item is classified before assignment. The classification is the output of the
parallel-safety evaluation, and it is recorded, not merely thought about.

| Class | Meaning | Action |
|---|---|---|
| **Can run now** | No dependency, no conflict with anything in flight, capacity exists | Assign |
| **Can run together** | Passes all five rules against one or more other candidates | Assign as a batch |
| **Blocked** | An external condition prevents it — a missing decision, an unavailable dependency, an unconfirmed prerequisite | Record what would unblock it; do not assign |
| **Waiting for dependency** | Another work item must complete first | Queue behind that item; the edge is explicit |
| **High conflict risk** | It passes the rules on paper but overlaps heavily with in-flight work | Assign only alone, or defer |
| **Must be sequential** | Fails rule 3 or 4 against something in flight | Queue; the conflict group is named |

The distinction between **Blocked** and **Waiting for dependency** is not pedantry. A dependency
resolves by finishing other work already on the plan. A block resolves only when someone does
something outside the plan — confirms an antivirus exclusion, chooses a logging library, gets a
decision. Blocks are invisible until they are named, and they are the most common reason a plan
stalls without anybody noticing.

Worked examples against the real system:

| Item | Class | Reason |
|---|---|---|
| M-02-1.1 Commit Stage 1b | Can run now | No dependency; the work is done and unpushed |
| M-08-2.1 Close the recovery | **Blocked** | Requires someone to open Windows Security and confirm the exclusion |
| M-08-1.4 Branch protection | Waiting for dependency | Requires M-08-1.2 pipelines to have a status check to require |
| M-02-1.5 Layer schema convention | Must be sequential | `parallel_safe: false`; every schema at once |
| M-01-2.1 Tenant isolation | Must be sequential | `parallel_safe: false`; touches every tenant-owned entity |
| M-01-5.1 Real secret resolver | Can run now | No dependencies; P0 |

---

## 6. Work item scope

A work item declares its scope in three dimensions before a worker is assigned:

```yaml
scope:
  projects: [Nexus.Platform.Identity]
  schemas:  [identity]
  contracts: [Nexus.Platform.Contracts]
```

| Dimension | Meaning | Conflict effect |
|---|---|---|
| `projects` | The .NET projects the change may touch | Overlap fails rule 2 |
| `schemas` | The database schemas it may alter | Overlap fails rule 3 |
| `contracts` | The contract assemblies it may change | Overlap fails rule 4 |

An empty list is meaningful: `schemas: []` declares that the item makes no schema change, and a
migration appearing in that item's diff is a scope violation caught at review.

**Scope is a commitment, not an estimate.** A change outside the declared scope means the work item
was wrong: stop, reclassify, and either widen the scope with a fresh conflict check or split the
item. Silently widening scope mid-flight is how a "can run together" batch becomes a merge conflict.

---

## 7. Work item sizing

| Property | Target |
|---|---|
| Duration | Hours to a few days |
| Branch lifetime | Under a week; longer is a planning failure (`GIT_WORKFLOW.md` §3.3) |
| Owner | Exactly one |
| Migrations | At most one (`DATABASE_STANDARDS.md` §9.2) |
| Contract changes | At most one boundary |
| Acceptance criteria | At least one, and it must be able to fail |
| Reviewability | One sitting |

Decomposition depth is a function of phase, not importance. P0 and P1 decompose to subtask; P2 to
work item; P3 to milestone; P4 and P5 to feature. **Do not fabricate tasks for systems whose
requirements are unknown.** Depth is added as an item approaches execution, not in advance to make a
plan look complete.

---

## 8. The worker

A worker is a human or an agent holding an assignment. The workflow does not distinguish between
them — same branch, same worktree, same evidence, same review, same rejection path.

| Rule | Statement |
|---|---|
| One work item at a time | A worker with two assignments has two half-finished branches |
| Own worktree | Sibling directory, per `GIT_WORKFLOW.md` §5 |
| Scope-bound | A worker may not exceed the declared scope |
| Evidence-producing | The worker produces the evidence; DELIVERY executes it; ASSURANCE judges it |
| Reviewed by another | Never self-approval, agent or human |
| Permission-bound | An agent worker's permissions are in `SECURITY_STANDARDS.md` §worker permissions |

Agent-produced work receives *more* review scrutiny, not less. The failure mode of an agent worker is
plausible code that satisfies the letter of the task and misses its point — which is exactly what a
green build cannot detect and an acceptance criterion can.

---

## 9. Working in the current system

Concrete steps, for the system as it is on 2026-08-21.

### 9.1 Starting

1. Confirm the work item is classified `Can run now` or `Can run together` (§5).
2. Confirm no other in-flight item shares its schema or contract scope (§4).
3. `git fetch --prune`; branch from the integration branch.
4. Create a worktree in a **sibling** directory (`GIT_WORKFLOW.md` §5.1).
5. Restore against `nuget.config` → `C:\Personal\LocalNuGet`. **TARGET — M-08-1.1** moves this to
   GitHub Packages, because a local file feed is unreachable from any build agent.
6. `set-openai-key.ps1` if the work touches a model path. **TARGET — M-01-5.1 ISecretResolver.**

### 9.2 Building and running

| Action | Command / expectation |
|---|---|
| Build | `dotnet build` against the repository's `.slnx` |
| Run Chat API | `http://localhost:5299` |
| Run Intelligence API | Its own `launchSettings.json`; the port is unverified — read it, do not assume |
| Apply migrations | `dotnet ef database update` against LocalDB, manually |
| Known noise | "Failed to determine the https port for redirect" in development — expected, not a defect |

### 9.3 Testing

**CURRENT: exactly two behaviour tests exist across all three repositories** —
`Ranking/KeywordContextRankerTests.cs` in Nexus.Intelligence and `Chat/ChatContextBundleMapperTests.cs` in
Nexus.Experience. Three architecture test files exist. `Nexus.Platform.Tests` is a `.csproj` with zero
`.cs` files.

Run them. They are fast, they are the entire safety net, and a change that breaks one of them has
broken something real. `ASSURANCE_STANDARDS.md` owns what to add and in what order.

### 9.4 Finishing

1. Commit at every stage boundary and **push at every stage boundary** — `GIT_WORKFLOW.md` §7. This
   rule exists because of the 2026-08-20 incident, and the code that was at risk was proven code.
2. Rebase onto the integration branch; resolve conflicts in your own branch.
3. Verify the specific behaviour again after the rebase.
4. Open a pull request with the work item id and the evidence.
5. Do not merge your own work.
6. Remove the worktree and delete the branch after merge.

---

## 10. Phases

Work is scheduled by phase. Phases cut across layers; a layer's milestones scatter across phases by
dependency and value and are never grouped into one block.

| Phase | Name | Intent | Streams |
|---|---|---|---|
| **P0** | Groundwork | Make the current system safe, verifiable and single-stack. Nothing new is designed | Single |
| **P1** | Foundation Gate | The minimum capability to safely begin real product development | Single |
| **P2** | First business system and Nexus continuation | The gate opens; development splits into two permanent streams | A: business systems, B: Nexus |
| **P3** | Breadth | Stream A widens; Stream B makes each additional product cheaper | A, B |
| **P4** | Consumer products and scale | Consumer products on the matured platform | A, B, C |
| **P5** | Nexus builds Nexus | Evaluation-driven self-improvement and gated safety-critical domains | A, B, C |

Governing principles that constrain scheduling:

- Layers define responsibility, not chronological order.
- Build only the minimum foundation required before starting real business systems.
- Do not complete CORE, DATA, AI or DEVELOPER in full before building ERP.
- Independent work runs simultaneously. Do not artificially serialise.
- Architect for the future; build for today's need.
- **A feature is not complete because code was written. It is complete when its requirements are
  verified and acceptance evidence exists.**

### 10.1 The Foundation Gate

P1 exits when, and only when:

- the Foundation Gate acceptance test passes — three workers, isolated, evidenced, reviewed,
  integrated;
- every action is attributable to a real user in an enforced tenant;
- conversation is a layer, consumable by any product with its own scope;
- a work item cannot integrate while a mandatory acceptance criterion is unverified.

The first and fourth are this document's concern. The second is `SECURITY_STANDARDS.md`. The fourth
is jointly owned with `ASSURANCE_STANDARDS.md`.

The gate is explicitly **not** completeness of CORE, DATA, AI or DEVELOPER. It is the minimum at
which parallel product development stops being dangerous.

---

## 11. Architectural constraints on every work item

These are invariants. A work item that violates one is rejected at review regardless of what it
achieves.

| Invariant | Statement |
|---|---|
| Dependency direction | A layer may depend only on layers below it. 08, 09 and 10 are cross-cutting |
| No shared kernel | `Nexus.Platform.Contracts` and `Nexus.Intelligence.Contracts` never reference product types. **Currently true — keep it true** |
| Products never reference each other | Enforced physically by database separation (`DATABASE_STANDARDS.md` §2.3) |
| AI never sees product structure | It receives `ContextBundle`; `ScopeRef` is opaque to it |
| Conversation is universal; structure is contextual | EXPERIENCE owns the engine; a consumer registers a scope kind and an `IScopeResolver` |
| No product branching | No `if (Product == X)` anywhere. Capability packs are declared, not coded |
| Chat is not the engine | A standalone Chat application, if released, is a Layer 12 product consuming EXPERIENCE |

The scope hierarchy: **PRODUCT CORE owns Workspace → Project → Subproject. DEVELOPER extends
Subproject → Release → Milestone → Feature → WorkItem → Task.** DEVELOPER does not redefine
Workspace; it extends downward from Subproject.

NetArchTest enforces the first two mechanically today — `PlatformBoundaryTests.cs`,
`BoundaryRuleTests.cs`, `BoundaryTests.cs` — but only when a developer runs them.
**TARGET — M-08-1.4** makes them a pipeline gate.

---

## 12. Where this workflow is weakest today

| Weakness | Consequence | Closed by |
|---|---|---|
| No CI | Every state 10–11 transition is a human assertion | M-08-1.2, M-08-1.3 |
| No acceptance criteria | State 12 cannot be evaluated at all | M-09-1.1, M-09-1.2 |
| Two behaviour tests | State 11 proves almost nothing | Ongoing, per `ASSURANCE_STANDARDS.md` |
| No work graph | States 1–6 exist only in documents | M-07-1.1, M-07-7.1 |
| No dependency graph | Parallel safety is evaluated by hand | M-07-2.1, M-07-2.2 |
| No deployment | State 15 does not occur | M-08-5.1 |
| No observability | State 16 is unmeasurable | M-10-1.1, M-10-2.1 |
| `main` unprotected | State 14 can be bypassed entirely | M-08-1.4 |

The honest summary: **the discipline described here is currently maintained by people remembering
it.** Every milestone in the right-hand column converts a remembered rule into an enforced one, and
that conversion — not the volume of features — is what makes simultaneous development safe.

---

## 13. References

- `GIT_WORKFLOW.md` — branches, worktrees, commits, merges, conflict resolution, the incident.
- `ASSURANCE_STANDARDS.md` — acceptance criteria, verification methods, evidence, verdicts, gates.
- `DATABASE_STANDARDS.md` — the migration and model-snapshot constraint behind rule 3.
- `API_STANDARDS.md` — the contract constraint behind rule 4.
- `SECURITY_STANDARDS.md` — worker permissions and what an agent may not do.
- `CONFIGURATION_STANDARDS.md` — per-worker local configuration.
