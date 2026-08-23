# Assurance Standards

**Status:** Active
**Owner:** ASSURANCE (Layer 09)
**Last updated:** 2026-08-21
**Layer:** 09 ASSURANCE — cross-cutting
**Authoritative for:** unit, integration, contract, architecture, UI, performance and security
testing; regression; AI evaluation; machine inspection; validation; verification; acceptance;
evidence; defects; nonconformance; quality gates; release qualification; assurance profiles; test
project structure and naming.

Not authoritative for: when an item may advance — `DEVELOPMENT_WORKFLOW.md`; how tests are executed
in a pipeline — that is DELIVERY, and no pipeline exists yet; what the code under test must look
like — `DATABASE_STANDARDS.md`, `API_STANDARDS.md`, `SECURITY_STANDARDS.md`.

---

## 1. Position

ASSURANCE exists to close one gap: **a green build is not a satisfied requirement.**

DELIVERY can truthfully report that everything compiled and every test passed while the thing that
was asked for was never built. ASSURANCE is the layer that answers the different question — *was the
requirement satisfied?* — and it answers it with stored evidence, not with an opinion in a pull
request.

The four-layer test ownership split is defined in `DEVELOPMENT_WORKFLOW.md` §3 and is not repeated
here. In one line: DEVELOPER asks what must be proven, DELIVERY executes repeatable technical
verification, ASSURANCE determines whether the requirement was satisfied, OPERATIONS proves the
running system stays healthy.

---

## 2. Current reality — stated plainly

Every standard in this document is a target unless marked otherwise, because almost none of it
exists.

### 2.1 The tests that exist

| Repository | Project | Files |
|---|---|---|
| NexusAI | `Nexus.Platform.Architecture.Tests` | `PlatformBoundaryTests.cs` |
| NexusAI | `Nexus.Platform.Tests` | **NONE — a `.csproj` with zero `.cs` files** |
| Nexus.Int | `Nexus.Intelligence.Architecture.Tests` | `BoundaryRuleTests.cs` |
| Nexus.Int | `Nexus.Intelligence.Tests` | `Ranking/KeywordContextRankerTests.cs` |
| Nexus.Web | `Nexus.Products.Chat.Architecture.Tests` | `BoundaryTests.cs` |
| Nexus.Web | `Nexus.Products.Chat.Tests` | `Chat/ChatContextBundleMapperTests.cs` |

Five test files. Three of them are architecture tests using NetArchTest.

**Exactly TWO behaviour tests exist across all three repositories.** One ranks context items. One
maps a context bundle. Neither touches persistence, an endpoint, identity, tenancy, or a model
invocation.

`Nexus.Platform.Tests` is a project file with no tests in it. It has never asserted anything.

### 2.2 What follows from that

| Area | Test coverage |
|---|---|
| The Id/Seq/Ref pattern | None — proven by a log line, not a test |
| Any endpoint | None |
| Any repository | None |
| Any migration | None |
| Identity, tenancy, authorization | None — and none of it is implemented either |
| The model gateway | None |
| The turn pipeline | None |
| The frontend | None |

The `Workspace` insert that proved `Ref` and `Seq` work is evidenced by `api_run.log` at
2026-08-20 18:09 UTC. That is real evidence of a real behaviour, and it is **not** a regression
test: nothing will tell anyone if it stops working.

### 2.3 What does not exist at all

There is **no CI**. `.github\workflows\` in NexusAI is empty; Nexus.Web and Nexus.Int have no
`.github` directory. There is no acceptance criterion model, no verification method model, no
evidence store, no verdict, no quality gate and no release qualification. Nothing in this document
is currently enforced by a machine.

### 2.4 No test framework is named

The test framework in use is whatever the existing `.csproj` files declare. This document does not
name one, because naming an unverified framework would be an invention. Before writing a new test
project, read a neighbouring `.csproj` and match it. Uniformity across the three repositories
matters more than any framework preference.

**NetArchTest is confirmed in use** for architecture tests.

---

## 3. The traceability chain

This is the spine of the layer. Every element is a record, and each links to the next.

```
Requirement
   └── AcceptanceCriterion
          └── Verification / Validation Method
                 └── Test / Inspection / Evaluation
                        └── Evidence
                               └── Pass / Fail
                                      └── Release Qualification
```

| Element | Definition | Introduced by |
|---|---|---|
| **Requirement** | A stated need, owned by DEVELOPER | M-07-7.1 Requirements |
| **AcceptanceCriterion** | A checkable statement that can fail. Links to exactly one DEVELOPER Requirement or WorkItem | M-09-1.1 |
| **VerificationMethod** | How the criterion will be proven. One of five kinds (§4) | M-09-1.1 |
| **Test / Inspection / Evaluation** | The activity actually performed | M-09-3.1, M-09-4.1, M-09-6.1 |
| **Evidence** | An artefact referencing a DELIVERY PipelineRun, a document, or an uploaded artefact | M-09-1.2 |
| **VerificationRun** | Method, criterion, actor, timestamp, verdict | M-09-1.2 |
| **Pass / Fail** | A stored verdict. **A verdict of Pass with no linked evidence is rejected at write time** | M-09-1.2 |
| **Release Qualification** | An aggregate verdict over every mandatory criterion in scope | M-09-5.1 |

### 3.1 The rules that make the chain real

| Rule | Statement |
|---|---|
| Criteria must be falsifiable | Criterion text that is empty or unfalsifiable is rejected at write time |
| One requirement per criterion | An `AcceptanceCriterion` links to exactly one Requirement or WorkItem |
| No orphan work items | A work item with no criterion is **reported as a traceability gap, not silently accepted** |
| Gaps are queryable | The gap report is queryable per milestone |
| No unevidenced Pass | Pass without linked evidence is rejected at write time |
| Current verdict is derived | A criterion's verdict is derivable from its most recent run, never stored twice |
| No cross-schema FK to DEVELOPER | The link to a DEVELOPER node is polymorphic — layer, type, id — per `DATABASE_STANDARDS.md` §5.4 |

The gap report matters more than it sounds. Without it, a system with no acceptance criteria looks
identical to a system whose criteria all pass. The gap report is what makes absence visible.

### 3.2 References

`AcceptanceCriterion` carries a `Ref` on the standard pattern: a registered prefix and eight digits,
per `DATABASE_STANDARDS.md` §3.6. Evidence, runs and defects likewise. The prefixes are allocated at
M-09-1.1 and registered before first use.

---

## 4. Verification, validation and the five methods

### 4.1 The distinction

- **Verification** — was the thing built correctly, against its specification?
- **Validation** — was the correct thing built, against the actual need?

A system can be fully verified and completely invalid. Verification is answerable by machine;
validation almost never is.

### 4.2 The five verification methods

Every `VerificationMethod` is one of exactly five kinds. There is no sixth.

| Method | Definition | Typical evidence | Automatable |
|---|---|---|---|
| **Test** | Execute against defined inputs and compare to an expected result | Test run record, counts, outcome | Yes |
| **Inspection** | Examine the artefact or product directly against a characteristic | Inspection record, measurement, photograph | Partly |
| **Analysis** | Reason from models, calculations or static properties | Analysis document, tool output | Partly |
| **Demonstration** | Operate the system and observe the intended behaviour | Recording, witnessed record, log excerpt | No |
| **Evaluation** | Score behaviour against a question set and a threshold | Evaluation run, score, question set | Yes |

Each method carries the assurance profile it belongs to (§7).

**Demonstration is legitimate and dangerous.** It is the right method for "a user can sign in and see
their workspaces". It is the wrong method for anything that must keep working, because it leaves no
regression net. Where Demonstration is chosen, a follow-up criterion verified by Test is recorded as
a debt with a milestone.

---

## 5. Test types

### 5.1 Unit tests

| Property | Standard |
|---|---|
| Scope | One class or one behaviour, no I/O |
| Speed | Milliseconds; the whole suite in seconds |
| Isolation | No database, no filesystem, no network, no clock |
| Naming | `Method_Condition_ExpectedResult` |
| Assertions | One logical assertion per test |
| Determinism | No randomness, no `DateTime.UtcNow` — inject the clock |

**CURRENT: two exist.** `KeywordContextRankerTests` and `ChatContextBundleMapperTests`.

First targets, in priority order, chosen because each protects something already proven or already
load-bearing:

1. Domain aggregate invariants — `Workspace`, then `Project`. Every status transition that must be
   refused.
2. `Restore` round-trips — construct, restore, assert equality. This protects the rehydration
   pattern across all eleven aggregates.
3. `StronglyTypedIdConverters` — every converter, both directions.
4. The Intelligence turn pipeline steps — `IntentClassifier`, `PolicyGate`, `ContextSelector`,
   `AgentSelector`, `ModelSelector`, `ResponseComposer` — each in isolation.
5. `AggregatingModelCatalog` and `RoutingModelGateway` routing decisions.

### 5.2 Integration tests

| Property | Standard |
|---|---|
| Scope | Two or more components across a real boundary |
| Database | A real SQL Server — LocalDB locally; never an in-memory provider |
| State | Each test creates and disposes its own data; no shared fixtures with mutable state |
| Speed | Seconds |
| Naming | `*.IntegrationTests` project (§9) |

**The in-memory EF Core provider is prohibited for integration tests.** It does not enforce foreign
keys, does not evaluate computed columns and does not implement `rowversion` — which means it cannot
exercise the Id/Seq/Ref pattern, the cascade rules, or optimistic concurrency, the three things most
worth testing.

First targets:

1. **The Id/Seq/Ref pattern.** Insert two `Workspace` rows; assert `Ref` is server-generated,
   sequential, unique, and formatted `WKS-00000001`. This converts the `api_run.log` evidence into a
   regression test.
2. **The cascade rules.** Assert that deleting a referenced parent throws rather than cascading —
   the ADR-014 `Restrict` rule (`DATABASE_STANDARDS.md` §5.3).
3. **Concurrency.** Two reads, two writes, assert `DbUpdateConcurrencyException`.
4. **Migration applies cleanly** to an empty database, and `Down` reverses it.
5. **Cross-tenant denial** — see §5.7 and `SECURITY_STANDARDS.md`.

### 5.3 Contract tests

A contract test proves that a published boundary still behaves as its consumers expect. In Nexus,
the boundaries worth testing are the HTTP surfaces (`/api/v1`, `/intelligence/v1`) and the contract
assemblies (`Nexus.Platform.Contracts`, `Nexus.Intelligence.Contracts`).

| What is asserted | Why |
|---|---|
| The route exists and the method is accepted | A renamed route is a silent break |
| Response fields the consumer reads are present and named identically | Rename detection |
| Removed or renamed fields fail the test | `API_STANDARDS.md` §15 breaking-change list |
| Status codes for known failure conditions | A `404` that becomes a `500` breaks error handling |
| Problem Details shape on failure | `API_STANDARDS.md` §7 |

The `Nexus.Web.Client` TypeScript models (`Workspace.ts`, `Project.ts`, `chat.types.ts`) are the
real consumer contract. A response DTO change that those models do not expect is a break regardless
of what any C# test says.

**None exist today.**

### 5.4 Architecture tests

**CURRENT — the only test type with real coverage.** Three files using NetArchTest:
`PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs`, `BoundaryTests.cs`.

What architecture tests must assert:

| Rule | Source |
|---|---|
| A layer depends only on layers below it | `DEVELOPMENT_WORKFLOW.md` §11 |
| Contracts never reference product types — no shared kernel | `DEVELOPMENT_WORKFLOW.md` §11 |
| Products never reference each other | `DEVELOPMENT_WORKFLOW.md` §11 |
| Domain references no EF Core assembly | `DATABASE_STANDARDS.md` §3.5 |
| Value converters exist only in Infrastructure | `DATABASE_STANDARDS.md` §3.5 |
| **A configuration never writes outside its layer's schema** | **TARGET — M-02-1.5** |
| **ASSURANCE never branches on product identity** | **TARGET — M-09-7.1** |
| No `if (Product == X)` anywhere | `DEVELOPMENT_WORKFLOW.md` §11 |

**TARGET — M-08-1.4.** NetArchTest runs in every pipeline as a hard gate, and a pull request
containing a boundary violation cannot merge — demonstrated once with a deliberate violation, then
reverted. Today these tests run only when someone remembers to run them, which is the same as not
running.

### 5.5 UI tests

**TARGET — none exist, and no UI test tooling is selected.**

The React client has zero tests. What is worth testing, when tooling is chosen:

| Level | Target |
|---|---|
| Component | `Card`, `MetricCard`, `RouteErrorBoundary` — including the error path |
| Hook | The TanStack Query hooks: loading, error and success states |
| API client | `ApiClient` and `ApiError` against a stubbed transport, including Problem Details parsing |
| Flow | Create a workspace; select a workspace; send a chat message and see the reply |

Choosing the tooling is not blocked on any milestone. It is blocked on someone deciding, and it is
recorded here as an open decision (§13).

### 5.6 Performance tests

**TARGET.** No performance test, benchmark or baseline exists.

Nothing about performance is currently measurable, so nothing about it is currently assertable. The
useful sequence is: establish baselines first (**M-10-4.2 Capacity and performance baselines**),
then write criteria against them. A performance criterion written before a baseline exists is a
guess with a number in it.

The two areas that will need them first: listing endpoints once pagination exists
(`API_STANDARDS.md` §9), and model invocation latency and cost per turn
(**M-04-4.1 Per-turn cost attribution**).

### 5.7 Security tests

**TARGET.** These are listed here because they are tests; the rules they prove are in
`SECURITY_STANDARDS.md`.

| Test | Milestone | Note |
|---|---|---|
| **A user in tenant A cannot read tenant B data** | M-01-2.1 | **Written BEFORE the implementation** |
| A query omitting the tenant filter fails at build time or throws | M-01-2.1 | Not "returns all tenants" |
| Invalid credentials return 401 and record an audit entry | M-01-1.2 | |
| An expired or revoked token is rejected by every host | M-01-1.2 | |
| A user without a permission receives 403 and an audit entry is recorded | M-01-3.1 | |
| Every Experience and Developer endpoint rejects unauthenticated requests | M-01-3.1 | |
| A known secret pattern is redacted from logs | M-10-1.1 | Unit test |
| A secret scan fails the build on a match | M-01-5.1 | CI gate |

The cross-tenant denial test is written first, watched to fail, and only then made to pass. A test
written after the implementation proves the implementation agrees with itself.

---

## 6. Regression

| Rule | Statement |
|---|---|
| Every fixed defect gets a test | The test fails before the fix and passes after |
| The test is committed with the fix | Not in a follow-up work item |
| Regression suite = the full suite | There is no separate curated regression set |
| Never delete a failing test | Fix it, or record a deviation with a reason |
| Never mark a test ignored silently | An ignored test carries a work item id and a date |

**TARGET — M-09-5.2 Regression qualification** makes regression a qualified activity rather than an
implicit consequence of running the suite.

A repository with two behaviour tests has no regression protection. Every fix from now on adds one,
and that is the mechanism by which the suite becomes real — not a dedicated coverage project.

---

## 7. Assurance profiles

Different product types require different proof. A game, an ERP module and a boring machine do not
share a definition of *adequately verified*.

A profile decides **which methods are mandatory, not which are possible.**

| Profile | Mandatory methods | Rationale |
|---|---|---|
| **Software** | Unit, integration, contract and architecture verification | Correctness is establishable by test; boundaries must be machine-enforced |
| **AI** | Evaluation and citation checking, in addition to software methods | Behaviour is probabilistic; a passing unit test says nothing about answer quality |
| **ERP** | Process validation and user acceptance, in addition to software methods | The risk is that a correct implementation encodes the wrong process |
| **Machine** | Inspection characteristics, measurement and validation | Physical conformance is not testable in software; it is measured |
| **Consumer** | Software methods plus usability and accessibility validation | The user is unsupported and unforgiving; correctness is necessary and insufficient |

**TARGET — M-09-7.1 Profile definition and selection.** Its acceptance criteria are exact:
software, AI, ERP, machine and consumer profiles exist and differ in mandatory methods; **selecting
a profile is a declaration, not a code change**; and an architecture test forbids branching on
product identity inside ASSURANCE.

That third criterion is what keeps profiles from becoming the `if (Product == X)` branching the
architecture forbids everywhere else. A profile is data. ASSURANCE reads which methods are mandatory
and applies them uniformly; it never knows which product it is looking at.

### 7.1 Safety-critical

**TARGET — M-09-7.2, phase P4.** A criterion marked safety-critical:

- cannot be waived by the ordinary deviation path — only by a named human with recorded authority;
- is enumerable per product, with its verification state always current;
- **may not be created, modified or waived by any agent.**

That last rule is absolute and has no exception path. It is stated here so that it is already
written down before the first safety-critical domain arrives, not negotiated when it does.

---

## 8. Machine inspection

**TARGET — M-09-4.1 Inspection plans and characteristics, M-09-4.2 Measurement evidence and
instrument traceability.** Phase P3.

Inspection is verification of a physical characteristic against a specified tolerance. It differs
from a test in three ways that matter for the data model:

| Aspect | Test | Inspection |
|---|---|---|
| Result | Pass or fail | A **measured value**, judged against a tolerance |
| Repeatability | Deterministic | Subject to instrument accuracy and operator variation |
| Traceability | To a commit | To a **calibrated instrument**, with a calibration date |

An inspection record therefore carries the characteristic, the nominal value, the tolerance, the
measured value, the instrument, and the instrument's calibration state. A measurement from an
instrument whose calibration has lapsed is not evidence.

Every unit in an inspection record follows `DATABASE_STANDARDS.md` §11.4 — the column names its
unit or it is wrong. This is the domain where an ambiguous `Length` column causes physical damage
rather than a display bug.

No machine domain exists in Nexus today. This section is written now because retrofitting
measurement traceability into an assurance model designed only for software is expensive, and the
roadmap places machine systems in P5 behind an explicit gate.

---

## 9. Test project structure and naming

### 9.1 The standard

| Suffix | Contains | Scope |
|---|---|---|
| `*.Tests` | Unit tests | One class or behaviour, no I/O |
| `*.IntegrationTests` | Integration tests | Real database, real boundaries |
| `*.Architecture.Tests` | NetArchTest rules | Assembly and namespace structure |
| `*.Assurance.Tests` | Acceptance criteria executed as automated verification | Criterion-linked, evidence-producing |

One test project per production project it targets, named by prefixing the production project's
name: `Nexus.Platform.Core` → `Nexus.Platform.Core.Tests`.

### 9.2 Folder structure inside a test project

Mirror the production project's folders. This is already how the existing tests are laid out:
`Ranking/KeywordContextRankerTests.cs` sits under `Ranking/` because
`Nexus.Intelligence.Context/Ranking/` is where `KeywordContextRanker` lives; `Chat/
ChatContextBundleMapperTests.cs` likewise.

### 9.3 What to actually do, given six projects and five files

**Do not create new test projects to satisfy a naming table.** An empty `.csproj` is worse than no
project: `Nexus.Platform.Tests` has existed with zero tests, and its presence has made the system
look tested for as long as it has existed.

The recommendation:

| Action | Justification |
|---|---|
| **Put tests in `Nexus.Platform.Tests`** | The project exists and is empty. Filling it costs nothing and removes a lie |
| **Keep the three `*.Architecture.Tests` projects** | They are real, they work, and they are the only enforcement that exists |
| **Add `*.IntegrationTests` only when the first integration test is written** | Not before |
| **Add `*.Assurance.Tests` only when M-09-1.1 gives criteria to link to** | An assurance test with nothing to trace to is a unit test with a longer name |
| **Do not add a UI test project until the tooling is chosen** | §13 |

The clean standard in §9.1 is the target. The path to it is by writing tests, not by creating
projects.

---

## 10. Evidence

**TARGET — M-09-1.2 Evidence and verdict.**

Evidence is a stored artefact that someone else can inspect and reach the same conclusion from.

| Acceptable evidence | Not evidence |
|---|---|
| A DELIVERY PipelineRun reference | "It builds on my machine" |
| A test run record with counts and outcome | "The tests pass" |
| A log excerpt with a correlation id | A screenshot with no context |
| An inspection record with instrument and calibration | A measurement with no instrument |
| An evaluation run with its score and question set | "The answers looked good" |
| A witnessed demonstration record with date and witness | An undated claim of a demonstration |
| A document reference or uploaded artefact | A pull request description |

Evidence is immutable once linked to a verdict. A re-run produces new evidence and a new verification
run; it never overwrites the old one, because the history of what was believed and when is itself
information.

**A verdict of Pass without linked evidence is rejected at write time.** This is a database
constraint on a verdict record, not a review convention.

---

## 11. Defects, nonconformance and deviations

Three distinct records with three distinct meanings. Conflating them loses the reason a system was
released in a state somebody knew about.

| Record | Meaning | Milestone |
|---|---|---|
| **Defect** | The system does not do what was specified | M-09-2.1 Defect lifecycle |
| **Nonconformance** | The *process* was not followed | M-09-2.2 |
| **Deviation** | A known departure, accepted deliberately, with authority and an expiry | M-09-2.2 |

### 11.1 Defect

A defect record carries: what was observed, what was expected, how to reproduce, the affected
criterion, severity, and — once fixed — the regression test that proves it.

Defect severity:

| Severity | Definition | Effect on a release |
|---|---|---|
| Critical | Data loss, security failure, or the system is unusable | Blocks |
| Major | A required behaviour is wrong; no acceptable workaround | Blocks |
| Minor | Wrong behaviour with a workaround | Recorded as a deviation if released |
| Cosmetic | No functional impact | Does not block |

A production defect found in state 16 becomes a **new work item**, not a reopened one — see
`DEVELOPMENT_WORKFLOW.md` §2.3. The original item's history stays true.

### 11.2 Nonconformance

A nonconformance is raised when the process was bypassed: a merge without review, a release without
qualification, a migration edited after being pushed, a test deleted rather than fixed. It is not a
disciplinary record; it is a signal that a control failed and needs a corrective action.

The 2026-08-20 incident produced one that is still open: **the recommended antivirus exclusion has
never been confirmed.** Its corrective action is **M-08-2.1**, and the nonconformance stays open
until the evidence is recorded. See `GIT_WORKFLOW.md` §2.

### 11.3 Deviation

A deviation records: the criterion, why it is not met, who accepted it, the authority under which
they accepted it, and the date it expires. A deviation with no expiry is a silent change of
standard.

**A safety-critical criterion cannot be waived by a deviation** (§7.1).

---

## 12. Quality gates and release qualification

### 12.1 Quality gate V1

**TARGET — M-09-1.3 Quality gate V1.** The minimum gate, and the Foundation Gate's assurance
requirement:

> **A work item cannot integrate while a mandatory acceptance criterion is unverified.**

Evaluated at `DEVELOPMENT_WORKFLOW.md` state 12, before state 13 review and state 14 integration.
The gate needs only four things to be real: criteria, methods, evidence and a verdict. That is why
M-09-1.1 and M-09-1.2 are deliberately small.

### 12.2 Gate evaluation

| Input | Source | Blocking |
|---|---|---|
| Build outcome | DELIVERY pipeline run | Yes |
| Architecture tests | NetArchTest in the pipeline (M-08-1.4) | Yes |
| Test outcome | DELIVERY test record | Yes |
| Mandatory criteria verified | ASSURANCE verification runs | Yes |
| Traceability gaps | ASSURANCE gap report | Yes — a work item with no criterion does not pass |
| Open critical or major defects | Defect records | Yes |
| Open deviations | Deviation records | No, but they are listed on the release |

### 12.3 Release qualification

**TARGET — M-09-5.1 Release qualification.** A release is qualified when every mandatory criterion
in its scope has a Pass verdict with linked evidence, every critical and major defect is closed or
deviated, the regression suite has run and passed (M-09-5.2), and the profile's mandatory methods
(§7) have all been applied.

The output is a stored qualification record naming what was qualified, against which criteria, on
which evidence, by whom, and when. A release without one is unqualified — which is a describable
state, not a forbidden one, but it must be visible rather than assumed.

**Nexus has never released. There is nothing qualified today.**

---

## 13. AI evaluation

AI behaviour cannot be verified by unit test. A model invocation is not deterministic and its
output space is not enumerable, so the method is **Evaluation**: score behaviour against a question
set and a threshold.

**TARGET — M-04-5.1 Evaluation harness** builds the mechanism; **M-09-6.1 Evaluation as a
verification method** makes its output count as evidence:

| Rule | Statement |
|---|---|
| An evaluation run writes Evidence with its score and its question set | The question set is part of the evidence, not an implementation detail |
| A criterion can require a minimum score | And a drop below it fails the gate |
| **Citation correctness is expressible as a criterion** | Not a subjective judgement |

Citation correctness is the highest-value AI criterion in Nexus, because the contracts already carry
the structure to check it: `Citation` in `Nexus.Intelligence.Contracts/Context/`, and
`CitationsPanel.tsx`, `citationTargets.ts` and `useCitationTarget.ts` on the frontend. A citation
that points at nothing is mechanically detectable. **M-04-2.1 Citations proven against a live model**
is a P0 exit criterion.

What must never happen: an evaluation score being treated as a pass without a threshold declared in
advance. A score chosen after seeing the result is not a criterion.

---

## 14. Open decisions

| Question | What would decide it | State |
|---|---|---|
| UI test tooling | Someone choosing; not blocked on a milestone | Not yet decided |
| Performance test tooling | Baselines first (M-10-4.2), then tooling | Not yet decided |
| Coverage thresholds | Meaningful only once coverage exists at all | Not yet decided — deliberately |
| Test data strategy for integration tests | The first integration test against LocalDB | Not yet decided |
| Whether `Nexus.Platform.Tests` keeps its name or is replaced | Whichever comes first: tests, or the repository rename | Not yet decided |
| Mutation testing | Not raised | Not yet decided |

Coverage thresholds are listed as deliberately undecided. Setting a threshold against two behaviour
tests would produce either an unmeetable target or a meaningless one, and coverage percentage is in
any case a proxy for the question ASSURANCE actually asks.

---

## 15. The honest summary

Nexus has a well-defined assurance model and almost no assurance. Three architecture test files
enforce real boundaries when someone runs them. Two behaviour tests exist. One project claims to
hold tests and holds none. Nothing runs automatically, nothing is traced to a requirement, and
nothing has ever been qualified.

The first three things that change that, in order:

1. **Any CI at all** (M-08-1.2) — until something runs the tests, the tests are decorative.
2. **The Id/Seq/Ref integration test** — it converts the strongest piece of evidence in the system
   from a log line into a regression net.
3. **The cross-tenant denial test, written before the implementation** (M-01-2.1) — because it is
   the one test whose absence is a security exposure rather than a quality risk.

---

## 16. References

- `DEVELOPMENT_WORKFLOW.md` — the state model, the four-layer test ownership split, parallel safety.
- `SECURITY_STANDARDS.md` — the rules the security tests prove.
- `DATABASE_STANDARDS.md` — Id/Seq/Ref, cascade rules and concurrency: what the integration tests assert.
- `API_STANDARDS.md` — the contract surface the contract tests protect.
- `GIT_WORKFLOW.md` — the evidence a pull request must carry; the open nonconformance from 2026-08-20.
- `CONFIGURATION_STANDARDS.md` — test environment configuration.
